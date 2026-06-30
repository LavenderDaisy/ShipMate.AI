using System.ClientModel;
using Microsoft.Extensions.Configuration;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using OpenAI;
using ShipMate.AI.Console.Carriers;
using ShipMate.AI.Console.Plugins;
using ShipMate.AI.Console.Printing;

namespace ShipMate.AI.Console;

/// <summary>
/// Assembles the Semantic Kernel with all carrier/shipping/label/printing services and
/// registers the LLM-callable plugins. Shared by the console host and the web API so the
/// wiring logic lives in one place. Returns a ready-to-use kernel plus the chat settings
/// and system prompt.
/// </summary>
public static class ShipMateKernelFactory
{
    public const string SystemPrompt = """
        You are ShipMate, a helpful shipping assistant for a multi-carrier shipping platform.
        You have tools to: look up shipping rates across carriers (get_shipping_rates),
        book a shipment (create_shipment), track a shipment (track_shipment), generate a
        printable 4x6 ZPL shipping label (render_label), send a booked shipment's label to the
        label printer (print_label), and buy and print a REAL carrier label (buy_and_print_carrier_label).

        Orchestrate tools as needed to fully satisfy the request. For example, if the user
        says "find the cheapest overnight option, ship it and print the label", first call
        get_shipping_rates, pick the cheapest matching option, call create_shipment with that
        carrier and service, then call print_label with the returned tracking number. After
        booking, give the user the tracking number. When asked where a package is, call
        track_shipment with the tracking number. Use buy_and_print_carrier_label only when the
        user explicitly wants a real/actual carrier label rather than the demo label.

        Always state carrier, service, price, transit time, and any tracking number clearly.
        Ask for missing details (origin, destination, weight) only if required to proceed.
        """;

    /// <summary>
    /// Builds the kernel, services, and execution settings from configuration. Throws
    /// <see cref="ShipMateConfigurationException"/> if the LLM provider is not configured.
    /// </summary>
    public static ShipMateKernel Build(IConfiguration config)
    {
        var provider = (config["Provider"] ?? "Ollama").Trim();

        var builder = Kernel.CreateBuilder();
        ConfigureChatProvider(builder, provider, config);

        // --- Carrier rating layer ---
        var easyPostKey = config["EasyPost:ApiKey"];
        ICarrierRateEngine[] rateEngines;

        if (!string.IsNullOrWhiteSpace(easyPostKey))
        {
            rateEngines = new ICarrierRateEngine[] { new EasyPostRateEngine(easyPostKey) };
        }
        else
        {
            rateEngines = new ICarrierRateEngine[]
            {
                new MockCarrierRateEngine("UPS",   baseRate: 8.50m, perPound: 0.85m),
                new MockCarrierRateEngine("FedEx", baseRate: 9.10m, perPound: 0.78m),
                new MockCarrierRateEngine("USPS",  baseRate: 7.20m, perPound: 1.05m),
            };
        }

        var ratingService = new RatingService(rateEngines);
        var shipmentStore = CreateShipmentStore(config);
        var shippingService = new ShippingService(ratingService, shipmentStore);
        var labelsDir = Path.Combine(AppContext.BaseDirectory, "labels");
        var labelService = new LabelService(shipmentStore, labelsDir);
        var printer = CreatePrinter(config);
        EasyPostLabelService? easyPostLabel = !string.IsNullOrWhiteSpace(easyPostKey)
            ? new EasyPostLabelService(easyPostKey)
            : null;

        builder.Plugins.AddFromObject(new RatePlugin(ratingService), "Rating");
        builder.Plugins.AddFromObject(new ShipPlugin(shippingService), "Shipping");
        builder.Plugins.AddFromObject(new TrackPlugin(shippingService), "Tracking");
        builder.Plugins.AddFromObject(new LabelPrintPlugin(labelService), "Labeling");
        builder.Plugins.AddFromObject(
            new PrintLabelPlugin(labelService, printer, labelsDir, easyPostLabel), "Printing");

        var kernel = builder.Build();

        var settings = new OpenAIPromptExecutionSettings
        {
            ToolCallBehavior = ToolCallBehavior.AutoInvokeKernelFunctions,
            Temperature = 0.2
        };

        return new ShipMateKernel(kernel, settings, provider);
    }

