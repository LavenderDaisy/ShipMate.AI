using System.ClientModel;
using Microsoft.Extensions.Configuration;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using OpenAI;
using ShipMate.AI.Console.Carriers;
using ShipMate.AI.Console.Plugins;

// ---------------------------------------------------------------------------
// ShipMate AI — M1: Conversational shipping-rate copilot.
// Natural language in -> LLM decides to call get_shipping_rates -> carrier
// engine returns quotes -> LLM answers in prose. Carriers are mocked for now.
//
// Supports three LLM backends via the "Provider" setting:
//   AzureOpenAI | OpenAI | Ollama (local, free, OpenAI-compatible endpoint)
// ---------------------------------------------------------------------------

var config = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: true)
    .AddUserSecrets<Program>(optional: true)
    .AddEnvironmentVariables()
    .Build();

var provider = (config["Provider"] ?? "Ollama").Trim();

// --- Build the kernel and register the selected chat-completion backend ----
var builder = Kernel.CreateBuilder();

try
{
    ConfigureChatProvider(builder, provider, config);
}
catch (ConfigurationMissingException ex)
{
    System.Console.WriteLine(ex.Message);
    return;
}

// --- Wire up the carrier rating layer and expose it as a tool --------------
// If an EasyPost API key is configured, use live multi-carrier rates; otherwise
// fall back to deterministic built-in mock carriers (no key, no cost).
var easyPostKey = config["EasyPost:ApiKey"];
ICarrierRateEngine[] rateEngines;

if (!string.IsNullOrWhiteSpace(easyPostKey))
{
    rateEngines = new ICarrierRateEngine[] { new EasyPostRateEngine(easyPostKey) };
    System.Console.WriteLine("Carrier rates: EasyPost (live)");
}
else
{
    rateEngines = new ICarrierRateEngine[]
    {
        new MockCarrierRateEngine("UPS",   baseRate: 8.50m, perPound: 0.85m),
        new MockCarrierRateEngine("FedEx", baseRate: 9.10m, perPound: 0.78m),
        new MockCarrierRateEngine("USPS",  baseRate: 7.20m, perPound: 1.05m),
    };
    System.Console.WriteLine("Carrier rates: built-in mock (set EasyPost:ApiKey for live rates)");
}

var ratingService = new RatingService(rateEngines);

// Ship/Track/Label share a store so a tracking number minted by create_shipment can be
// resolved later by track_shipment or render_label within the same session.
var shipmentStore = new ShipmentStore();
var shippingService = new ShippingService(ratingService, shipmentStore);
var labelService = new LabelService(shipmentStore,
    Path.Combine(AppContext.BaseDirectory, "labels"));

builder.Plugins.AddFromObject(new RatePlugin(ratingService), "Rating");
builder.Plugins.AddFromObject(new ShipPlugin(shippingService), "Shipping");
builder.Plugins.AddFromObject(new TrackPlugin(shippingService), "Tracking");
builder.Plugins.AddFromObject(new LabelPrintPlugin(labelService), "Labeling");

var kernel = builder.Build();
var chat = kernel.GetRequiredService<IChatCompletionService>();

// Let the model call our functions automatically as needed.
var settings = new OpenAIPromptExecutionSettings
{
    ToolCallBehavior = ToolCallBehavior.AutoInvokeKernelFunctions,
    Temperature = 0.2
};

var history = new ChatHistory("""
    You are ShipMate, a helpful shipping assistant for a multi-carrier shipping platform.
    You have tools to: look up shipping rates across carriers (get_shipping_rates),
    book a shipment (create_shipment), track a shipment (track_shipment), and generate a
    printable 4x6 ZPL shipping label (render_label).

    Orchestrate tools as needed to fully satisfy the request. For example, if the user
    says "find the cheapest overnight option, ship it and print the label", first call
    get_shipping_rates, pick the cheapest matching option, call create_shipment with that
    carrier and service, then call render_label with the returned tracking number. After
    booking, give the user the tracking number. When asked where a package is, call
    track_shipment with the tracking number.

    Always state carrier, service, price, transit time, and any tracking number clearly.
    Ask for missing details (origin, destination, weight) only if required to proceed.
    """);

System.Console.WriteLine("ShipMate AI (M2)  —  type your shipping question, or 'exit' to quit.");
System.Console.WriteLine($"LLM provider: {provider}");
System.Console.WriteLine("Try: \"Find the cheapest overnight from 30301 to 10001 for a 5 lb residential package, ship it and print the label.\"");
System.Console.WriteLine("Then: \"Where is my package?\"");
System.Console.WriteLine();

