using ShipMate.AI.Console.Knowledge;

namespace ShipMate.AI.Tests;

/// <summary>
/// Tests for the RAG retrieval machinery: hashing embedding determinism, cosine similarity,
/// and end-to-end relevance of vector search over the carrier knowledge base. All offline.
/// </summary>
[TestFixture]
public class RagKnowledgeTests
{
    [Test]
    public void HashingEmbedding_IsDeterministic()
    {
        var svc = new HashingEmbeddingService(128);

        var a = svc.Embed("lithium batteries by air");
        var b = svc.Embed("lithium batteries by air");

        Assert.That(a, Is.EqualTo(b));
        Assert.That(a, Has.Length.EqualTo(128));
    }

    [Test]
    public void HashingEmbedding_IsL2Normalized()
    {
        var svc = new HashingEmbeddingService();

        var v = svc.Embed("prohibited items include explosives and firearms");

        var norm = Math.Sqrt(v.Sum(x => (double)x * x));
        Assert.That(norm, Is.EqualTo(1.0).Within(1e-4));
    }

    [Test]
    public void CosineSimilarity_IdenticalVectors_IsOne()
    {
        var v = new[] { 0.5f, 0.5f, 0.5f, 0.5f };
        Assert.That(VectorSearchService.CosineSimilarity(v, v), Is.EqualTo(1.0).Within(1e-6));
    }

    [Test]
    public void CosineSimilarity_OrthogonalVectors_IsZero()
    {
        var a = new[] { 1f, 0f };
        var b = new[] { 0f, 1f };
        Assert.That(VectorSearchService.CosineSimilarity(a, b), Is.EqualTo(0.0).Within(1e-6));
    }

    [Test]
    public void CosineSimilarity_DifferentLengths_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            VectorSearchService.CosineSimilarity(new[] { 1f }, new[] { 1f, 2f }));
    }

    [Test]
    public void Search_LithiumQuery_RetrievesHazmatDocFirst()
    {
        var search = new VectorSearchService(new HashingEmbeddingService(), CarrierKnowledgeBase.Documents);

        var results = search.Search("can I ship lithium batteries by air", topK: 3);

        Assert.That(results, Is.Not.Empty);
        Assert.That(results[0].Document.Id, Is.EqualTo("hazmat-lithium"));
    }

    [Test]
    public void Search_ChinaQuery_RetrievesInternationalChinaDoc()
    {
        var search = new VectorSearchService(new HashingEmbeddingService(), CarrierKnowledgeBase.Documents);

        var results = search.Search("shipping a package to China customs", topK: 3);

        Assert.That(results.Select(r => r.Document.Id), Does.Contain("international-china"));
    }

    [Test]
    public void Search_RespectsTopK()
    {
        var search = new VectorSearchService(new HashingEmbeddingService(), CarrierKnowledgeBase.Documents);

        var results = search.Search("shipping rules", topK: 2);

        Assert.That(results, Has.Count.LessThanOrEqualTo(2));
    }

    [Test]
    public void Search_EmptyQuery_ReturnsEmpty()
    {
        var search = new VectorSearchService(new HashingEmbeddingService(), CarrierKnowledgeBase.Documents);

        Assert.That(search.Search("  ", topK: 3), Is.Empty);
    }
}
