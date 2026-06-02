using System.Text;

namespace AgentHub.API.Services.KnowledgeBase;

public sealed class SemanticChunker
{
    private readonly KnowledgeBaseOptions _options;

    public SemanticChunker(KnowledgeBaseOptions options)
    {
        _options = options;
    }

    public IReadOnlyList<KnowledgeBaseChunkDraft> CreateChunks(
        KnowledgeBaseBlobDocument document,
        IReadOnlyList<ExtractedPdfPage> pages)
    {
        if (pages.Count == 0)
        {
            return Array.Empty<KnowledgeBaseChunkDraft>();
        }

        var parentId = CreateParentId(document.BlobPath);
        var chunks = new List<KnowledgeBaseChunkDraft>();
        var builder = new StringBuilder();
        var firstPageNumber = pages[0].PageNumber;

        foreach (var page in pages)
        {
            foreach (var paragraph in SplitParagraphs(page.Text))
            {
                if (builder.Length > 0 && builder.Length + paragraph.Length + 2 > _options.ChunkMaxCharacters)
                {
                    AddChunk(chunks, document, parentId, firstPageNumber, builder.ToString());
                    var overlap = GetOverlap(builder.ToString(), _options.ChunkOverlapCharacters);
                    builder.Clear();
                    if (!string.IsNullOrWhiteSpace(overlap))
                    {
                        builder.Append(overlap).Append("\n\n");
                    }

                    firstPageNumber = page.PageNumber;
                }

                builder.Append(paragraph).Append("\n\n");
            }
        }

        if (builder.Length > 0)
        {
            AddChunk(chunks, document, parentId, firstPageNumber, builder.ToString());
        }

        return chunks.Count > _options.MaxChunksPerDocument
            ? chunks.Take(_options.MaxChunksPerDocument).ToArray()
            : chunks;
    }

    public static string CreateParentId(string blobPath)
    {
        var bytes = Encoding.UTF8.GetBytes(blobPath);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static void AddChunk(
        List<KnowledgeBaseChunkDraft> chunks,
        KnowledgeBaseBlobDocument document,
        string parentId,
        int pageNumber,
        string body)
    {
        var index = chunks.Count;
        chunks.Add(new KnowledgeBaseChunkDraft(
            ChunkId: $"{parentId}_{index}",
            ParentId: parentId,
            ChunkIndex: index,
            Content: BuildContent(document, body.Trim()),
            PageNumber: pageNumber));
    }

    private static string BuildContent(KnowledgeBaseBlobDocument document, string body)
    {
        var context = string.Join(" > ", new[]
        {
            document.FileName,
            document.MetadataCategory,
            document.MetadataSubcategory,
            document.MetadataDocumentType
        }.Where(value => !string.IsNullOrWhiteSpace(value)));

        return string.IsNullOrWhiteSpace(context)
            ? body
            : $"[Context: {context}]\n\n{body}";
    }

    private static IEnumerable<string> SplitParagraphs(string text)
    {
        var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        return normalized
            .Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .SelectMany(SplitOversizedParagraph);
    }

    private static IEnumerable<string> SplitOversizedParagraph(string paragraph)
    {
        const int maxParagraphLength = 3200;
        if (paragraph.Length <= maxParagraphLength)
        {
            yield return paragraph;
            yield break;
        }

        for (var start = 0; start < paragraph.Length; start += maxParagraphLength)
        {
            yield return paragraph.Substring(start, Math.Min(maxParagraphLength, paragraph.Length - start));
        }
    }

    private static string GetOverlap(string content, int length)
    {
        if (length <= 0 || content.Length <= length)
        {
            return string.Empty;
        }

        var start = content.Length - length;
        var nextBreak = content.IndexOf('\n', start);
        return nextBreak >= 0 && nextBreak < content.Length - 1
            ? content[(nextBreak + 1)..]
            : content[start..];
    }
}

public sealed record KnowledgeBaseChunkDraft(
    string ChunkId,
    string ParentId,
    int ChunkIndex,
    string Content,
    int PageNumber);