while (true)
{
    System.Console.Write("You> ");
    var input = System.Console.ReadLine();

    if (string.IsNullOrWhiteSpace(input))
    {
        continue;
    }

    if (input.Trim().Equals("exit", StringComparison.OrdinalIgnoreCase))
    {
        break;
    }

    history.AddUserMessage(input);

    try
    {
        var response = await chat.GetChatMessageContentAsync(history, settings, kernel);
        System.Console.WriteLine($"ShipMate> {response.Content}");
        System.Console.WriteLine();
        history.AddAssistantMessage(response.Content ?? string.Empty);
    }
    catch (Exception ex)
    {
        System.Console.WriteLine($"[error] {ex.Message}");
        System.Console.WriteLine();
    }
}

// ---------------------------------------------------------------------------
// Provider configuration
// ---------------------------------------------------------------------------

static void ConfigureChatProvider(IKernelBuilder builder, string provider, IConfiguration config)
{
    switch (provider.ToLowerInvariant())
    {
        case "azureopenai":
        {
            var endpoint = config["AzureOpenAI:Endpoint"];
            var deployment = config["AzureOpenAI:DeploymentName"] ?? "gpt-4o";
            var apiKey = config["AzureOpenAI:ApiKey"];

            if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(apiKey))
            {
                throw new ConfigurationMissingException("""
                    Azure OpenAI is not configured.

                    Set these via user-secrets (recommended) or appsettings.json:
                      dotnet user-secrets set "AzureOpenAI:Endpoint"       "https://<your-resource>.openai.azure.com/"
                      dotnet user-secrets set "AzureOpenAI:ApiKey"         "<your-key>"
                      dotnet user-secrets set "AzureOpenAI:DeploymentName" "gpt-4o"
                    """);
            }

            builder.AddAzureOpenAIChatCompletion(deployment, endpoint, apiKey);
            break;
        }

        case "openai":
        {
            var modelId = config["OpenAI:ModelId"] ?? "gpt-4o-mini";
            var apiKey = config["OpenAI:ApiKey"];
            var endpoint = config["OpenAI:Endpoint"];

            if (string.IsNullOrWhiteSpace(apiKey))
            {
                throw new ConfigurationMissingException("""
                    OpenAI is not configured.

                    Set these via user-secrets (recommended) or appsettings.json:
                      dotnet user-secrets set "OpenAI:ApiKey"  "<your-key>"
                      dotnet user-secrets set "OpenAI:ModelId" "gpt-4o-mini"

                    For an OpenAI-compatible provider (DeepSeek, Qwen, Zhipu, ...) also set:
                      dotnet user-secrets set "OpenAI:Endpoint" "https://api.deepseek.com/v1"
                    """);
            }

            if (string.IsNullOrWhiteSpace(endpoint))
            {
                // Official OpenAI.
                builder.AddOpenAIChatCompletion(modelId, apiKey);
            }
            else
            {
                // OpenAI-compatible endpoint (e.g. DeepSeek, Qwen/DashScope, Zhipu).
                var openAiClient = new OpenAIClient(
                    new ApiKeyCredential(apiKey),
                    new OpenAIClientOptions { Endpoint = new Uri(endpoint) });

                builder.AddOpenAIChatCompletion(modelId, openAiClient);
            }

            break;
        }

        case "ollama":
        {
            // Ollama exposes an OpenAI-compatible endpoint at /v1, so we reuse the
            // OpenAI connector and point it at the local server. No API key needed,
            // but the SDK requires a non-empty credential, so we pass a placeholder.
            var modelId = config["Ollama:ModelId"] ?? "llama3.1";
            var endpoint = config["Ollama:Endpoint"] ?? "http://localhost:11434/v1";

            var openAiClient = new OpenAIClient(
                new ApiKeyCredential("ollama"),
                new OpenAIClientOptions { Endpoint = new Uri(endpoint) });

            builder.AddOpenAIChatCompletion(modelId, openAiClient);
            break;
        }

        default:
            throw new ConfigurationMissingException(
                $"Unknown Provider '{provider}'. Use one of: AzureOpenAI, OpenAI, Ollama.");
    }
}

/// <summary>Thrown when the selected provider is missing required configuration.</summary>
internal sealed class ConfigurationMissingException : Exception
{
    public ConfigurationMissingException(string message) : base(message) { }
}
