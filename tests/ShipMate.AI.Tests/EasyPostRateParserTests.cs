using ShipMate.AI.Console.Carriers;

namespace ShipMate.AI.Tests;

/// <summary>
/// Tests for <see cref="EasyPostRateParser"/>. These exercise the EasyPost response
/// mapping using captured JSON samples, so no API key or network access is required.
/// </summary>
[TestFixture]
public class EasyPostRateParserTests
{
    // Mirrors a real EasyPost test response: USPS with several services, including
    // duplicate entries for the same service that the parser should collapse.
    private const string SampleJson = """
        {
          "rates": [
            { "carrier": "USPS", "service": "GroundAdvantage", "rate": "11.16", "currency": "USD", "delivery_days": 3 },
            { "carrier": "USPS", "service": "GroundAdvantage", "rate": "11.16", "currency": "USD", "delivery_days": 3 },
            { "carrier": "USPS", "service": "Priority",        "rate": "19.08", "currency": "USD", "delivery_days": 2 },
            { "carrier": "USPS", "service": "Express",         "rate": "72.80", "currency": "USD", "delivery_days": 1 },
            { "carrier": "USPS", "service": "Express",         "rate": "80.00", "currency": "USD", "delivery_days": 1 }
          ]
        }
        """;

    [Test]
    public void Parse_DeduplicatesByCarrierAndService()
    {
        var quotes = EasyPostRateParser.Parse(SampleJson);

        // 5 raw rates -> 3 unique (carrier, service) groups.
        Assert.That(quotes, Has.Count.EqualTo(3));
    }

    [Test]
    public void Parse_KeepsCheapestPerService()
    {
        var quotes = EasyPostRateParser.Parse(SampleJson);

        var express = quotes.Single(q => q.ServiceName == "Express");
        // Two Express entries (72.80 and 80.00); cheapest wins.
        Assert.That(express.TotalCharge, Is.EqualTo(72.80m));
    }

    [Test]
    public void Parse_SortsCheapestFirst()
    {
        var quotes = EasyPostRateParser.Parse(SampleJson);

        Assert.That(quotes.Select(q => q.TotalCharge),
            Is.EqualTo(new[] { 11.16m, 19.08m, 72.80m }));
    }

    [Test]
    public void Parse_PreservesRealServiceName()
    {
        var quotes = EasyPostRateParser.Parse(SampleJson);

        Assert.That(quotes.Select(q => q.ServiceName),
            Does.Contain("GroundAdvantage").And.Contain("Priority").And.Contain("Express"));
    }

    [Test]
    public void Parse_MapsCarrierAndCurrency()
    {
        var quotes = EasyPostRateParser.Parse(SampleJson);

        Assert.Multiple(() =>
        {
            Assert.That(quotes.All(q => q.Carrier == "USPS"), Is.True);
            Assert.That(quotes.All(q => q.Currency == "USD"), Is.True);
        });
    }

    [Test]
    public void Parse_SkipsEntriesWithInvalidRate()
    {
        const string json = """
            { "rates": [
                { "carrier": "USPS", "service": "Priority", "rate": "not-a-number", "currency": "USD", "delivery_days": 2 },
                { "carrier": "USPS", "service": "Express",  "rate": "20.00",        "currency": "USD", "delivery_days": 1 }
            ] }
            """;

        var quotes = EasyPostRateParser.Parse(json);

        Assert.That(quotes, Has.Count.EqualTo(1));
        Assert.That(quotes[0].ServiceName, Is.EqualTo("Express"));
    }

    [Test]
    public void Parse_ReturnsEmptyWhenNoRatesArray()
    {
        var quotes = EasyPostRateParser.Parse("""{ "error": "something" }""");

        Assert.That(quotes, Is.Empty);
    }

    [TestCase(1, ServiceLevel.Overnight)]
    [TestCase(2, ServiceLevel.TwoDay)]
    [TestCase(3, ServiceLevel.Ground)]
    [TestCase(null, ServiceLevel.Ground)]
    public void MapServiceLevel_MapsTransitDaysToTier(int? days, ServiceLevel expected)
    {
        Assert.That(EasyPostRateParser.MapServiceLevel(days), Is.EqualTo(expected));
    }
}
