using AgentHub.API.Services.KnowledgeBase;

namespace AgentHub.Tests;

public class KnowledgeBaseChunkerTests
{
    [Fact]
    public void CreateParentId_UsesUrlSafeBase64WithoutPadding()
    {
        var parentId = SemanticChunker.CreateParentId("internal_docs/Chillers/Manual.pdf");

        Assert.DoesNotContain("=", parentId);
        Assert.DoesNotContain("+", parentId);
        Assert.DoesNotContain("/", parentId);
    }

    [Fact]
    public void CreateChunks_AddsContextAndStableChunkIds()
    {
        var options = new KnowledgeBaseOptions(
            new Uri("https://storage.blob.core.windows.net/kb"),
            null,
            ChunkMaxCharacters: 120,
            ChunkOverlapCharacters: 10,
            DefaultMaxFiles: 10,
            MaxChunksPerDocument: 10);
        var chunker = new SemanticChunker(options);
        var document = new KnowledgeBaseBlobDocument(
            "internal_docs/Chillers/Water-cooled Chillers/Manual.pdf",
            "Manual.pdf",
            "application/pdf",
            1234,
            DateTimeOffset.Parse("2026-04-06T14:23:18Z"),
            DateTimeOffset.Parse("2026-04-15T14:17:31Z"),
            "Chillers",
            "Water Cooled",
            null,
            BinaryData.FromString("unused"));
        var pages = new[]
        {
            new ExtractedPdfPage(10, "## Water Quality Guidelines\n\nWater systems should be cleaned and flushed prior to installation."),
            new ExtractedPdfPage(11, "## Caution\n\nImproper additives may adversely affect performance and warranty coverage.")
        };

        var chunks = chunker.CreateChunks(document, pages);

        Assert.NotEmpty(chunks);
        Assert.All(chunks, chunk => Assert.StartsWith(chunk.ParentId + "_", chunk.ChunkId));
        Assert.Contains("[Context: Manual.pdf > Chillers > Water Cooled]", chunks[0].Content);
        Assert.Equal(10, chunks[0].PageNumber);
    }

    [Fact]
    public void IndexState_IsCurrentFor_ReturnsTrueOnlyWhenIndexedBlobIsCurrent()
    {
        var blobLastModified = DateTimeOffset.Parse("2026-04-15T14:17:31Z");
        var document = new KnowledgeBaseBlobDocument(
            "internal_docs/Chillers/Water-cooled Chillers/Manual.pdf",
            "Manual.pdf",
            "application/pdf",
            1234,
            DateTimeOffset.Parse("2026-04-06T14:23:18Z"),
            blobLastModified,
            "Chillers",
            "Water Cooled",
            null,
            BinaryData.FromString("unused"));

        var currentState = new KnowledgeBaseDocumentIndexState(
            document.ParentId,
            blobLastModified,
            DateTimeOffset.Parse("2026-05-14T22:09:21Z"),
            ChunkCount: 2);
        var staleState = currentState with { BlobLastModified = blobLastModified.AddSeconds(-1) };

        Assert.True(currentState.IsCurrentFor(document));
        Assert.False(staleState.IsCurrentFor(document));
    }
}