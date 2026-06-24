using System.Net.Sockets;
using System.Text;

namespace ShipMate.AI.Console.Printing;

/// <summary>
/// Sends raw ZPL to a network label printer over a TCP socket. Most Zebra-compatible
/// printers listen on port 9100 (the de-facto "raw"/JetDirect printing port) and accept
/// ZPL directly on the stream. Useful for shop-floor printers addressed by IP.
/// </summary>
public sealed class TcpZplPrinter : IZplPrinter
{
    private readonly string _host;
    private readonly int _port;
    private readonly int _timeoutMs;

    public TcpZplPrinter(string host, int port = 9100, int timeoutMs = 5000)
    {
        _host = host;
        _port = port;
        _timeoutMs = timeoutMs;
    }

    public string Destination => $"{_host}:{_port}";

    public PrintResult Print(string zpl)
    {
        var bytes = Encoding.ASCII.GetBytes(zpl);

        try
        {
            using var client = new TcpClient();
            var connect = client.BeginConnect(_host, _port, null, null);
            if (!connect.AsyncWaitHandle.WaitOne(_timeoutMs))
            {
                return Failure($"Connection to {Destination} timed out after {_timeoutMs} ms.");
            }

            client.EndConnect(connect);

            using var stream = client.GetStream();
            stream.WriteTimeout = _timeoutMs;
            stream.Write(bytes, 0, bytes.Length);
            stream.Flush();

            return new PrintResult
            {
                Success = true,
                Destination = Destination,
                BytesSent = bytes.Length
            };
        }
        catch (Exception ex)
        {
            return Failure(ex.Message);
        }
    }

    private PrintResult Failure(string error) => new()
    {
        Success = false,
        Destination = Destination,
        BytesSent = 0,
        Error = error
    };
}
