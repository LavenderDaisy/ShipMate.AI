namespace ShipMate.AI.Console.Carriers;

/// <summary>
/// A deterministic, in-memory carrier rate engine used for the M1 demo. It produces
/// plausible quotes from a simple base-rate + per-pound + service-multiplier model so
/// the AI layer can be exercised end to end without live carrier credentials.
/// Swap this for a real CarrierEngine-backed implementation in later milestones.
/// </summary>
public sealed class MockCarrierRateEngine : ICarrierRateEngine
{
    private readonly decimal _baseRate;
    private readonly decimal _perPound;

    public MockCarrierRateEngine(string carrierName, decimal baseRate, decimal perPound)
    {
        CarrierName = carrierName;
        _baseRate = baseRate;
        _perPound = perPound;
    }

    public string CarrierName { get; }

    public IReadOnlyList<RateQuote> GetRates(RateRequest request)
    {
        var quotes = new List<RateQuote>();

        foreach (var service in Enum.GetValues<ServiceLevel>())
        {
            var (multiplier, transitDays) = service switch
            {
                ServiceLevel.Overnight => (3.2m, 1),
                ServiceLevel.TwoDay => (1.9m, 2),
                _ => (1.0m, 5)
            };

            var weightCharge = _perPound * (decimal)request.WeightLbs;
            var residentialSurcharge = request.Residential ? 4.50m : 0m;
            var total = Math.Round((_baseRate + weightCharge) * multiplier + residentialSurcharge, 2);

            quotes.Add(new RateQuote
            {
                Carrier = CarrierName,
                ServiceLevel = service,
                TotalCharge = total,
                TransitDays = transitDays,
                Currency = "USD"
            });
        }

        return quotes;
    }
}
