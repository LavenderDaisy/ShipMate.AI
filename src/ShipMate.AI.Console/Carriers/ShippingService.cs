namespace ShipMate.AI.Console.Carriers;

/// <summary>
/// Creates shipments and serves tracking information. Like the rating layer, this is a
/// deterministic stand-in for a real CarrierEngine Ship/Track transaction: it reuses the
/// rating engines to price the shipment, mints a carrier-style tracking number, persists
/// the result, and synthesizes a plausible tracking timeline. Swap for real carrier APIs
/// in a later milestone without changing the AI/plugin layer.
/// </summary>
public sealed class ShippingService
{
    private readonly RatingService _ratingService;
    private readonly ShipmentStore _store;

    public ShippingService(RatingService ratingService, ShipmentStore store)
    {
        _ratingService = ratingService;
        _store = store;
    }

    public ShipmentResult CreateShipment(ShipmentRequest request)
    {
        // Reuse the rating layer so the booked price is consistent with quoted rates.
        var quote = _ratingService.GetAllRates(new RateRequest
            {
                OriginZip = request.OriginZip,
                DestinationZip = request.DestinationZip,
                WeightLbs = request.WeightLbs,
                ServiceLevel = request.ServiceLevel,
                Residential = request.Residential
            })
            .FirstOrDefault(q =>
                q.Carrier.Equals(request.Carrier, StringComparison.OrdinalIgnoreCase) &&
                q.ServiceLevel == request.ServiceLevel);

        var charge = quote?.TotalCharge ?? 0m;
        var transitDays = quote?.TransitDays ?? request.ServiceLevel switch
        {
            ServiceLevel.Overnight => 1,
            ServiceLevel.TwoDay => 2,
            _ => 5
        };

        var result = new ShipmentResult
        {
            TrackingNumber = GenerateTrackingNumber(request.Carrier),
            Carrier = request.Carrier,
            ServiceLevel = request.ServiceLevel,
            TotalCharge = charge,
            EstimatedDelivery = DateTime.UtcNow.Date.AddDays(transitDays),
            Currency = quote?.Currency ?? "USD"
        };

        _store.Add(result);
        return result;
    }

    public TrackingInfo? Track(string trackingNumber)
    {
        if (!_store.TryGet(trackingNumber, out var shipment) || shipment is null)
        {
            return null;
        }

        var events = new List<TrackingEvent>
        {
            new() { Timestamp = DateTime.UtcNow.AddHours(-6), Status = "Label created",  Location = shipment.Carrier + " origin facility" },
            new() { Timestamp = DateTime.UtcNow.AddHours(-3), Status = "Picked up",      Location = "Origin" },
            new() { Timestamp = DateTime.UtcNow.AddHours(-1), Status = "In transit",     Location = "Regional hub" }
        };

        return new TrackingInfo
        {
            TrackingNumber = shipment.TrackingNumber,
            Carrier = shipment.Carrier,
            CurrentStatus = "In transit",
            EstimatedDelivery = shipment.EstimatedDelivery,
            Events = events
        };
    }

    private static string GenerateTrackingNumber(string carrier)
    {
        var prefix = carrier.ToUpperInvariant() switch
        {
            "UPS" => "1Z",
            "FEDEX" => "FX",
            "USPS" => "US",
            _ => "SM"
        };

        var digits = Random.Shared.NextInt64(100_000_000, 999_999_999);
        return $"{prefix}{digits}";
    }
}
