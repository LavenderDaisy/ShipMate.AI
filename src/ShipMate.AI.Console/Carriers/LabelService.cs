using System.Text;

namespace ShipMate.AI.Console.Carriers;

/// <summary>
/// Generates 4x6 inch ZPL shipping labels for booked shipments. This mirrors the role
/// of a printing service in a real shipping platform: take a shipment, lay out carrier
/// branding, a scannable tracking barcode, and human-readable text, then emit a printer
/// payload. ZPL is rendered at 203 dpi (8 dots/mm), the most common thermal resolution.
/// </summary>
public sealed class LabelService
{
    // 4x6 inch label at 203 dpi.
    private const int Dpi = 203;
    private const int WidthInches = 4;
    private const int HeightInches = 6;
    private const int WidthDots = WidthInches * Dpi;   // 812

    private readonly ShipmentStore _store;
    private readonly string _outputDirectory;

    public LabelService(ShipmentStore store, string outputDirectory)
    {
        _store = store;
        _outputDirectory = outputDirectory;
    }

    /// <summary>
    /// Renders a ZPL label for the given tracking number and writes it to disk.
    /// Returns null if no shipment with that tracking number exists in this session.
    /// </summary>
    public LabelResult? RenderLabel(string trackingNumber)
    {
        if (!_store.TryGet(trackingNumber, out var shipment) || shipment is null)
        {
            return null;
        }

        var zpl = BuildZpl(shipment);

        Directory.CreateDirectory(_outputDirectory);
        var filePath = Path.Combine(_outputDirectory, $"label_{shipment.TrackingNumber}.zpl");
        File.WriteAllText(filePath, zpl, Encoding.ASCII);

        return new LabelResult
        {
            TrackingNumber = shipment.TrackingNumber,
            Carrier = shipment.Carrier,
            Format = LabelFormat.Zpl,
            Content = zpl,
            FilePath = filePath,
            WidthInches = WidthInches,
            HeightInches = HeightInches
        };
    }

    private static string BuildZpl(ShipmentResult shipment)
    {
        // Center the barcode horizontally: Code128 module width ~2 dots, the field is
        // positioned with a left margin that keeps a 4-inch label balanced.
        var shipDate = DateTime.UtcNow.ToString("yyyy-MM-dd");

        var sb = new StringBuilder();
        sb.Append("^XA");                                  // start of label
        sb.Append("^CI28");                                // UTF-8 encoding
        sb.Append("^PW").Append(WidthDots);                // print width (dots)
        sb.Append("^LL1218");                              // label length (6in @ 203dpi)

        // --- Carrier banner ---
        sb.Append("^FO30,30^A0N,70,70^FD").Append(shipment.Carrier).Append("^FS");
        sb.Append("^FO30,110^A0N,40,40^FD").Append(shipment.ServiceLevel).Append("^FS");

        // --- Divider ---
        sb.Append("^FO30,170^GB752,3,3^FS");

        // --- Service / date block ---
        sb.Append("^FO30,200^A0N,30,30^FDShip Date: ").Append(shipDate).Append("^FS");
        sb.Append("^FO30,240^A0N,30,30^FDEst. Delivery: ")
          .Append(shipment.EstimatedDelivery.ToString("yyyy-MM-dd")).Append("^FS");
        sb.Append("^FO30,280^A0N,30,30^FDBilled: ")
          .Append(shipment.TotalCharge.ToString("0.00")).Append(' ').Append(shipment.Currency).Append("^FS");

        // --- Tracking barcode (Code128) with human-readable line ---
        sb.Append("^FO30,360^A0N,34,34^FDTRACKING #^FS");
        sb.Append("^BY3,2,180");                            // bar width 3, ratio 2, height 180
        sb.Append("^FO60,410^BCN,180,Y,N,N^FD").Append(shipment.TrackingNumber).Append("^FS");

        // --- Footer ---
        sb.Append("^FO30,650^A0N,24,24^FDShipMate AI - demo label^FS");

        sb.Append("^XZ");                                  // end of label
        return sb.ToString();
    }
}
