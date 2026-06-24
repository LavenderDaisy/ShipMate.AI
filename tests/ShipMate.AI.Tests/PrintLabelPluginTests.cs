using ShipMate.AI.Console.Carriers;
using ShipMate.AI.Console.Plugins;
using ShipMate.AI.Console.Printing;

namespace ShipMate.AI.Tests;

/// <summary>
/// Tests for <see cref="PrintLabelPlugin"/> orchestration using a fake printer that records
/// what it was asked to print. These verify the plugin's decision logic (resolve shipment,
/// print, surface success/failure) without any real printer or network access.
/// </summary>
[TestFixture]
public class PrintLabelPluginTests
{
    /// <summary>An in-test printer that records the last payload and returns a canned result.</summary>
    private sealed class FakeZplPrinter : IZplPrinter
    {
        private readonly bool _succeed;
        public FakeZplPrinter(bool succeed) => _succeed = succeed;

        public string? LastZpl { get; private set; }
        public int CallCount { get; private set; }
        public string Destination => "fake-printer";

        public PrintResult Print(string zpl)
        {
            LastZpl = zpl;
            CallCount++;
            return new PrintResult
            {
                Success = _succeed,
                Destination = Destination,
                BytesSent = _succeed ? zpl.Length : 0,
                Error = _succeed ? null : "simulated printer failure"
            };
        }
    }

    private string _outputDir = null!;
    private ShipmentStore _store = null!;
    private LabelService _labelService = null!;

    [SetUp]
    public void SetUp()
    {
        _outputDir = Path.Combine(Path.GetTempPath(), "shipmate-tests", Guid.NewGuid().ToString("N"));
        _store = new ShipmentStore();
        _labelService = new LabelService(_store, _outputDir);

        _store.Add(new ShipmentResult
        {
            TrackingNumber = "US999000111",
            Carrier = "USPS",
            ServiceLevel = ServiceLevel.Overnight,
            TotalCharge = 50m,
            EstimatedDelivery = DateTime.UtcNow.Date.AddDays(1),
            Currency = "USD"
        });
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_outputDir))
        {
            Directory.Delete(_outputDir, recursive: true);
        }
    }

    [Test]
    public void PrintLabel_PrintsZpl_ForBookedShipment()
    {
        var printer = new FakeZplPrinter(succeed: true);
        var plugin = new PrintLabelPlugin(_labelService, printer, _outputDir);

        var message = plugin.PrintLabel("US999000111");

        Assert.Multiple(() =>
        {
            Assert.That(printer.CallCount, Is.EqualTo(1));
            Assert.That(printer.LastZpl, Does.StartWith("^XA").And.Contains("US999000111"));
            Assert.That(message, Does.Contain("Printed").And.Contains("US999000111"));
        });
    }

    [Test]
    public void PrintLabel_ReturnsNotFound_AndDoesNotPrint_ForUnknownTracking()
    {
        var printer = new FakeZplPrinter(succeed: true);
        var plugin = new PrintLabelPlugin(_labelService, printer, _outputDir);

        var message = plugin.PrintLabel("NOPE");

        Assert.Multiple(() =>
        {
            Assert.That(printer.CallCount, Is.EqualTo(0));
            Assert.That(message, Does.Contain("No shipment found"));
        });
    }

    [Test]
    public void PrintLabel_SurfacesPrinterError_WhenPrintingFails()
    {
        var printer = new FakeZplPrinter(succeed: false);
        var plugin = new PrintLabelPlugin(_labelService, printer, _outputDir);

        var message = plugin.PrintLabel("US999000111");

        Assert.Multiple(() =>
        {
            Assert.That(message, Does.Contain("Failed to print"));
            Assert.That(message, Does.Contain("simulated printer failure"));
        });
    }

    [Test]
    public void BuyAndPrintCarrierLabel_ReportsUnavailable_WhenEasyPostNotConfigured()
    {
        var printer = new FakeZplPrinter(succeed: true);
        // easyPostLabel defaults to null -> real labels unavailable.
        var plugin = new PrintLabelPlugin(_labelService, printer, _outputDir);

        var message = plugin.BuyAndPrintCarrierLabel("30301", "10001", 2);

        Assert.Multiple(() =>
        {
            Assert.That(printer.CallCount, Is.EqualTo(0));
            Assert.That(message, Does.Contain("EasyPost"));
        });
    }
}
