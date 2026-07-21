using System.IO.Pipes;
using System.Text.Json;
using Client.Telemetry;

namespace MuDiagnostics.Web.Services;

public sealed class PipeIngestService : BackgroundService
{
    private readonly DiagnosticsServerOptions _options;
    private readonly TelemetryStore _store;
    private readonly LiveTelemetryBroker _broker;
    private readonly TelemetryAlertEngine _alerts;
    private readonly ILogger<PipeIngestService> _logger;

    public PipeIngestService(
        DiagnosticsServerOptions options,
        TelemetryStore store,
        LiveTelemetryBroker broker,
        TelemetryAlertEngine alerts,
        ILogger<PipeIngestService> logger)
    {
        _options = options;
        _store = store;
        _broker = broker;
        _alerts = alerts;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Listening for game telemetry on named pipe {PipeName}", _options.PipeName);

        while (!stoppingToken.IsCancellationRequested)
        {
            NamedPipeServerStream? pipe = null;
            try
            {
                pipe = new NamedPipeServerStream(
                    _options.PipeName,
                    PipeDirection.In,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                await pipe.WaitForConnectionAsync(stoppingToken).ConfigureAwait(false);
                _ = HandleClientAsync(pipe, stoppingToken);
                pipe = null;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Named-pipe accept loop failed");
                await Task.Delay(1000, stoppingToken).ConfigureAwait(false);
            }
            finally
            {
                pipe?.Dispose();
            }
        }
    }

    private async Task HandleClientAsync(NamedPipeServerStream pipe, CancellationToken stoppingToken)
    {
        string? sessionId = null;
        try
        {
            using (pipe)
            using (var reader = new StreamReader(pipe))
            {
                while (!stoppingToken.IsCancellationRequested && pipe.IsConnected)
                {
                    string? line = await reader.ReadLineAsync(stoppingToken).ConfigureAwait(false);
                    if (line is null)
                        break;
                    if (line.Length == 0 || line.Length > 2_000_000)
                        continue;

                    TelemetryEnvelope? envelope;
                    try
                    {
                        envelope = JsonSerializer.Deserialize<TelemetryEnvelope>(line, TelemetryProtocol.JsonOptions);
                    }
                    catch (JsonException ex)
                    {
                        _logger.LogWarning(ex, "Discarding malformed telemetry message");
                        continue;
                    }

                    if (envelope is null || envelope.ProtocolVersion != TelemetryProtocol.CurrentVersion)
                        continue;

                    sessionId = envelope.SessionId;
                    if (envelope.Kind == TelemetryMessageKind.Hello)
                    {
                        _store.SetConnected(envelope);
                        _broker.Publish(envelope);
                        _logger.LogInformation("Game client connected: PID={ProcessId}, Session={SessionId}", envelope.Client?.ProcessId, envelope.SessionId);
                        continue;
                    }

                    _store.Add(envelope);
                    _broker.Publish(envelope);

                    if (envelope.Kind == TelemetryMessageKind.Snapshot)
                    {
                        foreach (var alert in _alerts.Evaluate(envelope))
                        {
                            if (alert.Event is null)
                                continue;
                            _store.Add(alert);
                            _broker.Publish(alert);
                        }
                    }
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (IOException ex)
        {
            _logger.LogDebug(ex, "Game telemetry pipe disconnected");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Game telemetry pipe failed");
        }
        finally
        {
            _store.SetDisconnected(sessionId);
            _logger.LogInformation("Game client disconnected: Session={SessionId}", sessionId ?? "unknown");
        }
    }
}
