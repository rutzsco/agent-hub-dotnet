using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace AgentHub.API.Services.KnowledgeBase;

public sealed class KnowledgeBaseBlobSource
{
    private readonly BlobContainerClient _containerClient;
    private readonly KnowledgeBaseOptions _options;
    private readonly ILogger<KnowledgeBaseBlobSource> _logger;

    public KnowledgeBaseBlobSource(
        BlobContainerClient containerClient,
        KnowledgeBaseOptions options,
        ILogger<KnowledgeBaseBlobSource> logger)
    {
        _containerClient = containerClient;
        _options = options;
        _logger = logger;
    }

    public async IAsyncEnumerable<KnowledgeBaseBlobDocument> GetPdfDocumentsAsync(
        string? blobPrefix,
        int? maxFiles,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var prefix = string.IsNullOrWhiteSpace(blobPrefix) ? _options.BlobPrefix : blobPrefix;
        var limit = Math.Clamp(maxFiles ?? _options.DefaultMaxFiles, 1, 100);
        var count = 0;

        await foreach (var blob in _containerClient.GetBlobsAsync(BlobTraits.Metadata, prefix: prefix, cancellationToken: cancellationToken))
        {
            if (count >= limit)
            {
                yield break;
            }

            if (!blob.Name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var blobClient = _containerClient.GetBlobClient(blob.Name);
            var content = await blobClient.DownloadContentAsync(cancellationToken).ConfigureAwait(false);
            var metadata = new Dictionary<string, string>(blob.Metadata, StringComparer.OrdinalIgnoreCase);

            _logger.LogInformation("Loaded KnowledgeBase source PDF. BlobPath={BlobPath}, SizeBytes={SizeBytes}", blob.Name, blob.Properties.ContentLength ?? 0);

            count++;
            yield return new KnowledgeBaseBlobDocument(
                BlobPath: blob.Name,
                FileName: Path.GetFileName(blob.Name),
                ContentType: blob.Properties.ContentType,
                SizeBytes: blob.Properties.ContentLength,
                CreatedOn: blob.Properties.CreatedOn,
                LastModified: blob.Properties.LastModified,
                MetadataCategory: GetMetadata(metadata, "category") ?? GetPathSegment(blob.Name, 1),
                MetadataSubcategory: GetMetadata(metadata, "subcategory") ?? GetPathSegment(blob.Name, 2),
                MetadataDocumentType: GetMetadata(metadata, "document_type") ?? GetMetadata(metadata, "documentType"),
                Content: content.Value.Content);
        }
    }

    private static string? GetMetadata(IDictionary<string, string> metadata, string key)
    {
        return metadata.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : null;
    }

    private static string? GetPathSegment(string blobPath, int logicalIndex)
    {
        var segments = blobPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var offset = segments.Length > 0 && segments[0].Equals("internal_docs", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        var index = offset + logicalIndex - 1;
        return index >= 0 && index < segments.Length - 1
            ? segments[index].Replace('-', ' ')
            : null;
    }
}