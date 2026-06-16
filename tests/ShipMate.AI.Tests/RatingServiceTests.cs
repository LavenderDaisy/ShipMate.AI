using ShipMate.AI.Console.Carriers;

namespace ShipMate.AI.Tests;

/// <summary>
/// Tests for <see cref="RatingService"/>, which fans a rate request across all registered
/// carrier engines and returns the combined quotes sorted cheapest-first.
/// </summary>
[TestFixture]
public class RatingServiceTests
{
    /// <summary>A tiny in-test engine returning a fixed quote, to control expectations.</summary>
    private sealed class StubRateEngine : ICarrierRateEngine
    {
        private readonly RateQuote _quote;
        public StubRateEngine(string carrier, decimal charge)
        {
            CarrierName = carrier;
            _quote = new RateQuote
            {
                Carrier = carrier,
                ServiceLevel = ServiceLevel.Ground,
                TotalCharge = charge,
                TransitDays = 3,
                Currency = "USD"
            };
        }

        public string CarrierName { get; }
        public IReadOnlyList<RateQuote> GetRates(RateRequest request) => new[] { _quote };
    }

    private static RateRequest SampleRequest() => new()
    {
        OriginZip = "30301",
        DestinationZip = "10001",
        WeightLbs = 5,
        ServiceLevel = ServiceLevel.Ground
    };

    [Test]
    public void GetAllRates_AggregatesAcrossEngines()
    {
        var service = new RatingService(new ICarrierRateEngine[]
        {
            new StubRateEngine("UPS", 20m),
            new StubRateEngine("FedEx", 15m)
        });

        var quotes = service.GetAllRates(SampleRequest());

        Assert.That(quotes.Select(q => q.Carrier), Is.EquivalentTo(new[] { "UPS", "FedEx" }));
    }

    [Test]
    public void GetAllRates_SortsCheapestFirst()
    {
        var service = new RatingService(new ICarrierRateEngine[]
        {
            new StubRateEngine("UPS", 20m),
            new StubRateEngine("FedEx", 15m),
            new StubRateEngine("USPS", 18m)
        });

        var quotes = service.GetAllRates(SampleRequest());

        Assert.That(quotes.Select(q => q.TotalCharge),
            Is.EqualTo(new[] { 15m, 18m, 20m }));
    }

    [Test]
    public void GetAllRates_ReturnsEmpty_WhenNoEngines()
    {
        var service = new RatingService(Array.Empty<ICarrierRateEngine>());

        Assert.That(service.GetAllRates(SampleRequest()), Is.Empty);
    }

    [Test]
    public void MockCarrierRateEngine_ReturnsOneQuotePerServiceLevel()
    {
        var engine = new MockCarrierRateEngine("UPS", baseRate: 8.50m, perPound: 0.85m);

        var quotes = engine.GetRates(SampleRequest());

        // One quote for each ServiceLevel tier.
        Assert.That(quotes.Select(q => q.ServiceLevel),
            Is.EquivalentTo(Enum.GetValues<ServiceLevel>()));
    }

    [Test]
    public void MockCarrierRateEngine_AddsResidentialSurcharge()
    {
        var engine = new MockCarrierRateEngine("UPS", baseRate: 8.50m, perPound: 0.85m);
        var baseReq = SampleRequest();
        var residentialReq = baseReq with { Residential = true };

        var ground = engine.GetRates(baseReq).Single(q => q.ServiceLevel == ServiceLevel.Ground);
        var groundResidential = engine.GetRates(residentialReq).Single(q => q.ServiceLevel == ServiceLevel.Ground);

        Assert.That(groundResidential.TotalCharge, Is.GreaterThan(ground.TotalCharge));
    }
}
