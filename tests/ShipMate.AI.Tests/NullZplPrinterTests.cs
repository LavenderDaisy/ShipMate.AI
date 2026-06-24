using ShipMate.AI.Console.Printing;

namespace ShipMate.AI.Tests;

/// <summary>
/// Tests for <see cref="NullZplPrinter"/>, the Null Object used when no printer is
/// configured. It should report success without sending any bytes, so label generation
/// still works while nothing is physically printed.
/// </summary>
[TestFixture]
public class NullZplPrinterTests
{
    [Test]
    public void Print_ReportsSuccessWithoutSendingBytes()
    {
        var printer = new NullZplPrinter();

        var result = printer.Print("^XA^FDhello^FS^XZ");

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(result.BytesSent, Is.EqualTo(0));
            Assert.That(result.Error, Is.Null);
        });
    }

    [Test]
    public void Destination_DescribesFileOnlyMode()
    {
        Assert.That(new NullZplPrinter().Destination, Does.Contain("file"));
    }
}
