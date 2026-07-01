namespace ShipMate.AI.Console.Knowledge;

/// <summary>
/// An in-memory vector store with cosine-similarity search — the retrieval half of RAG.
/// Documents are embedded once at construction; queries are embedded on demand and matched
/// by cosine similarity, returning the top-K most relevant documents. In a production system
/// this would be a dedicated vector database (Azure AI Search, Qdrant, pgvector); the search
/// contract stays the same.
/// </summary>
public sealed class VectorSearchService
{
    private readonly IEmbeddingService _embeddings;
    private readonly List<EmbeddedDocument> _store;

    public VectorSearchService(IEmbeddingService embeddings, IEnumerable<KnowledgeDocument> documents)
    {
        _embeddings = embeddings;
        _store = documents
            .Select(d => new EmbeddedDocument
            {
                Document = d,
                // Embed title + content so both contribute to matching.
                Embedding = _embeddings.Embed($"{d.Title}. {d.Content}")
            })
            .ToList();
    }

    public int Count => _store.Count;

    /// <summary>Returns the top-K documents most similar to the query, highest score first.</summary>
    public IReadOnlyList<SearchResult> Search(string query, int topK = 3, double minScore = 0.0)
    {
        if (string.IsNullOrWhiteSpace(query) || _store.Count == 0)
        {
            return Array.Empty<SearchResult>();
        }

        var queryVector = _embeddings.Embed(query);

        return _store
            .Select(e => new SearchResult
            {
                Document = e.Document,
                Score = CosineSimilarity(queryVector, e.Embedding)
            })
            .Where(r => r.Score >= minScore)
            .OrderByDescending(r => r.Score)
            .Take(topK)
            .ToList();
    }

    /// <summary>Cosine similarity of two equal-length vectors, in [-1, 1].</summary>
    public static double CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length != b.Length)
        {
            throw new ArgumentException("Vectors must have the same length.");
        }

        double dot = 0, normA = 0, normB = 0;
        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }

        if (normA <= 0 || normB <= 0)
        {
            return 0;
        }

        return dot / (Math.Sqrt(normA) * Math.Sqrt(normB));
    }
}
