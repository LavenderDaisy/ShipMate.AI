namespace ShipMate.AI.Console.Carriers;

/// <summary>
/// Abstraction over a carrier's rating capability. In the real StarShip server each
/// carrier (UPS, FedEx, ...) plugs into the engine via BaseCarrierEngine and a Rate
/// transaction. Here we model the same contract so the AI layer is carrier-agnostic.
/// </summary>
public interface ICarrierRateEngine
{
    /// <summary>Carrier display name, e.g. "UPS", "FedEx".</summary>
    string CarrierName { get; }

    /// <summary>Returns quotes for the requested shipment, one per supported service.</summary>
    IReadOnlyList<RateQuote> GetRates(RateRequest request);
}
