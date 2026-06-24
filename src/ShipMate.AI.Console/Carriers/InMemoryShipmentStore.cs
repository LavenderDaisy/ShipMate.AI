using System.Collections.Concurrent;

namespace ShipMate.AI.Console.Carriers;

/// <summary>
/// In-memory <see cref="IShipmentStore"/> backed by a thread-safe dictionary. Scoped to a
/// single run; used by default when no durable store (MongoDB) is configured. Keeps the
/// app fully runnable with zero external dependencies.
/// </summary>
public sealed class InMemoryShipmentStore : IShipmentStore
{
    private readonly ConcurrentDictionary<string, ShipmentResult> _shipments = new();

    public void Add(ShipmentResult shipment) => _shipments[shipment.TrackingNumber] = shipment;

    public bool TryGet(string trackingNumber, out ShipmentResult? shipment)
    {
        var found = _shipments.TryGetValue(trackingNumber, out var value);
        shipment = value;
        return found;
    }

    public IReadOnlyCollection<ShipmentResult> All => _shipments.Values.ToList();
}
