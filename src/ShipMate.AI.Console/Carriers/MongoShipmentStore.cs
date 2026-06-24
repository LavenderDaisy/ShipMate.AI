using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;

namespace ShipMate.AI.Console.Carriers;

/// <summary>
/// Durable <see cref="IShipmentStore"/> backed by MongoDB. Shipments persist across runs,
/// keyed by tracking number (used as the document _id, giving idempotent upserts). A
/// dedicated BSON document type keeps the domain <see cref="ShipmentResult"/> free of any
/// persistence concerns (separation of concerns / persistence ignorance).
/// </summary>
public sealed class MongoShipmentStore : IShipmentStore
{
    private readonly IMongoCollection<ShipmentDocument> _collection;

    public MongoShipmentStore(string connectionString, string database = "shipmate", string collection = "shipments")
    {
        var client = new MongoClient(connectionString);
        _collection = client.GetDatabase(database).GetCollection<ShipmentDocument>(collection);
    }

    public void Add(ShipmentResult shipment)
    {
        var doc = ShipmentDocument.FromDomain(shipment);
        _collection.ReplaceOne(
            d => d.TrackingNumber == doc.TrackingNumber,
            doc,
            new ReplaceOptions { IsUpsert = true });
    }

    public bool TryGet(string trackingNumber, out ShipmentResult? shipment)
    {
        var doc = _collection.Find(d => d.TrackingNumber == trackingNumber).FirstOrDefault();
        shipment = doc?.ToDomain();
        return doc is not null;
    }

    public IReadOnlyCollection<ShipmentResult> All =>
        _collection.Find(FilterDefinition<ShipmentDocument>.Empty)
            .ToList()
            .Select(d => d.ToDomain())
            .ToList();

    /// <summary>BSON persistence shape for a shipment. Separate from the domain record.</summary>
    internal sealed class ShipmentDocument
    {
        [BsonId]
        public string TrackingNumber { get; set; } = string.Empty;
        public string Carrier { get; set; } = string.Empty;
        public string ServiceLevel { get; set; } = string.Empty;

        [BsonRepresentation(BsonType.Decimal128)]
        public decimal TotalCharge { get; set; }

        public DateTime EstimatedDelivery { get; set; }
        public string Currency { get; set; } = "USD";

        public static ShipmentDocument FromDomain(ShipmentResult s) => new()
        {
            TrackingNumber = s.TrackingNumber,
            Carrier = s.Carrier,
            ServiceLevel = s.ServiceLevel.ToString(),
            TotalCharge = s.TotalCharge,
            EstimatedDelivery = s.EstimatedDelivery,
            Currency = s.Currency
        };

        public ShipmentResult ToDomain() => new()
        {
            TrackingNumber = TrackingNumber,
            Carrier = Carrier,
            ServiceLevel = Enum.TryParse<ServiceLevel>(ServiceLevel, out var sl) ? sl : Carriers.ServiceLevel.Ground,
            TotalCharge = TotalCharge,
            EstimatedDelivery = EstimatedDelivery,
            Currency = Currency
        };
    }
}
