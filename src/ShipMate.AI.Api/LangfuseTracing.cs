using System.Text;
using Microsoft.Extensions.Configuration;
using OpenTelemetry.Exporter;
using OpenTelemetry.Trace;

namespace ShipMate.AI.Api;

/// <summary>
/// Bridges Semantic Kernel's GenAI OpenTelemetry traces to Langfuse.
///
/// Langfuse has no native .NET SDK, so we use the industry-standard path instead:
/// Semantic Kernel emits GenAI spans (model, token usage, prompts, tool calls) as
/// OpenTelemetry activities, and we export them over OTLP/HTTP to Langfuse's
/// ingestion endpoint (<c>/api/public/otel/v1/traces</c>). This is the same
/// "OTel as the standard, Langfuse as the LLM backend" split used in production.
/// </summary>
public static class LangfuseTracing
{
    /// <summary>
    /// Enables Semantic Kernel model diagnostics. MUST be called before the kernel is
    /// built. When <paramref name="includeSensitive"/> is true, prompt and completion
    /// text is captured in the spans so Langfuse shows the actual conversation
    /// (disable this if traces might contain data you don't want stored).
    /// </summary>
    public static void EnableSemanticKernelDiagnostics(bool includeSensitive)
    {
        AppContext.SetSwitch(
            "Microsoft.SemanticKernel.Experimental.GenAI.EnableOTelDiagnostics", true);

        if (includeSensitive)
        {
            AppContext.SetSwitch(
                "Microsoft.SemanticKernel.Experimental.GenAI.EnableOTelDiagnosticsSensitive", true);
        }
    }

    /// <summary>True when both Langfuse keys are present in configuration.</summary>
    public static bool IsConfigured(IConfiguration config) =>
        !string.IsNullOrWhiteSpace(config["Langfuse:PublicKey"]) &&
        !string.IsNullOrWhiteSpace(config["Langfuse:SecretKey"]);

    /// <summary>
    /// Adds the Langfuse OTLP exporter to the tracer provider. No-op when the Langfuse
    /// keys are not configured, so the app runs fine without them.
    /// </summary>
    public static TracerProviderBuilder AddLangfuseExporter(
        this TracerProviderBuilder tracing, IConfiguration config)
    {
        if (!IsConfigured(config))
            return tracing;

        var publicKey = config["Langfuse:PublicKey"]!.Trim();
        var secretKey = config["Langfuse:SecretKey"]!.Trim();
        var host = (config["Langfuse:Host"] ?? "https://cloud.langfuse.com").Trim().TrimEnd('/');

        // Langfuse authenticates OTLP ingestion with HTTP Basic auth where the
        // credentials are base64("publicKey:secretKey").
        var auth = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{publicKey}:{secretKey}"));

        tracing.AddOtlpExporter(o =>
        {
            o.Endpoint = new Uri($"{host}/api/public/otel/v1/traces");
            o.Protocol = OtlpExportProtocol.HttpProtobuf;
            o.Headers = $"Authorization=Basic {auth}";
        });

        return tracing;
    }
}