    // --- LLM provider selection (same logic as the console host) ---

    private static void ConfigureChatProvider(IKernelBuilder builder, string provider, IConfiguration config)
    {
        switch (provider.ToLowerInvariant())
        {
            case "azureopenai":
            {
                var endpoint = config["AzureOpenAI:Endpoint"];
                var deployment = config["AzureOpenAI:DeploymentName"] ?? "gpt-4o";
                var apiKey = config["AzureOpenAI:ApiKey"];
                if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(apiKey))
                    throw new ShipMateConfigurationException("Azure OpenAI is not configured. Set AzureOpenAI:Endpoint and AzureOpenAI:ApiKey.");
                builder.AddAzureOpenAIChatCompletion(deployment, endpoint, apiKey);
                break;
            }
            case "openai":
            {
                var modelId = config["OpenAI:ModelId"] ?? "gpt-4o-mini";
                var apiKey = config["OpenAI:ApiKey"];
                var endpoint = config["OpenAI:Endpoint"];
                if (string.IsNullOrWhiteSpace(apiKey))
                    throw new ShipMateConfigurationException("OpenAI is not configured. Set OpenAI:ApiKey.");
                if (string.IsNullOrWhiteSpace(endpoint))
                {
                    builder.AddOpenAIChatCompletion(modelId, apiKey);
                }
                else
                {
                    var openAiClient = new OpenAIClient(
                        new ApiKeyCredential(apiKey),
                        new OpenAIClientOptions { Endpoint = new Uri(endpoint) });
                    builder.AddOpenAIChatCompletion(modelId, openAiClient);
                }
                break;
            }
            case "ollama":
            {
                var modelId = config["Ollama:ModelId"] ?? "llama3.1";
                var endpoint = config["Ollama:Endpoint"] ?? "http://localhost:11434/v1";
                var openAiClient = new OpenAIClient(
                    new ApiKeyCredential("ollama"),
                    new OpenAIClientOptions { Endpoint = new Uri(endpoint) });
                builder.AddOpenAIChatCompletion(modelId, openAiClient);
                break;
            }
            default:
                throw new ShipMateConfigurationException($"Unknown Provider '{provider}'. Use AzureOpenAI, OpenAI, or Ollama.");
        }
    }

    // --- Store / printer selection ---

    private static IShipmentStore CreateShipmentStore(IConfiguration config)
    {
        var connectionString = config["Mongo:ConnectionString"];
        if (string.IsNullOrWhiteSpace(connectionString))
            return new InMemoryShipmentStore();

        var database = config["Mongo:Database"] ?? "shipmate";
        try
        {
            return new MongoShipmentStore(connectionString, database);
        }
        catch
        {
            return new InMemoryShipmentStore();
        }
    }

    private static IZplPrinter CreatePrinter(IConfiguration config)
    {
        var type = (config["Printer:Type"] ?? "None").Trim();
        return type.ToLowerInvariant() switch
        {
            "windows" when !string.IsNullOrWhiteSpace(config["Printer:Name"])
                => new WindowsRawZplPrinter(config["Printer:Name"]!),
            "tcp" when !string.IsNullOrWhiteSpace(config["Printer:Host"])
                => new TcpZplPrinter(config["Printer:Host"]!, int.TryParse(config["Printer:Port"], out var p) ? p : 9100),
            _ => new NullZplPrinter()
        };
    }
}

/// <summary>Bundle of the built kernel and execution settings.</summary>
public sealed record ShipMateKernel(Kernel Kernel, OpenAIPromptExecutionSettings Settings, string ProviderName);

/// <summary>Thrown when the LLM provider or required config is missing.</summary>
public sealed class ShipMateConfigurationException : Exception
{
    public ShipMateConfigurationException(string message) : base(message) { }
}
