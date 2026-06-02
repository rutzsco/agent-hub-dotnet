namespace AgentHub.API.Services.KnowledgeBase;

public sealed class KnowledgeBaseIngestionService
{
    private readonly KnowledgeBaseOptions _options;
    private readonly KnowledgeBaseBlobSource _blobSource;
    private readonly DocumentIntelligencePdfTextExtractor _pdfTextExtractor;
    private readonly SemanticChunker _chunker;
    private readonly KnowledgeBaseEmbeddingService _embeddingService;
    private readonly IKnowledgeBaseRepository _repository;
    private readonly ILogger<KnowledgeBaseIngestionService> _logger;

    public KnowledgeBaseIngestionService(
        KnowledgeBaseOptions options,
        KnowledgeBaseBlobSource blobSource,
        DocumentIntelligencePdfTextExtractor pdfTextExtractor,
        SemanticChunker chunker,
        KnowledgeBaseEmbeddingService embeddingService,
        IKnowledgeBaseRepository repository,
        ILogger<KnowledgeBaseIngestionService> logger)
    {
        _options = options;
        _blobSource = blobSource;
        _pdfTextExtractor = pdfTextExtractor;
        _chunker = chunker;
        _embeddingService = embeddingService;
        _repository = repository;
        _logger = logger;
    }

    public async Task<KnowledgeBaseIngestionResult> IngestAsync(
        string? blobPrefix,
        int? maxFiles,
        bool forceReindex,
        CancellationToken cancellationToken)
    {
        var effectivePrefix = string.IsNullOrWhiteSpace(blobPrefix) ? _options.BlobPrefix : blobPrefix;
        var files = new List<KnowledgeBaseFileIngestionResult>();
        var filesFound = 0;

        await foreach (var document in _blobSource.GetPdfDocumentsAsync(effectivePrefix, maxFiles, cancellationToken))
        {
            filesFound++;
            try
            {
                if (!forceReindex)
                {
                    var state = await _repository.GetDocumentIndexStateAsync(document.ParentId, cancellationToken).ConfigureAwait(false);
                    if (state?.IsCurrentFor(document) == true)
                    {
                        _logger.LogInformation(
                            "Skipping current KnowledgeBase PDF. BlobPath={BlobPath}, LastIndexed={LastIndexed}",
                            document.BlobPath,
                            state.LastIndexed);
                        files.Add(new KnowledgeBaseFileIngestionResult(
                            document.BlobPath,
                            document.ParentId,
                            state.ChunkCount,
                            Status: "skipped_current",
                            UsedCachedDocumentIntelligence: false,
                            Error: null));
                        continue;
                    }
                }

                _logger.LogInformation("Indexing KnowledgeBase PDF. BlobPath={BlobPath}", document.BlobPath);
                var extraction = await _pdfTextExtractor.ExtractPagesAsync(document, cancellationToken);
                var drafts = _chunker.CreateChunks(document, extraction.Pages);
                var chunks = new List<KnowledgeBaseChunk>(drafts.Count);
                var indexedAt = DateTimeOffset.UtcNow;

                foreach (var draft in drafts)
                {
                    var vector = await _embeddingService.GenerateEmbeddingAsync(draft.Content, cancellationToken);
                    chunks.Add(new KnowledgeBaseChunk(
                        draft.ChunkId,
                        draft.ParentId,
                        draft.ChunkIndex,
                        draft.Content,
                        document.FileName,
                        document.BlobPath,
                        document.FileName,
                        document.ContentType,
                        document.SizeBytes,
                        document.CreatedOn,
                        document.LastModified,
                        document.MetadataCategory,
                        document.MetadataSubcategory,
                        document.MetadataDocumentType,
                        draft.PageNumber,
                        indexedAt,
                        vector));
                }

                await _repository.ReplaceDocumentChunksAsync(document.ParentId, chunks, cancellationToken);
                files.Add(new KnowledgeBaseFileIngestionResult(
                    document.BlobPath,
                    document.ParentId,
                    chunks.Count,
                    Status: "indexed",
                    UsedCachedDocumentIntelligence: extraction.UsedCache,
                    Error: null));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "KnowledgeBase PDF indexing failed. BlobPath={BlobPath}", document.BlobPath);
                files.Add(new KnowledgeBaseFileIngestionResult(
                    document.BlobPath,
                    document.ParentId,
                    0,
                    Status: "failed",
                    UsedCachedDocumentIntelligence: false,
                    Error: ex.Message));
            }
        }

        return new KnowledgeBaseIngestionResult(
            effectivePrefix,
            forceReindex,
            filesFound,
            files.Count(file => file.Status == "indexed"),
            files.Count(file => file.Status == "skipped_current"),
            files.Where(file => file.Status == "indexed").Sum(file => file.ChunkCount),
            files);
    }
}

public sealed record KnowledgeBaseIngestionResult(
    string? BlobPrefix,
    bool ForceReindex,
    int FilesFound,
    int FilesIndexed,
    int FilesSkipped,
    int ChunksIndexed,
    IReadOnlyList<KnowledgeBaseFileIngestionResult> Files);

public sealed record KnowledgeBaseFileIngestionResult(
    string BlobPath,
    string ParentId,
    int ChunkCount,
    string Status,
    bool UsedCachedDocumentIntelligence,
    string? Error);