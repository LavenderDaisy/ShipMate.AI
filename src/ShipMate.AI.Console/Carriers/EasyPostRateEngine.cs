using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace ShipMate.AI.Console.Carriers;

/// <summary>
/// A real carrier rate engine backed by the EasyPost API (https://www.easypost.com).
/// EasyPost is a shipping aggregator: a single account returns live rates from many
/// carriers (USPS, UPS, FedEx, ...). This implements the same <see cref="ICarrierRateEngine"/>
/// contract as the mock engine, so the AI/plugin layer needs no changes to use real data.
///
/// Use an EasyPost <b>test</b> API key (prefix "EZTK...") to get deterministic test rates
/// at no cost. Get one at https://www.easypost.com/account/api-keys.
/// </summary>
public sealed class EasyPostRateEngine : ICarrierRateEngine, IDisposable
{
    private const string ShipmentsUrl = "https://api.easypost.com/v2/shipments";

    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;

    public EasyPostRateEngine(string apiKey, HttpClient? httpClient = null)
    {
        _ownsHttpClient = httpClient is null;
        _httpClient = httpClient ?? new HttpClient();

        // EasyPost uses HTTP Basic auth with the API key as the username and no password.
        var token = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{apiKey}:"));
        _httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Basic", token);
    }

    public string CarrierName => "EasyPost";

    public IReadOnlyList<RateQuote> GetRates(RateRequest request)
    {
        // ICarrierRateEngine is synchronous; bridge to the async HTTP call here. This is
        // acceptable for a console app. A service host would expose an async rate method.
        return GetRatesAsync(request).GetAwaiter().GetResult();
    }

    private async Task<IReadOnlyList<RateQuote>> GetRatesAsync(RateRequest request)
    {
        var payload = new
        {
            shipment = new
            {
                to_address = new
                {
                    zip = request.DestinationZip,
                    country = "US",
                    residential = request.Residential
                },
                from_address = new
                {
                    zip = request.OriginZip,
                    country = "US"
                },
                parcel = new
                {
                    // EasyPost expects weight in ounces.
                    weight = Math.Round(request.WeightLbs * 16.0, 1)
                }
            }
        };

        using var content = new StringContent(
            JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        using var response = await _httpClient.PostAsync(ShipmentsUrl, content);
        var body = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"EasyPost rating failed ({(int)response.StatusCode}): {Truncate(body, 300)}");
        }

        return EasyPostRateParser.Parse(body);
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max] + "...";

    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }
}
