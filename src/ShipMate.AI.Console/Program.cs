using Microsoft.Extensions.Configuration;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using ShipMate.AI.Console;
using ShipMate.AI.Console.Carriers;

// ---------------------------------------------------------------------------
// ShipMate AI — interactive console host.
// Natural language in -> LLM decides which tools to call (rate, ship, track,
// label, print, carrier-rules RAG) -> LLM answers in prose.
//
// All kernel/carrier/shipping/label/printing/RAG wiring lives in
// ShipMateKernelFactory so the console and the web API share one composition root.
// ---------------------------------------------------------------------------

var config = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: true)
    .AddUserSecrets<Program>(optional: true)
    .AddEnvironmentVariables()
    .Build();

// --dump / --list: print all stored shipments and exit. Useful for checking MongoDB
// contents without installing Compass. Usage: dotnet run -- --dump
if (args is { Length: > 0 } && args[0] is "--dump" or "--list")
{
    DumpShipments(ShipMateKernelFactory.CreateShipmentStore(config));
    return;
}

// Build the shared kernel (provider, carrier engines, shipment store, printer, RAG).
ShipMateKernel shipMate;
try
{
    shipMate = ShipMateKernelFactory.Build(config);
}
catch (ShipMateConfigurationException ex)
{
    System.Console.WriteLine(ex.Message);
    return;
}

var kernel = shipMate.Kernel;
var chat = kernel.GetRequiredService<IChatCompletionService>();
var history = new ChatHistory(ShipMateKernelFactory.SystemPrompt);

System.Console.WriteLine("ShipMate AI  —  type your shipping question, or 'exit' to quit.");
System.Console.WriteLine($"LLM provider: {shipMate.ProviderName}");
System.Console.WriteLine("Try: \"Find the cheapest overnight from 30301 to 10001 for a 5 lb residential package, ship it and print the label.\"");
System.Console.WriteLine("Then: \"Where is my package?\"");
System.Console.WriteLine();

while (true)
{
    System.Console.Write("You> ");
    var input = System.Console.ReadLine();

    // Null means stdin reached end-of-input (e.g. piped/redirected input); exit cleanly.
    if (input is null)
    {
        break;
    }

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
        var response = await chat.GetChatMessageContentAsync(history, shipMate.Settings, kernel);
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

static void DumpShipments(IShipmentStore store)
{
    var all = store.All;
    if (all.Count == 0)
    {
        System.Console.WriteLine("No shipments in store.");
        return;
    }

    System.Console.WriteLine($"\n=== {all.Count} shipment(s) in store ===\n");
    foreach (var s in all)
    {
        System.Console.WriteLine($"  Tracking#: {s.TrackingNumber}");
        System.Console.WriteLine($"  Carrier:   {s.Carrier} ({s.ServiceLevel})");
        System.Console.WriteLine($"  Charge:    {s.TotalCharge:C} {s.Currency}");
        System.Console.WriteLine($"  Est. delivery: {s.EstimatedDelivery:yyyy-MM-dd}");
        System.Console.WriteLine();
    }
}
