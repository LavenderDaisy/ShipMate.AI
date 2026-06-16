namespace ShipMate.AI.Console.Carriers;

/// <summary>
/// Supported label output formats. ZPL (Zebra Programming Language) is the de-facto
/// standard for thermal shipping-label printers; 4x6 inch is the standard label size.
/// </summary>
public enum LabelFormat
{
    Zpl
}

/// <summary>
/// A rendered shipping label: the raw printer payload plus where it was saved on disk
/// so it can be sent to a thermal printer or previewed in a ZPL viewer.
/// </summary>
public sealed record LabelResult
{
    public required string TrackingNumber { get; init; }
    public required string Carrier { get; init; }
    public required LabelFormat Format { get; init; }
    public required string Content { get; init; }
    public required string FilePath { get; init; }
    public required int WidthInches { get; init; }
    public required int HeightInches { get; init; }
}
