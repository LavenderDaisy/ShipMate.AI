using System.Net;
using System.Net.Sockets;
using System.Text;
using ShipMate.AI.Console.Printing;

namespace ShipMate.AI.Tests;

/// <summary>
/// Tests for <see cref="TcpZplPrinter"/>. The success path uses a real loopback
/// <see cref="TcpListener"/> to verify the exact ZPL bytes are transmitted; the failure
/// path points at a closed port to verify graceful error reporting (no exception thrown).
/// </summary>
[TestFixture]
public class TcpZplPrinterTests
{
    private const string Zpl = "^XA^FDtcp-test^FS^XZ";

    [Test]
    public void Print_SendsExactZplBytes_ToListeningSocket()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        // Accept one connection and read everything the printer sends.
        var receivedTask = Task.Run(() =>
        {
            using var client = listener.AcceptTcpClient();
            using var stream = client.GetStream();
            using var ms = new MemoryStream();
            var buffer = new byte[1024];
            int read;
            // Read until the client closes the connection.
            while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                ms.Write(buffer, 0, read);
            }
            return ms.ToArray();
        });

        try
        {
            var printer = new TcpZplPrinter("127.0.0.1", port);
            var result = printer.Print(Zpl);

            var received = receivedTask.Wait(TimeSpan.FromSeconds(5))
                ? receivedTask.Result
                : Array.Empty<byte>();

            Assert.Multiple(() =>
            {
                Assert.That(result.Success, Is.True);
                Assert.That(result.BytesSent, Is.EqualTo(Encoding.ASCII.GetByteCount(Zpl)));
                Assert.That(Encoding.ASCII.GetString(received), Is.EqualTo(Zpl));
            });
        }
        finally
        {
            listener.Stop();
        }
    }

    [Test]
    public void Print_ReturnsFailure_WhenNothingIsListening()
    {
        // Bind to grab a free port, then release it so the connection is refused.
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var closedPort = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();

        var printer = new TcpZplPrinter("127.0.0.1", closedPort, timeoutMs: 1000);
        var result = printer.Print(Zpl);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.BytesSent, Is.EqualTo(0));
            Assert.That(result.Error, Is.Not.Null.And.Not.Empty);
        });
    }

    [Test]
    public void Destination_ShowsHostAndPort()
    {
        var printer = new TcpZplPrinter("192.168.1.50", 9100);

        Assert.That(printer.Destination, Is.EqualTo("192.168.1.50:9100"));
    }
}
