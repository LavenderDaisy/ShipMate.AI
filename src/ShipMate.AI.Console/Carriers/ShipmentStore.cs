using System.Collections.Concurrent;

namespace ShipMate.AI.Console.Carriers;

/// <summary>
/// In-memory store of created shipments keyed by tracking number. This is the bridge
/// that lets the Track tool find shipments the Ship tool created during the same
/// session, which is what makes multi-step tool orchestration observable. In a real
/// system this would be MongoDB (M2 of the roadmap) or the carrier's own tracking API.
/// </summary>
public sealed class ShipmentStore
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
