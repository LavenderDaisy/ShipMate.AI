using System.ComponentModel;
using System.Text;
using Microsoft.SemanticKernel;
using ShipMate.AI.Console.Carriers;
using ShipMate.AI.Console.Printing;

namespace ShipMate.AI.Console.Plugins;

/// <summary>
/// Semantic Kernel plugin that sends ZPL labels to a physical/virtual label printer.
/// It can print either the self-rendered demo label for a booked shipment, or — when
/// EasyPost is configured — buy a real carrier label and print its ZPL.
/// </summary>
public sealed class PrintLabelPlugin
{
    private readonly LabelService _labelService;
    private readonly IZplPrinter _printer;
    private readonly EasyPostLabelService? _easyPostLabel;
    private readonly string _outputDirectory;

    public PrintLabelPlugin(
        LabelService labelService,
        IZplPrinter printer,
        string outputDirectory,
        EasyPostLabelService? easyPostLabel = null)
    {
        _labelService = labelService;
        _printer = printer;
        _outputDirectory = outputDirectory;
        _easyPostLabel = easyPostLabel;
    }

    [KernelFunction("print_label")]
    [Description("Sends the shipping label for a booked shipment to the label printer. " +
                 "Use this when the user asks to print a label for a shipment they already " +
                 "booked (identified by its tracking number).")]
    public string PrintLabel(
        [Description("The tracking number of a shipment booked earlier via create_shipment.")]
        string trackingNumber)
    {
        var label = _labelService.RenderLabel(trackingNumber);
        if (label is null)
        {
            return $"No shipment found for tracking number '{trackingNumber}'. Book it first.";
        }

        var result = _printer.Print(label.Content);
        return DescribeResult(result, $"label for {label.Carrier} {label.TrackingNumber}");
    }

    [KernelFunction("buy_and_print_carrier_label")]
    [Description("Buys a REAL carrier shipping label from EasyPost for the given shipment " +
                 "and prints its ZPL to the label printer. Use this when the user wants an " +
                 "actual scannable carrier label (not the demo label). Returns the tracking number.")]
    public string BuyAndPrintCarrierLabel(
        [Description("Origin postal/ZIP code, e.g. '30301'.")] string originZip,
        [Description("Destination postal/ZIP code, e.g. '10001'.")] string destinationZip,
        [Description("Package weight in pounds.")] double weightLbs,
        [Description("True if delivering to a residential address.")] bool residential = false)
    {
        if (_easyPostLabel is null)
        {
            return "Real carrier labels require EasyPost to be configured (set EasyPost:ApiKey). " +
                   "You can still print the demo label via print_label.";
        }

        EasyPostLabel label;
        try
        {
            label = _easyPostLabel.BuyLabel(new RateRequest
            {
                OriginZip = originZip,
                DestinationZip = destinationZip,
                WeightLbs = weightLbs,
                Residential = residential
            });
        }
        catch (Exception ex)
        {
            return $"Failed to buy the carrier label: {ex.Message}";
        }

        // The shipment was bought, but the label file could not be downloaded (e.g. the
        // CDN is unreachable on this network). Return the tracking number and label URL.
        if (label.Zpl is null)
        {
            return $"Bought a real {label.Carrier} {label.Service} label, tracking {label.TrackingNumber}, " +
                   $"but could not download the ZPL on this network. Retrieve it here: {label.LabelUrl}";
        }

        // Persist the real ZPL alongside the demo labels for inspection.
        Directory.CreateDirectory(_outputDirectory);
        var filePath = Path.Combine(_outputDirectory, $"carrier_label_{label.TrackingNumber}.zpl");
        File.WriteAllText(filePath, label.Zpl, Encoding.ASCII);

        var result = _printer.Print(label.Zpl);
        var summary = DescribeResult(result,
            $"real {label.Carrier} {label.Service} label, tracking {label.TrackingNumber}");

        return $"{summary} Saved to: {filePath}.";
    }

    private string DescribeResult(PrintResult result, string what)
    {
        if (result.Success)
        {
            return $"Printed {what} to {result.Destination} ({result.BytesSent} bytes).";
        }

        return $"Failed to print {what} to {result.Destination}: {result.Error}";
    }
}
