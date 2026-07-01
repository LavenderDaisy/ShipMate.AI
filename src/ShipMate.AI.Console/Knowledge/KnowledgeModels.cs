namespace ShipMate.AI.Console.Knowledge;

/// <summary>
/// A single knowledge-base document (a carrier rule, restriction, or policy snippet).
/// </summary>
public sealed record KnowledgeDocument
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string Content { get; init; }
}

/// <summary>A document plus its embedding vector, held in the vector store.</summary>
public sealed record EmbeddedDocument
{
    public required KnowledgeDocument Document { get; init; }
    public required float[] Embedding { get; init; }
}

/// <summary>A retrieval hit: the matched document and its similarity score (0..1).</summary>
public sealed record SearchResult
{
    public required KnowledgeDocument Document { get; init; }
    public required double Score { get; init; }
}
