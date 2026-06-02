using Azure;
using Azure.AI.DocumentIntelligence;

namespace AgentHub.API.Services.KnowledgeBase;

public sealed class DocumentIntelligencePdfTextExtractor
{
    private const string ModelId = "prebuilt-layout";
    private readonly DocumentIntelligenceClient _client;
    private readonly KnowledgeBaseDocumentIntelligenceCache _cache;
    private readonly ILogger<DocumentIntelligencePdfTextExtractor> _logger;

    public DocumentIntelligencePdfTextExtractor(
        DocumentIntelligenceClient client,
        KnowledgeBaseDocumentIntelligenceCache cache,
        ILogger<DocumentIntelligencePdfTextExtractor> logger)
    {
        _client = client;
        _cache = cache;
        _logger = logger;
    }

    public async Task<DocumentIntelligenceExtractionResult> ExtractPagesAsync(
        KnowledgeBaseBlobDocument document,
        CancellationToken cancellationToken)
    {
        var cachedPages = await _cache.TryGetPagesAsync(document, cancellationToken).ConfigureAwait(false);
        if (cachedPages is not null)
        {
            return new DocumentIntelligenceExtractionResult(cachedPages, UsedCache: true);
        }

        _logger.LogInformation("Extracting PDF text with Document Intelligence. BlobPath={BlobPath}", document.BlobPath);

        var operation = await _client
            .AnalyzeDocumentAsync(WaitUntil.Completed, ModelId, document.Content, cancellationToken)
            .ConfigureAwait(false);

        var pages = operation.Value.Pages
            .Select(page => new ExtractedPdfPage(
                page.PageNumber,
                string.Join('\n', page.Lines.Select(line => line.Content))))
            .Where(page => !string.IsNullOrWhiteSpace(page.Text))
            .ToArray();

        _logger.LogInformation(
            "Extracted {PageCount} text-bearing page(s). BlobPath={BlobPath}",
            pages.Length,
            document.BlobPath);

        await _cache.StorePagesAsync(document, pages, cancellationToken).ConfigureAwait(false);
        return new DocumentIntelligenceExtractionResult(pages, UsedCache: false);
    }
}

public sealed record ExtractedPdfPage(int PageNumber, string Text);

public sealed record DocumentIntelligenceExtractionResult(
    IReadOnlyList<ExtractedPdfPage> Pages,
    bool UsedCache);