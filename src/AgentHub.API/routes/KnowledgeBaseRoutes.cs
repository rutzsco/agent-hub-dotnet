using AgentHub.API.Services.KnowledgeBase;

namespace AgentHub.API.Routes;

public static class KnowledgeBaseRoutes
{
    public static WebApplication MapKnowledgeBaseRoutes(this WebApplication app)
    {
        app.MapPost("/knowledge-base/ingest", async (
            KnowledgeBaseIngestRequest request,
            IServiceProvider serviceProvider,
            CancellationToken cancellationToken) =>
        {
            var service = serviceProvider.GetService<KnowledgeBaseIngestionService>();
            if (service is null)
            {
                return KnowledgeBaseNotConfigured();
            }

            if (request.MaxFiles is <= 0)
            {
                return Results.BadRequest("maxFiles must be greater than zero.");
            }

            var result = await service.IngestAsync(
                request.BlobPrefix,
                request.MaxFiles,
                request.ForceReindex,
                cancellationToken);
            return Results.Ok(result);
        });

        app.MapPost("/knowledge-base/search", async (
            KnowledgeBaseSearchRequest request,
            IServiceProvider serviceProvider,
            CancellationToken cancellationToken) =>
        {
            var service = serviceProvider.GetService<KnowledgeBaseSearchService>();
            if (service is null)
            {
                return KnowledgeBaseNotConfigured();
            }

            if (string.IsNullOrWhiteSpace(request.Query))
            {
                return Results.BadRequest("query is required.");
            }

            if (request.TopK is <= 0)
            {
                return Results.BadRequest("topK must be greater than zero.");
            }

            var filter = new KnowledgeBaseSearchFilter(
                request.Category,
                request.Subcategory,
                request.DocumentType,
                request.BlobPath,
                request.BlobPrefix,
                request.FileName);

            var result = await service.SearchAsync(request.Query, request.TopK ?? 5, filter, cancellationToken);
            return Results.Ok(new KnowledgeBaseSearchResponse(result));
        });

        return app;
    }

    private static IResult KnowledgeBaseNotConfigured()
    {
        return Results.Problem(
            title: "KnowledgeBase support is not configured",
            detail: "Set Cosmos, AgentHub:AzureOpenAIEndpoint, AgentHub:KnowledgeBase:BlobContainerUri, and AgentHub:KnowledgeBase:DocumentIntelligenceEndpoint to enable KnowledgeBase endpoints.",
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }
}

public sealed record KnowledgeBaseIngestRequest(
    string? BlobPrefix = null,
    int? MaxFiles = null,
    bool ForceReindex = false);

public sealed record KnowledgeBaseSearchRequest(
    string Query,
    int? TopK = null,
    string? Category = null,
    string? Subcategory = null,
    string? DocumentType = null,
    string? BlobPath = null,
    string? BlobPrefix = null,
    string? FileName = null);

public sealed record KnowledgeBaseSearchResponse(IReadOnlyList<KnowledgeBaseSearchHit> Results);