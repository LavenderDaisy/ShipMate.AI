namespace ShipMate.AI.Console.Printing;

/// <summary>
/// A no-op printer used when no printer is configured. It reports success without
/// sending anything, so label generation still works (files are written) but nothing
/// is physically printed. Follows the Null Object pattern to avoid null checks.
/// </summary>
public sealed class NullZplPrinter : IZplPrinter
{
    public string Destination => "none (file output only)";

    public PrintResult Print(string zpl) => new()
    {
        Success = true,
        Destination = Destination,
        BytesSent = 0
    };
}
