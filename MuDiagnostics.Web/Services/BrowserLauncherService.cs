using System.Diagnostics;

namespace MuDiagnostics.Web.Services;

public sealed class BrowserLauncherService : IHostedService
{
    private readonly DiagnosticsServerOptions _options;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly ILogger<BrowserLauncherService> _logger;

    public BrowserLauncherService(
        DiagnosticsServerOptions options,
        IHostApplicationLifetime lifetime,
        ILogger<BrowserLauncherService> logger)
    {
        _options = options;
        _lifetime = lifetime;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_options.OpenBrowserOnStart || Environment.GetCommandLineArgs().Any(x => string.Equals(x, "--no-browser", StringComparison.OrdinalIgnoreCase)))
            return Task.CompletedTask;

        _lifetime.ApplicationStarted.Register(() =>
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = _options.DashboardUrl,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not open the diagnostics dashboard automatically");
            }
        });
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
