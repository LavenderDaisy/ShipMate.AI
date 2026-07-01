namespace ShipMate.AI.Console.Knowledge;

/// <summary>
/// Turns text into a fixed-length embedding vector. Implementations may call a real
/// embedding model (OpenAI-compatible API) or compute a deterministic local vector.
/// Kept as a Strategy so the RAG pipeline is independent of the embedding source and can
/// run fully offline in tests.
/// </summary>
public interface IEmbeddingService
{
    /// <summary>Number of dimensions the returned vectors have.</summary>
    int Dimensions { get; }

    /// <summary>Computes an embedding vector for the given text.</summary>
    float[] Embed(string text);
}
