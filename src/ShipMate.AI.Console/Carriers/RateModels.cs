namespace ShipMate.AI.Console.Carriers;

/// <summary>
/// Service level requested for a shipment. Mirrors the service tiers exposed by
/// the StarShip CarrierEngine Rate transaction.
/// </summary>
public enum ServiceLevel
{
    Ground,
    TwoDay,
    Overnight
}

/// <summary>
/// Input for a rate request. This is intentionally a small, flat shape so the LLM
/// can populate it directly from a natural-language prompt via function calling.
/// </summary>
public sealed record RateRequest
{
    public required string OriginZip { get; init; }
    public required string DestinationZip { get; init; }
    public required double WeightLbs { get; init; }
    public ServiceLevel ServiceLevel { get; init; } = ServiceLevel.Ground;
    public bool Residential { get; init; }
}

/// <summary>
/// A single rate quote returned by one carrier for one service.
/// </summary>
public sealed record RateQuote
{
    public required string Carrier { get; init; }
    public required ServiceLevel ServiceLevel { get; init; }
    public required decimal TotalCharge { get; init; }
    public required int TransitDays { get; init; }
    public required string Currency { get; init; } = "USD";

    /// <summary>
    /// The carrier's specific service name when available (e.g. "Priority",
    /// "GroundAdvantage", "Express"). Null for engines that only expose coarse tiers.
    /// </summary>
    public string? ServiceName { get; init; }
}
