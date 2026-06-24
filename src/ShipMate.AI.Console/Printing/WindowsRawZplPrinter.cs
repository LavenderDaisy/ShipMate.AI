using System.Runtime.InteropServices;
using System.Text;

namespace ShipMate.AI.Console.Printing;

/// <summary>
/// Sends raw ZPL to a Windows printer by name through the print spooler using the "RAW"
/// data type. This bypasses the driver's text rendering so the ZPL commands reach the
/// printer verbatim — the standard technique for driving label printers (including
/// virtual ZPL printers) from Windows. Wraps the classic winspool.drv P/Invoke calls.
/// </summary>
public sealed class WindowsRawZplPrinter : IZplPrinter
{
    private readonly string _printerName;

    public WindowsRawZplPrinter(string printerName)
    {
        _printerName = printerName;
    }

    public string Destination => $"Windows printer '{_printerName}'";

    public PrintResult Print(string zpl)
    {
        if (!OperatingSystem.IsWindows())
        {
            return new PrintResult
            {
                Success = false,
                Destination = Destination,
                BytesSent = 0,
                Error = "Raw spooler printing is only supported on Windows."
            };
        }

        // ZPL is ASCII; send the bytes unmodified.
        var bytes = Encoding.ASCII.GetBytes(zpl);
        var unmanaged = Marshal.AllocCoTaskMem(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, unmanaged, bytes.Length);
            var ok = SendBytesToPrinter(_printerName, unmanaged, bytes.Length, out var error);
            return new PrintResult
            {
                Success = ok,
                Destination = Destination,
                BytesSent = ok ? bytes.Length : 0,
                Error = ok ? null : error
            };
        }
        finally
        {
            Marshal.FreeCoTaskMem(unmanaged);
        }
    }

    private static bool SendBytesToPrinter(string printerName, IntPtr data, int count, out string? error)
    {
        error = null;
        var di = new DOCINFOA
        {
            pDocName = "ShipMate ZPL Label",
            pDataType = "RAW"
        };

        if (!OpenPrinter(printerName.Normalize(), out var hPrinter, IntPtr.Zero))
        {
            error = $"OpenPrinter failed for '{printerName}' (Win32 error {Marshal.GetLastWin32Error()}).";
            return false;
        }

        try
        {
            if (StartDocPrinter(hPrinter, 1, di) == 0)
            {
                error = $"StartDocPrinter failed (Win32 error {Marshal.GetLastWin32Error()}).";
                return false;
            }

            try
            {
                if (!StartPagePrinter(hPrinter))
                {
                    error = $"StartPagePrinter failed (Win32 error {Marshal.GetLastWin32Error()}).";
                    return false;
                }

                try
                {
                    if (!WritePrinter(hPrinter, data, count, out var written) || written != count)
                    {
                        error = $"WritePrinter wrote {written}/{count} bytes (Win32 error {Marshal.GetLastWin32Error()}).";
                        return false;
                    }
                }
                finally
                {
                    EndPagePrinter(hPrinter);
                }
            }
            finally
            {
                EndDocPrinter(hPrinter);
            }
        }
        finally
        {
            ClosePrinter(hPrinter);
        }

        return true;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    private sealed class DOCINFOA
    {
        [MarshalAs(UnmanagedType.LPStr)] public string pDocName = string.Empty;
        [MarshalAs(UnmanagedType.LPStr)] public string? pOutputFile;
        [MarshalAs(UnmanagedType.LPStr)] public string pDataType = string.Empty;
    }

    [DllImport("winspool.drv", EntryPoint = "OpenPrinterA", SetLastError = true, CharSet = CharSet.Ansi)]
    private static extern bool OpenPrinter(string src, out IntPtr hPrinter, IntPtr pd);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool ClosePrinter(IntPtr hPrinter);

    [DllImport("winspool.drv", EntryPoint = "StartDocPrinterA", SetLastError = true, CharSet = CharSet.Ansi)]
    private static extern int StartDocPrinter(IntPtr hPrinter, int level, DOCINFOA di);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool EndDocPrinter(IntPtr hPrinter);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool StartPagePrinter(IntPtr hPrinter);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool EndPagePrinter(IntPtr hPrinter);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool WritePrinter(IntPtr hPrinter, IntPtr pBytes, int dwCount, out int dwWritten);
}
