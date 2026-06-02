using AgentHub.API;
using AgentHub.API.Routes;
using AgentHub.API.services.search;
using Azure.Search.Documents.Indexes;

var builder = WebApplication.CreateBuilder(args);

var settings = Settings.Load(builder.Configuration);

builder.Logging.ClearProviders();
builder.Logging.AddConsole(options =>
{
    options.FormatterName = "simple";
});
builder.Logging.AddSimpleConsole(options =>
{
    options.IncludeScopes = true;
    options.SingleLine = false;
    options.TimestampFormat = "yyyy-MM-dd HH:mm:ss.fff zzz ";
});

builder.Services.AddHealthChecks();
builder.Services.AddOpenApi();
builder.Services.AddAgentHubServices(settings);

var app = builder.Build();

var startupLogger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("AgentHub.Startup");
startupLogger.LogInformation(
    "Application starting. Environment={Environment}, FoundryEndpoint={FoundryEndpoint}, ModelDeployment={ModelDeployment}, FoundryAgentName={FoundryAgentName}",
    app.Environment.EnvironmentName,
    settings.AzureAIProjectEndpoint,
    settings.AzureAIModelDeploymentName,
    settings.FoundryAgentName ?? AgentHub.API.Agents.FoundryDemoAgent.DefaultName);

var searchIndexClient = app.Services.GetService<SearchIndexClient>();
if (searchIndexClient is not null)
{
    try
    {
        await LeanSearchIndex.EnsureCreatedAsync(searchIndexClient);
        startupLogger.LogInformation("Azure AI Search index '{IndexName}' ensured.", LeanSearchIndex.IndexName);
    }
    catch (Exception ex)
    {
        startupLogger.LogError(ex, "Failed to ensure Azure AI Search index '{IndexName}'.", LeanSearchIndex.IndexName);
    }
}
else
{
    startupLogger.LogInformation("Azure AI Search not configured (AgentHub:AzureSearchEndpoint missing); skipping index creation.");
}

app.Use(async (context, next) =>
{
    var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("AgentHub.Request");
    var startedAt = DateTime.UtcNow;

    logger.LogInformation("Request started. Method={Method}, Path={Path}, TraceId={TraceId}",
        context.Request.Method,
        context.Request.Path,
        context.TraceIdentifier);

    await next();

    var durationMs = (DateTime.UtcNow - startedAt).TotalMilliseconds;
    logger.LogInformation("Request completed. Method={Method}, Path={Path}, StatusCode={StatusCode}, DurationMs={DurationMs}, TraceId={TraceId}",
        context.Request.Method,
        context.Request.Path,
        context.Response.StatusCode,
        durationMs,
        context.TraceIdentifier);
});

app.MapHealthChecks("/health");
app.MapOpenApi();
app.UseDefaultFiles(new DefaultFilesOptions
{
    DefaultFileNames = new List<string> { "event-charter.html", "index.html" }
});
app.UseStaticFiles();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/openapi/v1.json", "AgentHub API");
});
app.MapAgentRoutes();
app.MapKnowledgeBaseRoutes();

app.Run();
