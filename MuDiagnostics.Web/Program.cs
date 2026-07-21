using System.Text;
using System.Text.Json;
using Client.Telemetry;
using Microsoft.AspNetCore.Http.Features;
using MuDiagnostics.Web.Services;

var builder = WebApplication.CreateBuilder(args);

var options = builder.Configuration.GetSection("DiagnosticsServer").Get<DiagnosticsServerOptions>()
    ?? new DiagnosticsServerOptions();
options.Normalize();

builder.Services.AddSingleton(options);
builder.Services.AddSingleton<TelemetryStore>();
builder.Services.AddSingleton<LiveTelemetryBroker>();
builder.Services.AddSingleton<TelemetryAlertEngine>();
builder.Services.AddSingleton<TelemetryAnalysisService>();
builder.Services.AddHostedService<PipeIngestService>();
builder.Services.AddHostedService<BrowserLauncherService>();

builder.Services.ConfigureHttpJsonOptions(json =>
{
    json.SerializerOptions.PropertyNamingPolicy = TelemetryProtocol.JsonOptions.PropertyNamingPolicy;
    json.SerializerOptions.DictionaryKeyPolicy = TelemetryProtocol.JsonOptions.DictionaryKeyPolicy;
    json.SerializerOptions.DefaultIgnoreCondition = TelemetryProtocol.JsonOptions.DefaultIgnoreCondition;
    foreach (var converter in TelemetryProtocol.JsonOptions.Converters)
        json.SerializerOptions.Converters.Add(converter);
});

var app = builder.Build();

app.Use(async (context, next) =>
{
    context.Response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate";
    context.Response.Headers["Pragma"] = "no-cache";
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["Content-Security-Policy"] =
        "default-src 'self'; style-src 'self'; script-src 'self'; img-src 'self' data:; connect-src 'self'; object-src 'none'; base-uri 'none'; frame-ancestors 'none'";
    await next().ConfigureAwait(false);
});

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/status", (TelemetryStore store) => Results.Json(store.GetStatus(), TelemetryProtocol.JsonOptions));

app.MapGet("/api/history", (TelemetryStore store, int? seconds) =>
{
    int requestedSeconds = Math.Clamp(seconds ?? 300, 5, 24 * 60 * 60);
    return Results.Json(store.GetHistory(TimeSpan.FromSeconds(requestedSeconds)), TelemetryProtocol.JsonOptions);
});

app.MapGet("/api/events", (TelemetryStore store, int? limit) =>
    Results.Json(store.GetEvents(Math.Clamp(limit ?? 200, 1, 10_000)), TelemetryProtocol.JsonOptions));


app.MapGet("/api/analysis", (TelemetryStore store, TelemetryAnalysisService analysis, int? seconds) =>
{
    int requestedSeconds = Math.Clamp(seconds ?? 300, 5, 24 * 60 * 60);
    return Results.Json(
        analysis.Analyze(store.GetHistory(TimeSpan.FromSeconds(requestedSeconds))),
        TelemetryProtocol.JsonOptions);
});

app.MapGet("/api/report.md", (TelemetryStore store, TelemetryAnalysisService analysis, int? seconds) =>
{
    int requestedSeconds = Math.Clamp(seconds ?? 1800, 5, 24 * 60 * 60);
    var report = analysis.Analyze(store.GetHistory(TimeSpan.FromSeconds(requestedSeconds)));
    return Results.Text(analysis.ToMarkdown(report), "text/markdown; charset=utf-8");
});

app.MapPost("/api/analyze.csv", async (HttpRequest request, TelemetryAnalysisService analysis, CancellationToken cancellationToken) =>
{
    if (!request.HasFormContentType)
        return Results.BadRequest(new { error = "multipart/form-data with a CSV file is required" });

    IFormCollection form = await request.ReadFormAsync(cancellationToken).ConfigureAwait(false);
    IFormFile? file = form.Files.GetFile("file") ?? form.Files.FirstOrDefault();
    if (file is null || file.Length <= 0)
        return Results.BadRequest(new { error = "CSV file is empty or missing" });
    if (file.Length > 32L * 1024L * 1024L)
        return Results.BadRequest(new { error = "CSV file exceeds the 32 MB limit" });

    await using Stream stream = file.OpenReadStream();
    var report = await analysis.AnalyzeCsvAsync(stream, file.FileName, cancellationToken).ConfigureAwait(false);
    return Results.Json(report, TelemetryProtocol.JsonOptions);
}).DisableAntiforgery();

app.MapPost("/api/session/reset", (TelemetryStore store) =>
{
    store.Reset();
    return Results.NoContent();
});

app.MapGet("/api/export.csv", (TelemetryStore store, int? seconds) =>
{
    int requestedSeconds = Math.Clamp(seconds ?? 1800, 5, 24 * 60 * 60);
    string csv = store.ExportCsv(TimeSpan.FromSeconds(requestedSeconds));
    string filename = $"muonline-diagnostics-{DateTime.UtcNow:yyyyMMdd-HHmmss}.csv";
    return Results.File(Encoding.UTF8.GetBytes(csv), "text/csv; charset=utf-8", filename);
});

app.MapGet("/api/live", async (HttpContext context, LiveTelemetryBroker broker) =>
{
    context.Response.ContentType = "text/event-stream";
    context.Response.Headers["Cache-Control"] = "no-cache";
    context.Response.Headers["Connection"] = "keep-alive";
    context.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();

    await using var subscription = broker.Subscribe();
    CancellationToken cancellationToken = context.RequestAborted;

    while (!cancellationToken.IsCancellationRequested)
    {
        using var heartbeatCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        heartbeatCts.CancelAfter(TimeSpan.FromSeconds(10));

        bool hasData;
        try
        {
            hasData = await subscription.Reader.WaitToReadAsync(heartbeatCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            await context.Response.WriteAsync(": heartbeat\n\n", cancellationToken).ConfigureAwait(false);
            await context.Response.Body.FlushAsync(cancellationToken).ConfigureAwait(false);
            continue;
        }

        if (!hasData)
            break;

        while (subscription.Reader.TryRead(out var envelope))
        {
            string json = JsonSerializer.Serialize(envelope, TelemetryProtocol.JsonOptions);
            await context.Response.WriteAsync($"data: {json}\n\n", cancellationToken).ConfigureAwait(false);
        }
        await context.Response.Body.FlushAsync(cancellationToken).ConfigureAwait(false);
    }
});

app.MapGet("/api/health", (TelemetryStore store) =>
{
    var status = store.GetStatus();
    return Results.Json(new
    {
        service = "ok",
        clientConnected = status.PipeConnected,
        lastReceivedUtc = status.LastReceivedUtc,
        protocolVersion = TelemetryProtocol.CurrentVersion
    });
});

app.MapFallbackToFile("index.html");

app.Logger.LogInformation("MU Online diagnostics dashboard: {Url}", options.DashboardUrl);
app.Logger.LogInformation("Named pipe: {PipeName}", options.PipeName);

await app.RunAsync();
