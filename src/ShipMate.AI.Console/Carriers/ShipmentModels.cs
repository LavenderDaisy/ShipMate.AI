namespace ShipMate.AI.Console.Carriers;

/// <summary>
/// Input for creating a shipment. Mirrors the shape the StarShip CarrierEngine Ship
/// transaction needs: who/where it ships from and to, the package, and the chosen service.
/// </summary>
public sealed record ShipmentRequest
{
    public required string Carrier { get; init; }
    public required ServiceLevel ServiceLevel { get; init; }
    public required string OriginZip { get; init; }
    public required string DestinationZip { get; init; }
    public required double WeightLbs { get; init; }
    public bool Residential { get; init; }
}

/// <summary>
/// Result of a successful shipment creation: the carrier tracking number plus the
/// rated charge. In a real integration the carrier API also returns a label payload.
/// </summary>
public sealed record ShipmentResult
{
    public required string TrackingNumber { get; init; }
    public required string Carrier { get; init; }
    public required ServiceLevel ServiceLevel { get; init; }
    public required decimal TotalCharge { get; init; }
    public required DateTime EstimatedDelivery { get; init; }
    public required string Currency { get; init; } = "USD";
}

/// <summary>A single scan/event in a package's tracking history.</summary>
public sealed record TrackingEvent
{
    public required DateTime Timestamp { get; init; }
    public required string Status { get; init; }
    public required string Location { get; init; }
}

/// <summary>Current status plus full scan history for a tracked shipment.</summary>
public sealed record TrackingInfo
{
    public required string TrackingNumber { get; init; }
    public required string Carrier { get; init; }
    public required string CurrentStatus { get; init; }
    public required DateTime EstimatedDelivery { get; init; }
    public required IReadOnlyList<TrackingEvent> Events { get; init; }
}
