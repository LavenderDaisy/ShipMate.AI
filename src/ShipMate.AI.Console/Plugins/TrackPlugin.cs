using System.ComponentModel;
using System.Text;
using Microsoft.SemanticKernel;
using ShipMate.AI.Console.Carriers;

namespace ShipMate.AI.Console.Plugins;

/// <summary>
/// Semantic Kernel plugin that lets the LLM look up tracking status for a shipment
/// created earlier in the session. Demonstrates state flowing across tool calls:
/// create_shipment mints a tracking number, track_shipment resolves it.
/// </summary>
public sealed class TrackPlugin
{
    private readonly ShippingService _shippingService;

    public TrackPlugin(ShippingService shippingService)
    {
        _shippingService = shippingService;
    }

    [KernelFunction("track_shipment")]
    [Description("Gets the current status and scan history for a shipment by its " +
                 "tracking number. Use this when the user asks where a package is or " +
                 "for its delivery status.")]
    public string TrackShipment(
        [Description("The carrier tracking number returned when the shipment was booked.")]
        string trackingNumber)
    {
        var info = _shippingService.Track(trackingNumber);

        if (info is null)
        {
            return $"No shipment found for tracking number '{trackingNumber}'. " +
                   "It may not have been booked in this session.";
        }

        var sb = new StringBuilder();
        sb.AppendLine($"{info.Carrier} {info.TrackingNumber} — {info.CurrentStatus}");
        sb.AppendLine($"Estimated delivery: {info.EstimatedDelivery:yyyy-MM-dd}");
        sb.AppendLine("History:");
        foreach (var e in info.Events)
        {
            sb.AppendLine($"- {e.Timestamp:yyyy-MM-dd HH:mm} UTC: {e.Status} @ {e.Location}");
        }

        return sb.ToString().TrimEnd();
    }
}
