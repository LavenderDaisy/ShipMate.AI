using ShipMate.AI.Console.Carriers;

namespace ShipMate.AI.Tests;

/// <summary>
/// Tests for <see cref="InMemoryShipmentStore"/>, the default per-session shipment store.
/// </summary>
[TestFixture]
public class InMemoryShipmentStoreTests
{
    private static ShipmentResult Sample(string tracking) => new()
    {
        TrackingNumber = tracking,
        Carrier = "USPS",
        ServiceLevel = ServiceLevel.Ground,
        TotalCharge = 12.34m,
        EstimatedDelivery = DateTime.UtcNow.Date.AddDays(3),
        Currency = "USD"
    };

    [Test]
    public void Add_ThenTryGet_ReturnsShipment()
    {
        var store = new InMemoryShipmentStore();
        store.Add(Sample("US1"));

        var found = store.TryGet("US1", out var shipment);

        Assert.Multiple(() =>
        {
            Assert.That(found, Is.True);
            Assert.That(shipment, Is.Not.Null);
            Assert.That(shipment!.Carrier, Is.EqualTo("USPS"));
        });
    }

    [Test]
    public void TryGet_ReturnsFalse_ForUnknownTracking()
    {
        var store = new InMemoryShipmentStore();

        var found = store.TryGet("missing", out var shipment);

        Assert.Multiple(() =>
        {
            Assert.That(found, Is.False);
            Assert.That(shipment, Is.Null);
        });
    }

    [Test]
    public void Add_WithSameTracking_Upserts()
    {
        var store = new InMemoryShipmentStore();
        store.Add(Sample("US1"));
        store.Add(Sample("US1") with { Carrier = "FedEx" });

        store.TryGet("US1", out var shipment);

        Assert.Multiple(() =>
        {
            Assert.That(store.All, Has.Count.EqualTo(1));
            Assert.That(shipment!.Carrier, Is.EqualTo("FedEx"));
        });
    }
}
