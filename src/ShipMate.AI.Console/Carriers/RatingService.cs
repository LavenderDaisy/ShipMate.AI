namespace ShipMate.AI.Console.Carriers;

/// <summary>
/// Aggregates rate quotes across all registered carrier engines. This mirrors the
/// dispatch role of the StarShip CarrierEngine, which fans a Rate transaction out to
/// every registered carrier and collects the results.
/// </summary>
public sealed class RatingService
{
    private readonly IReadOnlyList<ICarrierRateEngine> _engines;

    public RatingService(IEnumerable<ICarrierRateEngine> engines)
    {
        _engines = engines.ToList();
    }

    /// <summary>Returns every quote from every carrier, sorted cheapest first.</summary>
    public IReadOnlyList<RateQuote> GetAllRates(RateRequest request)
    {
        return _engines
            .SelectMany(engine => engine.GetRates(request))
            .OrderBy(q => q.TotalCharge)
            .ToList();
    }
}
