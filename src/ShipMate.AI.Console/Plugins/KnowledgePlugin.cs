using System.ComponentModel;
using System.Text;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using ShipMate.AI.Console.Knowledge;

namespace ShipMate.AI.Console.Plugins;

/// <summary>
/// Retrieval-Augmented Generation (RAG) tool. Exposes the carrier-rules knowledge base to
/// the LLM: given a natural-language question, it retrieves the most relevant rule snippets
/// via vector search and returns them so the model can answer grounded in real policy text
/// rather than hallucinating. This is "agentic RAG" — the model decides when to retrieve.
///
/// When query rewriting is enabled, the tool goes one step further: before retrieving, it
/// asks the LLM to expand the user's phrasing into search keywords (e.g. "power bank" ->
/// "lithium battery power bank"). This improves recall, especially for the lexical local
/// embedding, and makes the agentic nature explicit — the model shapes not just *when* to
/// retrieve but *what* to search for.
/// </summary>
public sealed class KnowledgePlugin
{
    private readonly VectorSearchService _search;
    private readonly bool _rewriteQueries;

    public KnowledgePlugin(VectorSearchService search, bool rewriteQueries = false)
    {
        _search = search;
        _rewriteQueries = rewriteQueries;
    }

    [KernelFunction("search_carrier_rules")]
    [Description("Searches the carrier rules and policy knowledge base (hazardous materials, " +
                 "prohibited/restricted items, international shipping eligibility, dimensional " +
                 "weight, surcharges). Use this whenever the user asks whether something can be " +
                 "shipped, is allowed, or about carrier policies/restrictions. Returns relevant " +
                 "policy snippets to ground your answer.")]
    public async Task<string> SearchCarrierRules(
        Kernel kernel,
        [Description("The user's question or topic, e.g. 'can I ship lithium batteries by air'.")]
        string query)
    {
        using var span = ShipMateTelemetry.StartSpan("tool.search_carrier_rules");
        span?.SetTag("rag.query", query);

        // Optionally let the LLM rewrite the question into keyword-rich search text.
        var searchQuery = query;
        if (_rewriteQueries)
        {
            searchQuery = await RewriteQueryAsync(kernel, query);
            span?.SetTag("rag.rewritten_query", searchQuery);
        }

        var results = _search.Search(searchQuery, topK: 3);
        span?.SetTag("rag.result_count", results.Count);

        if (results.Count == 0)
        {
            return "No relevant carrier rules were found for that question.";
        }

        var sb = new StringBuilder();
        sb.AppendLine("Relevant carrier rules:");
        foreach (var r in results)
        {
            sb.AppendLine($"- [{r.Document.Title}] {r.Document.Content}");
        }

        span?.SetTag("rag.top_score", results[0].Score);
        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// Uses the LLM to expand the user's question into search keywords, then appends them to
    /// the original query (so the original signal is never lost). Falls back to the original
    /// query on any failure, keeping retrieval robust.
    ///
    /// Important: this calls the chat model directly via IChatCompletionService with tool
    /// calling disabled, rather than kernel.InvokePromptAsync. Re-entering the kernel while
    /// it is mid auto-tool-invocation (this method runs inside a tool call) is fragile; a
    /// plain completion with a fresh history avoids that.
    /// </summary>
    private static async Task<string> RewriteQueryAsync(Kernel kernel, string query)
    {
        try
        {
            var chat = kernel.GetRequiredService<IChatCompletionService>();

            var history = new ChatHistory();
            history.AddUserMessage($$"""
                Expand this shipping question into concise search keywords for retrieving
                carrier policy documents. Expand abbreviations and add relevant synonyms, e.g.
                "power bank" -> "lithium battery power bank", "send abroad" -> "international
                customs". Reply with ONLY the keywords on one line, no explanation.

                Question: {{query}}
                """);

            // Explicitly disable tool calling so this is a plain, non-recursive completion.
            var settings = new OpenAIPromptExecutionSettings
            {
                ToolCallBehavior = null,
                Temperature = 0,
                MaxTokens = 64
            };

            var result = await chat.GetChatMessageContentAsync(history, settings);
            var keywords = result.Content?.Trim() ?? string.Empty;

            // Combine original + expanded keywords: adds recall without losing the original.
            return string.IsNullOrWhiteSpace(keywords) ? query : $"{query} {keywords}";
        }
        catch
        {
            return query;
        }
    }
}
