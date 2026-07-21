namespace Client.Main.Diagnostics;

public sealed class TelemetryPublisherOptions
{
    public bool Enabled { get; set; } = false;
    public string PipeName { get; set; } = Client.Telemetry.TelemetryProtocol.DefaultPipeName;
    public int SampleIntervalMs { get; set; } = 200;
    public int ReconnectDelayMs { get; set; } = 1500;
    public int QueueCapacity { get; set; } = 4;
    public string DashboardUrl { get; set; } = "http://127.0.0.1:5078";

    internal void Normalize()
    {
        PipeName = string.IsNullOrWhiteSpace(PipeName)
            ? Client.Telemetry.TelemetryProtocol.DefaultPipeName
            : PipeName.Trim();
        SampleIntervalMs = Math.Clamp(SampleIntervalMs, 100, 5000);
        ReconnectDelayMs = Math.Clamp(ReconnectDelayMs, 250, 30_000);
        QueueCapacity = Math.Clamp(QueueCapacity, 1, 64);
        DashboardUrl = string.IsNullOrWhiteSpace(DashboardUrl) ? "http://127.0.0.1:5078" : DashboardUrl.TrimEnd('/');
    }
}
