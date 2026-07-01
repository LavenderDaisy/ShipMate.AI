using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using Microsoft.SemanticKernel;
using ShipMate.AI.Console.Carriers;

namespace ShipMate.AI.Console.Plugins;

/// <summary>
/// Semantic Kernel plugin that exposes carrier rating to the LLM as a callable tool.
/// The [KernelFunction] + [Description] attributes are what the model reads to decide
/// when and how to call this function during automatic function calling.
/// </summary>
public sealed class RatePlugin
{
    private readonly RatingService _ratingService;

    public RatePlugin(RatingService ratingService)
    {
        _ratingService = ratingService;
    }

    [KernelFunction("get_shipping_rates")]
    [Description("Gets shipping rate quotes from all available carriers for a package. " +
                 "Use this whenever the user asks about shipping cost, price comparison, " +
                 "or which carrier/service to use.")]
    public string GetShippingRates(
        [Description("Origin postal/ZIP code the package ships from, e.g. '30301'.")]
        string originZip,
        [Description("Destination postal/ZIP code the package ships to, e.g. '10001'.")]
        string destinationZip,
        [Description("Package weight in pounds.")]
        double weightLbs,
        [Description("Requested service level. One of: Ground, TwoDay, Overnight. " +
                     "If the user does not specify, omit and all services are returned.")]
        string? serviceLevel = null,
        [Description("True if delivering to a residential address; adds a surcharge.")]
        bool residential = false)
    {
        var request = new RateRequest
        {
            OriginZip = originZip,
            DestinationZip = destinationZip,
            WeightLbs = weightLbs,
            ServiceLevel = ParseServiceLevel(serviceLevel),
            Residential = residential
        };

        using var span = ShipMateTelemetry.StartSpan("tool.get_shipping_rates");
        span?.SetTag("rate.origin_zip", originZip);
        span?.SetTag("rate.destination_zip", destinationZip);
        span?.SetTag("rate.weight_lbs", weightLbs);
        span?.SetTag("rate.service_level", serviceLevel ?? "all");

        var quotes = _ratingService.GetAllRates(request);
        span?.SetTag("rate.quote_count", quotes.Count);

        // When the user pinned a service level, only return matching quotes.
        if (serviceLevel is not null && Enum.TryParse<ServiceLevel>(serviceLevel, true, out var pinned))
        {
            quotes = quotes.Where(q => q.ServiceLevel == pinned).ToList();
        }

        if (quotes.Count == 0)
        {
            return "No rates were returned for the requested shipment.";
        }

        // Return a compact, model-friendly summary. The LLM will turn this into prose.
        var sb = new StringBuilder();
        sb.AppendLine($"Rates from {request.OriginZip} to {request.DestinationZip}, {request.WeightLbs} lb"
                      + (request.Residential ? " (residential):" : ":"));
        foreach (var q in quotes)
        {
            var service = string.IsNullOrEmpty(q.ServiceName)
                ? q.ServiceLevel.ToString()
                : $"{q.ServiceName} ({q.ServiceLevel})";
            sb.AppendLine($"- {q.Carrier} {service}: {q.TotalCharge:C} {q.Currency}, {q.TransitDays} day(s)");
        }

        return sb.ToString().TrimEnd();
    }

    private static ServiceLevel ParseServiceLevel(string? value)
    {
        if (!string.IsNullOrWhiteSpace(value) &&
            Enum.TryParse<ServiceLevel>(value, ignoreCase: true, out var parsed))
        {
            return parsed;
        }

        return ServiceLevel.Ground;
    }
}
