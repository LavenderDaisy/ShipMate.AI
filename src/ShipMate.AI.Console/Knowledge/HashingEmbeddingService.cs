using System.Text.RegularExpressions;

namespace ShipMate.AI.Console.Knowledge;

/// <summary>
/// A deterministic, dependency-free embedding based on hashed bag-of-words term frequency.
/// Each token is hashed into a fixed number of buckets and the resulting vector is L2
/// normalized, so cosine similarity reflects word overlap. It is not semantically deep like
/// a neural embedding, but it is free, offline, deterministic (great for tests), and works
/// well when documents and queries share vocabulary. Used as the fallback when no embedding
/// model is configured.
/// </summary>
public sealed partial class HashingEmbeddingService : IEmbeddingService
{
    public HashingEmbeddingService(int dimensions = 256)
    {
        Dimensions = dimensions;
    }

    public int Dimensions { get; }

    public float[] Embed(string text)
    {
        var vector = new float[Dimensions];

        foreach (var token in Tokenize(text))
        {
            var bucket = (int)(Hash(token) % (uint)Dimensions);
            vector[bucket] += 1f;
        }

        Normalize(vector);
        return vector;
    }

    private static IEnumerable<string> Tokenize(string text)
    {
        foreach (Match m in WordRegex().Matches(text.ToLowerInvariant()))
        {
            var word = m.Value;
            if (word.Length > 2) // skip trivial stop-ish short tokens
            {
                yield return word;
            }
        }
    }

    private static uint Hash(string token)
    {
        // FNV-1a 32-bit hash: fast, stable, well-distributed.
        const uint offset = 2166136261;
        const uint prime = 16777619;
        var hash = offset;
        foreach (var c in token)
        {
            hash ^= c;
            hash *= prime;
        }
        return hash;
    }

    private static void Normalize(float[] v)
    {
        double sumSq = 0;
        foreach (var x in v) sumSq += x * x;
        if (sumSq <= 0) return;

        var norm = (float)Math.Sqrt(sumSq);
        for (var i = 0; i < v.Length; i++) v[i] /= norm;
    }

    [GeneratedRegex(@"[a-z0-9]+")]
    private static partial Regex WordRegex();
}
