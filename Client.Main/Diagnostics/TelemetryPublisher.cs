#nullable enable annotations

using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Channels;
using Client.Telemetry;
using Microsoft.Extensions.Logging;

namespace Client.Main.Diagnostics;

public sealed class TelemetryPublisher : IAsyncDisposable
{
    private readonly TelemetryPublisherOptions _options;
    private readonly ILogger _logger;
    private readonly Channel<TelemetryEnvelope> _queue;
    private readonly CancellationTokenSource _cts = new();
    private readonly TelemetrySnapshotBuilder _snapshotBuilder = new();
    private readonly string _sessionId = Guid.NewGuid().ToString("N");
    private readonly TelemetryClientInfo _clientInfo;
    private Task? _worker;
    private long _nextSampleTimestamp;
    private int _started;
    private int _disposed;
    private int _connected;
    private long _droppedMessages;
    private string? _lastError;

    public TelemetryPublisher(TelemetryPublisherOptions options, ILogger logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _options.Normalize();
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _queue = Channel.CreateBounded<TelemetryEnvelope>(
            new BoundedChannelOptions(_options.QueueCapacity)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = false,
                AllowSynchronousContinuations = false
            },
            _ => Interlocked.Increment(ref _droppedMessages));

        using var process = Process.GetCurrentProcess();
        _clientInfo = new TelemetryClientInfo
        {
            ProcessName = process.ProcessName,
            MachineName = Environment.MachineName,
            Framework = RuntimeInformation.FrameworkDescription,
            OperatingSystem = RuntimeInformation.OSDescription,
            Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            ClientVersion = typeof(MuGame).Assembly.GetName().Version?.ToString() ?? "unknown",
            ProcessId = Environment.ProcessId,
            ProcessorCount = Environment.ProcessorCount,
            StartedUtc = process.StartTime.ToUniversalTime()
        };
    }

    public bool Enabled => _options.Enabled;
    public bool IsConnected => Volatile.Read(ref _connected) != 0;
    public string PipeName => _options.PipeName;
    public string DashboardUrl => _options.DashboardUrl;
    public long DroppedMessages => Interlocked.Read(ref _droppedMessages);
    public string? LastError => Volatile.Read(ref _lastError);

    public void Start()
    {
        if (!_options.Enabled || Interlocked.Exchange(ref _started, 1) != 0)
            return;

        _nextSampleTimestamp = Stopwatch.GetTimestamp();
        _worker = Task.Run(() => RunAsync(_cts.Token));
    }

    public bool TryBeginSnapshot()
    {
        if (!_options.Enabled || Volatile.Read(ref _started) == 0 || !IsConnected)
            return false;

        long now = Stopwatch.GetTimestamp();
        if (now < Volatile.Read(ref _nextSampleTimestamp))
            return false;

        long intervalTicks = Math.Max(1L, (long)(_options.SampleIntervalMs / 1000d * Stopwatch.Frequency));
        Volatile.Write(ref _nextSampleTimestamp, now + intervalTicks);
        return true;
    }

    public void PublishSnapshot(MuGame game)
    {
        try
        {
            var envelope = new TelemetryEnvelope
            {
                Kind = TelemetryMessageKind.Snapshot,
                SessionId = _sessionId,
                TimestampUtc = DateTimeOffset.UtcNow,
                Snapshot = _snapshotBuilder.Build(game, DroppedMessages)
            };
            Enqueue(envelope);
        }
        catch (Exception ex)
        {
            Volatile.Write(ref _lastError, ex.Message);
            _logger.LogDebug(ex, "Failed to create diagnostics snapshot");
        }
    }

    public void PublishEvent(
        string category,
        string message,
        TelemetrySeverity severity = TelemetrySeverity.Info,
        IReadOnlyDictionary<string, string>? properties = null)
    {
        if (!_options.Enabled || Volatile.Read(ref _started) == 0 || string.IsNullOrWhiteSpace(message))
            return;

        Enqueue(new TelemetryEnvelope
        {
            Kind = TelemetryMessageKind.Event,
            SessionId = _sessionId,
            TimestampUtc = DateTimeOffset.UtcNow,
            Event = new TelemetryEvent
            {
                Category = string.IsNullOrWhiteSpace(category) ? "client" : category,
                Message = message,
                Severity = severity,
                Properties = properties
            }
        });
    }

    private void Enqueue(TelemetryEnvelope envelope)
    {
        if (!_queue.Writer.TryWrite(envelope))
            Interlocked.Increment(ref _droppedMessages);
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await using var pipe = new NamedPipeClientStream(
                    serverName: ".",
                    pipeName: _options.PipeName,
                    direction: PipeDirection.Out,
                    options: PipeOptions.Asynchronous);

                await pipe.ConnectAsync(750, cancellationToken).ConfigureAwait(false);
                Volatile.Write(ref _connected, 1);
                Volatile.Write(ref _lastError, null);

                await using var writer = new StreamWriter(pipe)
                {
                    AutoFlush = true,
                    NewLine = "\n"
                };

                await WriteEnvelopeAsync(writer, new TelemetryEnvelope
                {
                    Kind = TelemetryMessageKind.Hello,
                    SessionId = _sessionId,
                    TimestampUtc = DateTimeOffset.UtcNow,
                    Client = _clientInfo
                }, cancellationToken).ConfigureAwait(false);

                while (await _queue.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    while (_queue.Reader.TryRead(out var envelope))
                        await WriteEnvelopeAsync(writer, envelope, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                Volatile.Write(ref _lastError, ex.Message);
                _logger.LogDebug(ex, "Diagnostics service is unavailable; retrying in {Delay} ms", _options.ReconnectDelayMs);
            }
            finally
            {
                Volatile.Write(ref _connected, 0);
            }

            try
            {
                await Task.Delay(_options.ReconnectDelayMs, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private static async Task WriteEnvelopeAsync(
        StreamWriter writer,
        TelemetryEnvelope envelope,
        CancellationToken cancellationToken)
    {
        string json = JsonSerializer.Serialize(envelope, TelemetryProtocol.JsonOptions);
        await writer.WriteLineAsync(json.AsMemory(), cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        Interlocked.Exchange(ref _started, 0);
        _queue.Writer.TryComplete();
        _cts.Cancel();

        if (_worker is not null)
        {
            try
            {
                await _worker.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        _snapshotBuilder.Dispose();
        _cts.Dispose();
    }
}
