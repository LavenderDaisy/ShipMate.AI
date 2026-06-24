using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace ShipMate.AI.Console.Carriers;

/// <summary>
/// The real ZPL label and tracking number returned by EasyPost after buying a shipment.
/// </summary>
public sealed record EasyPostLabel
{
    public required string TrackingNumber { get; init; }
    public required string Carrier { get; init; }
    public required string Service { get; init; }

    /// <summary>The public CDN URL of the carrier's ZPL label.</summary>
    public required string LabelUrl { get; init; }

    /// <summary>
    /// The downloaded ZPL payload, or null if the shipment was bought successfully but the
    /// label file could not be fetched (e.g. blocked network). Use <see cref="LabelUrl"/>
    /// to retrieve it manually in that case.
    /// </summary>
    public string? Zpl { get; init; }
}

/// <summary>
/// Buys a real shipment label from EasyPost and retrieves it in ZPL format. Unlike the
/// self-rendered demo label, this returns the carrier's actual, scannable label payload.
/// Use an EasyPost <b>test</b> key (EZTK...) so buying is free and returns a test label.
/// </summary>
public sealed class EasyPostLabelService : IDisposable
{
    private const string ShipmentsUrl = "https://api.easypost.com/v2/shipments";

    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;

    public EasyPostLabelService(string apiKey, HttpClient? httpClient = null)
    {
        _ownsHttpClient = httpClient is null;
        _httpClient = httpClient ?? new HttpClient();

        var token = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{apiKey}:"));
        _httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Basic", token);
    }

    /// <summary>
    /// Creates a shipment (requesting ZPL labels), buys the cheapest rate, and returns the
    /// real carrier ZPL label plus tracking number.
    /// </summary>
    public EasyPostLabel BuyLabel(RateRequest request) =>
        BuyLabelAsync(request).GetAwaiter().GetResult();

    private async Task<EasyPostLabel> BuyLabelAsync(RateRequest request)
    {
        // EasyPost requires fuller addresses to buy; test mode accepts these placeholders.
        var createPayload = new
        {
            shipment = new
            {
                // Buying a label requires complete, internally consistent, verifiable
                // addresses (street/city/state/zip must agree). We use EasyPost's known
                // valid example addresses so the buy succeeds in test mode. The parcel
                // weight still reflects the caller's request. (Rating, by contrast, only
                // needs the origin/destination zips.)
                to_address = new
                {
                    name = "Test Recipient",
                    street1 = "179 N Harbor Dr",
                    city = "Redondo Beach",
                    state = "CA",
                    zip = "90277",
                    country = "US",
                    residential = request.Residential
                },
                from_address = new
                {
                    name = "ShipMate Demo",
                    street1 = "417 Montgomery St",
                    city = "San Francisco",
                    state = "CA",
                    zip = "94104",
                    country = "US"
                },
                parcel = new
                {
                    weight = Math.Round(request.WeightLbs * 16.0, 1)
                },
                options = new
                {
                    label_format = "ZPL"
                }
            }
        };

        var created = await PostJsonAsync(ShipmentsUrl, createPayload);
        using (created)
        {
            var root = created.RootElement;
            var shipmentId = root.GetProperty("id").GetString()
                ?? throw new InvalidOperationException("EasyPost did not return a shipment id.");

            var (rateId, carrier, service) = SelectCheapestRate(root, request);

            var buyPayload = new { rate = new { id = rateId } };
            using var bought = await PostJsonAsync($"{ShipmentsUrl}/{shipmentId}/buy", buyPayload);

            var boughtRoot = bought.RootElement;
            var tracking = boughtRoot.TryGetProperty("tracking_code", out var t)
                ? t.GetString() ?? "UNKNOWN"
                : "UNKNOWN";

            var zplUrl = boughtRoot.GetProperty("postage_label")
                .GetProperty("label_zpl_url").GetString()
                ?? throw new InvalidOperationException("EasyPost did not return a ZPL label URL.");

            // The label URL is a public CDN link. Fetch it with a plain client: reusing
            // _httpClient would send the EasyPost Basic auth header, which the CDN rejects
            // with 400 Bad Request. The shipment is already bought at this point, so if the
            // download fails (e.g. blocked network) we still return the tracking + URL.
            string? zpl = null;
            try
            {
                using var labelClient = new HttpClient();
                zpl = await labelClient.GetStringAsync(zplUrl);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                // Leave zpl null; caller can fall back to LabelUrl.
            }

            return new EasyPostLabel
            {
                TrackingNumber = tracking,
                Carrier = carrier,
                Service = service,
                LabelUrl = zplUrl,
                Zpl = zpl
            };
        }
    }

    private static (string rateId, string carrier, string service) SelectCheapestRate(
        JsonElement shipment, RateRequest request)
    {
        if (!shipment.TryGetProperty("rates", out var rates) ||
            rates.ValueKind != JsonValueKind.Array || rates.GetArrayLength() == 0)
        {
            throw new InvalidOperationException("EasyPost returned no rates for the shipment.");
        }

        string? bestId = null, bestCarrier = null, bestService = null;
        decimal bestAmount = decimal.MaxValue;

        foreach (var rate in rates.EnumerateArray())
        {
            if (!rate.TryGetProperty("rate", out var amountText) ||
                !decimal.TryParse(amountText.GetString(), out var amount))
            {
                continue;
            }

            if (amount < bestAmount)
            {
                bestAmount = amount;
                bestId = rate.GetProperty("id").GetString();
                bestCarrier = rate.TryGetProperty("carrier", out var c) ? c.GetString() : "Unknown";
                bestService = rate.TryGetProperty("service", out var s) ? s.GetString() : "Unknown";
            }
        }

        if (bestId is null)
        {
            throw new InvalidOperationException("Could not select a purchasable rate.");
        }

        return (bestId, bestCarrier ?? "Unknown", bestService ?? "Unknown");
    }

    private async Task<JsonDocument> PostJsonAsync(string url, object payload)
    {
        using var content = new StringContent(
            JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        using var response = await _httpClient.PostAsync(url, content);
        var body = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            var snippet = body.Length <= 300 ? body : body[..300] + "...";
            throw new InvalidOperationException(
                $"EasyPost request to {url} failed ({(int)response.StatusCode}): {snippet}");
        }

        return JsonDocument.Parse(body);
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }
}
