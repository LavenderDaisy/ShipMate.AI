using System.ComponentModel;
using Microsoft.SemanticKernel;
using ShipMate.AI.Console.Carriers;

namespace ShipMate.AI.Console.Plugins;

/// <summary>
/// Semantic Kernel plugin that lets the LLM book a shipment. In a multi-step request
/// like "find the cheapest overnight option and ship it", the model typically calls
/// get_shipping_rates first, then feeds the chosen carrier/service into this tool.
/// </summary>
public sealed class ShipPlugin
{
    private readonly ShippingService _shippingService;

    public ShipPlugin(ShippingService shippingService)
    {
        _shippingService = shippingService;
    }

    [KernelFunction("create_shipment")]
    [Description("Books a shipment with a specific carrier and service, returning a " +
                 "tracking number. Call this after the user has chosen (or asked you to " +
                 "pick) a carrier and service level. Requires carrier and serviceLevel; " +
                 "if unknown, call get_shipping_rates first to decide.")]
    public string CreateShipment(
        [Description("Carrier to ship with, e.g. 'UPS', 'FedEx', 'USPS'.")]
        string carrier,
        [Description("Service level. One of: Ground, TwoDay, Overnight.")]
        string serviceLevel,
        [Description("Origin postal/ZIP code, e.g. '30301'.")]
        string originZip,
        [Description("Destination postal/ZIP code, e.g. '10001'.")]
        string destinationZip,
        [Description("Package weight in pounds.")]
        double weightLbs,
        [Description("True if delivering to a residential address.")]
        bool residential = false)
    {
        if (!Enum.TryParse<ServiceLevel>(serviceLevel, ignoreCase: true, out var parsedService))
        {
            return $"Unknown service level '{serviceLevel}'. Use Ground, TwoDay, or Overnight.";
        }

        var result = _shippingService.CreateShipment(new ShipmentRequest
        {
            Carrier = carrier,
            ServiceLevel = parsedService,
            OriginZip = originZip,
            DestinationZip = destinationZip,
            WeightLbs = weightLbs,
            Residential = residential
        });

        using var span = ShipMateTelemetry.StartSpan("tool.create_shipment");
        span?.SetTag("ship.carrier", result.Carrier);
        span?.SetTag("ship.tracking_number", result.TrackingNumber);
        span?.SetTag("ship.total_charge", (double)result.TotalCharge);

        return $"Shipment booked with {result.Carrier} {result.ServiceLevel}. " +
               $"Tracking number: {result.TrackingNumber}. " +
               $"Charge: {result.TotalCharge:C} {result.Currency}. " +
               $"Estimated delivery: {result.EstimatedDelivery:yyyy-MM-dd}.";
    }
}
