using ShipMate.AI.Console.Carriers;

namespace ShipMate.AI.Tests;

/// <summary>
/// Tests for <see cref="LabelService"/> ZPL generation. They write to a temp directory
/// and assert on the emitted ZPL structure (no printer required).
/// </summary>
[TestFixture]
public class LabelServiceTests
{
    private string _outputDir = null!;
    private ShipmentStore _store = null!;
    private LabelService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _outputDir = Path.Combine(Path.GetTempPath(), "shipmate-tests", Guid.NewGuid().ToString("N"));
        _store = new ShipmentStore();
        _service = new LabelService(_store, _outputDir);

        _store.Add(new ShipmentResult
        {
            TrackingNumber = "US123456789",
            Carrier = "USPS",
            ServiceLevel = ServiceLevel.Overnight,
            TotalCharge = 42.50m,
            EstimatedDelivery = new DateTime(2026, 6, 17, 0, 0, 0, DateTimeKind.Utc),
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
    public void RenderLabel_ReturnsNull_ForUnknownTrackingNumber()
    {
        Assert.That(_service.RenderLabel("DOES-NOT-EXIST"), Is.Null);
    }

    [Test]
    public void RenderLabel_ProducesWellFormedZpl()
    {
        var label = _service.RenderLabel("US123456789");

        Assert.That(label, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(label!.Content, Does.StartWith("^XA"));
            Assert.That(label.Content, Does.EndWith("^XZ"));
            Assert.That(label.Format, Is.EqualTo(LabelFormat.Zpl));
            Assert.That(label.WidthInches, Is.EqualTo(4));
            Assert.That(label.HeightInches, Is.EqualTo(6));
        });
    }

    [Test]
    public void RenderLabel_EmbedsCarrierAndTrackingBarcode()
    {
        var label = _service.RenderLabel("US123456789");

        Assert.That(label, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(label!.Content, Does.Contain("USPS"));
            // Code128 barcode field with the tracking number.
            Assert.That(label.Content, Does.Contain("^BCN"));
            Assert.That(label.Content, Does.Contain("US123456789"));
        });
    }

    [Test]
    public void RenderLabel_WritesFileToDisk()
    {
        var label = _service.RenderLabel("US123456789");

        Assert.That(label, Is.Not.Null);
        Assert.That(File.Exists(label!.FilePath), Is.True);
        Assert.That(File.ReadAllText(label.FilePath), Is.EqualTo(label.Content));
    }
}
