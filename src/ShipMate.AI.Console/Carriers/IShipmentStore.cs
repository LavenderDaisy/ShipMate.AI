namespace ShipMate.AI.Console.Carriers;

/// <summary>
/// Stores created shipments keyed by tracking number. This is the bridge that lets the
/// Track and Label tools find shipments the Ship tool created earlier. Implementations
/// may be in-memory (per session) or durable (MongoDB), selected by configuration; the
/// AI/plugin layer depends only on this contract.
/// </summary>
public interface IShipmentStore
{
    /// <summary>Adds or replaces a shipment.</summary>
    void Add(ShipmentResult shipment);

    /// <summary>Looks up a shipment by tracking number.</summary>
    bool TryGet(string trackingNumber, out ShipmentResult? shipment);

    /// <summary>All stored shipments.</summary>
    IReadOnlyCollection<ShipmentResult> All { get; }
}
