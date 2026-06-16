using System.ComponentModel;
using Microsoft.SemanticKernel;
using ShipMate.AI.Console.Carriers;

namespace ShipMate.AI.Console.Plugins;

/// <summary>
/// Semantic Kernel plugin that renders a printable 4x6 ZPL shipping label for a booked
/// shipment. In a multi-step flow like "ship it and print the label", the model calls
/// create_shipment first, then feeds the returned tracking number into this tool.
/// </summary>
public sealed class LabelPrintPlugin
{
    private readonly LabelService _labelService;

    public LabelPrintPlugin(LabelService labelService)
    {
        _labelService = labelService;
    }

    [KernelFunction("render_label")]
    [Description("Generates a printable 4x6 inch ZPL shipping label for a shipment that " +
                 "has already been booked. Call this when the user asks to print, create, " +
                 "or generate a shipping label. Requires the shipment's tracking number.")]
    public string RenderLabel(
        [Description("The tracking number of a shipment booked earlier via create_shipment.")]
        string trackingNumber)
    {
        var label = _labelService.RenderLabel(trackingNumber);

        if (label is null)
        {
            return $"No shipment found for tracking number '{trackingNumber}'. " +
                   "Book the shipment first, then request a label.";
        }

        return $"Generated a {label.WidthInches}x{label.HeightInches} {label.Format} label " +
               $"for {label.Carrier} {label.TrackingNumber}. Saved to: {label.FilePath}. " +
               $"The ZPL can be sent to a thermal printer or previewed in a ZPL viewer.";
    }
}
