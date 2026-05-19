using System.ComponentModel;
using Microsoft.Extensions.AI;

namespace AgentHub.API.services.search;

/// <summary>
/// Exposes <see cref="LeanSearchRetriever"/> as a client-side <see cref="AIFunction"/>
/// that a Foundry/LLM agent can invoke via function calling (tool-call RAG).
/// </summary>
/// <remarks>
/// The model decides when to call the tool based on the function description and parameter
/// schemas — no system-prompt change is required. Results are returned as a compact JSON-like
/// string so the model can quote chunks and cite <c>docId</c>/<c>chunkId</c> in its reply.
/// </remarks>
public static class LeanKnowledgeTool
{
    public const string ToolName = "search_lean_knowledge";

    /// <summary>
    /// Builds the <see cref="AIFunction"/> tool that wraps the retriever.
    /// Register this on the agent's tool list to enable tool-call RAG.
    /// </summary>
    public static AIFunction Create(LeanSearchRetriever retriever, int defaultTopK)
    {
        // Local delegate is reflected by AIFunctionFactory to derive the JSON schema
        // (parameter names + [Description] attributes become the tool contract).
        [Description(
            "Search the Lean/Kaizen knowledge base (A3 reports, Kaizen events, SOPs, " +
            "standard work, gemba notes) for passages relevant to the user's question. " +
            "Call this whenever the user asks about past improvements, root causes, " +
            "countermeasures, KPIs, value streams, or sites. Returns ranked text chunks " +
            "with citations.")]
        async Task<string> SearchLeanKnowledge(
            [Description("The natural-language question or topic to look up.")]
            string query,
            [Description("Optional artifact type filter: a3, kaizenEvent, sop, standardWork, gembaNote, fiveWhys, fishbone, vsm.")]
            string? artifactType = null,
            [Description("Optional value stream name to filter by.")]
            string? valueStream = null,
            [Description("Optional site/plant identifier to filter by.")]
            string? site = null)
        {
            var filter = (artifactType is null && valueStream is null && site is null)
                ? null
                : new LeanSearchFilter(
                    ArtifactType: artifactType,
                    ValueStream: valueStream,
                    Site: site);

            var hits = await retriever.SearchAsync(query, defaultTopK, filter).ConfigureAwait(false);
            if (hits.Count == 0)
            {
                return "NO_RESULTS";
            }

            // Compact line-per-hit format keeps token usage low while preserving citation handles.
            var lines = hits.Select((h, i) =>
                $"[{i + 1}] docId={h.Document.DocId} chunkId={h.Document.ChunkId} " +
                $"type={h.Document.ArtifactType ?? "-"} section={h.Document.SectionType ?? "-"} " +
                $"score={h.Score:F3}\n{h.Document.Content}");

            return string.Join("\n---\n", lines);
        }

        return AIFunctionFactory.Create(SearchLeanKnowledge, name: ToolName);
    }
}
