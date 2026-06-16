using System.Text.Json;

namespace ShipMate.AI.Console.Carriers;

/// <summary>
/// Parses an EasyPost Shipment JSON response into <see cref="RateQuote"/> values.
/// Kept separate from <see cref="EasyPostRateEngine"/> so the response-mapping logic
/// (carrier/service extraction, tier mapping, de-duplication) can be unit tested
/// against captured JSON samples without making any HTTP calls.
/// </summary>
public static class EasyPostRateParser
{
    /// <summary>
    /// Parses the "rates" array of an EasyPost shipment response. Invalid entries are
    /// skipped. Results are de-duplicated per (carrier, service), keeping the cheapest,
    /// and returned sorted cheapest-first.
    /// </summary>
    public static IReadOnlyList<RateQuote> Parse(string json)
    {
        var quotes = new List<RateQuote>();

        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("rates", out var rates) ||
            rates.ValueKind != JsonValueKind.Array)
        {
            return quotes;
        }

        foreach (var rate in rates.EnumerateArray())
        {
            var carrier = rate.TryGetProperty("carrier", out var c) ? c.GetString() : null;
            var service = rate.TryGetProperty("service", out var s) ? s.GetString() : null;
            var amountText = rate.TryGetProperty("rate", out var r) ? r.GetString() : null;
            var currency = rate.TryGetProperty("currency", out var cur) ? cur.GetString() : "USD";

            int? deliveryDays = rate.TryGetProperty("delivery_days", out var d) &&
                                d.ValueKind == JsonValueKind.Number
                ? d.GetInt32()
                : null;

            if (carrier is null ||
                !decimal.TryParse(amountText, out var amount))
            {
                continue;
            }

            quotes.Add(new RateQuote
            {
                Carrier = carrier,
                ServiceLevel = MapServiceLevel(deliveryDays),
                ServiceName = service,
                TotalCharge = amount,
                TransitDays = deliveryDays ?? 5,
                Currency = currency ?? "USD"
            });
        }

        // EasyPost can return several rates that collapse onto the same carrier/service;
        // keep only the cheapest quote per (carrier, service) so the list is clean.
        return quotes
            .GroupBy(q => (q.Carrier, q.ServiceName ?? q.ServiceLevel.ToString()))
            .Select(g => g.OrderBy(q => q.TotalCharge).First())
            .OrderBy(q => q.TotalCharge)
            .ToList();
    }

    /// <summary>Maps EasyPost transit days onto the platform's coarse service tiers.</summary>
    public static ServiceLevel MapServiceLevel(int? deliveryDays) => deliveryDays switch
    {
        1 => ServiceLevel.Overnight,
        2 => ServiceLevel.TwoDay,
        _ => ServiceLevel.Ground
    };
}
