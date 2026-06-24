namespace ShipMate.AI.Console.Printing;

/// <summary>
/// Result of sending a ZPL payload to a printer destination.
/// </summary>
public sealed record PrintResult
{
    public required bool Success { get; init; }
    public required string Destination { get; init; }
    public required int BytesSent { get; init; }
    public string? Error { get; init; }
}

/// <summary>
/// Sends raw ZPL to a physical or virtual label printer. Implementations target a
/// specific transport (Windows print spooler, raw TCP socket, ...). ZPL is sent as
/// raw bytes so the printer interprets the commands directly rather than rendering text.
/// </summary>
public interface IZplPrinter
{
    /// <summary>A human-readable description of where this printer sends data.</summary>
    string Destination { get; }

    /// <summary>Sends the given ZPL payload to the printer.</summary>
    PrintResult Print(string zpl);
}
