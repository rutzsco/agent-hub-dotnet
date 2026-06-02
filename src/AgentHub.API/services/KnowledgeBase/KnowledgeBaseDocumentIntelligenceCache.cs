using Azure;
using Azure.Storage.Blobs;
using Newtonsoft.Json;

namespace AgentHub.API.Services.KnowledgeBase;

public sealed class KnowledgeBaseDocumentIntelligenceCache
{
    private readonly BlobContainerClient _containerClient;
    private readonly ILogger<KnowledgeBaseDocumentIntelligenceCache> _logger;

    public KnowledgeBaseDocumentIntelligenceCache(
        BlobContainerClient containerClient,
        ILogger<KnowledgeBaseDocumentIntelligenceCache> logger)
    {
        _containerClient = containerClient;
        _logger = logger;
    }

    public async Task<IReadOnlyList<ExtractedPdfPage>?> TryGetPagesAsync(
        KnowledgeBaseBlobDocument document,
        CancellationToken cancellationToken)
    {
        var blobClient = _containerClient.GetBlobClient(GetCacheBlobName(document));
        try
        {
            var response = await blobClient.DownloadContentAsync(cancellationToken).ConfigureAwait(false);
            var cached = JsonConvert.DeserializeObject<CachedDocumentIntelligenceResult>(response.Value.Content.ToString());
            if (cached is null || !cached.Matches(document))
            {
                return null;
            }

            _logger.LogInformation("Reusing cached Document Intelligence result. BlobPath={BlobPath}", document.BlobPath);
            return cached.Pages;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public async Task StorePagesAsync(
        KnowledgeBaseBlobDocument document,
        IReadOnlyList<ExtractedPdfPage> pages,
        CancellationToken cancellationToken)
    {
        await _containerClient.CreateIfNotExistsAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        var cached = new CachedDocumentIntelligenceResult(
            document.BlobPath,
            document.LastModified,
            document.SizeBytes,
            DateTimeOffset.UtcNow,
            pages);
        var json = JsonConvert.SerializeObject(cached);
        var blobClient = _containerClient.GetBlobClient(GetCacheBlobName(document));
        await blobClient.UploadAsync(BinaryData.FromString(json), overwrite: true, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("Cached Document Intelligence result. BlobPath={BlobPath}", document.BlobPath);
    }

    private static string GetCacheBlobName(KnowledgeBaseBlobDocument document)
    {
        return $"document-intelligence/{document.ParentId}.json";
    }

    private sealed record CachedDocumentIntelligenceResult(
        string BlobPath,
        DateTimeOffset? BlobLastModified,
        long? SizeBytes,
        DateTimeOffset CachedOn,
        IReadOnlyList<ExtractedPdfPage> Pages)
    {
        public bool Matches(KnowledgeBaseBlobDocument document)
        {
            return string.Equals(BlobPath, document.BlobPath, StringComparison.Ordinal)
                && BlobLastModified == document.LastModified
                && SizeBytes == document.SizeBytes
                && Pages.Count > 0;
        }
    }
}