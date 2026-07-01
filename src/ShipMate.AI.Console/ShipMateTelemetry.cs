using System.Diagnostics;

namespace ShipMate.AI.Console;

/// <summary>
/// Central ActivitySource for ShipMate AI distributed tracing. All spans (chat requests,
/// LLM calls, tool invocations, carrier API calls) are created from this single source so
/// they form a coherent trace tree. The source name "ShipMate.AI" is what backends like
/// Jaeger or the OTLP collector use to identify this service's telemetry.
/// </summary>
public static class ShipMateTelemetry
{
    /// <summary>The single ActivitySource all ShipMate spans are created from.</summary>
    public static readonly ActivitySource ActivitySource = new("ShipMate.AI", "1.0.0");

    /// <summary>Starts a span (Activity) with the given name, or returns null if no
    /// listener is active (the default when tracing is not configured).</summary>
    public static Activity? StartSpan(string name)
    {
        return ActivitySource.StartActivity(name);
    }
}
