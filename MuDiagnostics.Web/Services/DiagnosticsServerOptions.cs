using Client.Telemetry;

namespace MuDiagnostics.Web.Services;

public sealed class DiagnosticsServerOptions
{
    public string PipeName { get; set; } = TelemetryProtocol.DefaultPipeName;
    public int HistoryMinutes { get; set; } = 30;
    public int MaxEvents { get; set; } = 1000;
    public bool OpenBrowserOnStart { get; set; } = true;
    public string DashboardUrl { get; set; } = "http://127.0.0.1:5078";

    public void Normalize()
    {
        PipeName = string.IsNullOrWhiteSpace(PipeName) ? TelemetryProtocol.DefaultPipeName : PipeName.Trim();
        HistoryMinutes = Math.Clamp(HistoryMinutes, 1, 24 * 60);
        MaxEvents = Math.Clamp(MaxEvents, 100, 100_000);
        DashboardUrl = string.IsNullOrWhiteSpace(DashboardUrl)
            ? "http://127.0.0.1:5078"
            : DashboardUrl.TrimEnd('/');
    }
}
