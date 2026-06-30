using Microsoft.Extensions.Configuration;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using ShipMate.AI.Api;
using ShipMate.AI.Console;

// ---------------------------------------------------------------------------
// ShipMate AI — Web API + chat UI.
// Reuses the same Semantic Kernel, carrier, shipping, label, and printing layers
// as the console host via ShipMateKernelFactory.
// ---------------------------------------------------------------------------

var builder = WebApplication.CreateBuilder(args);

// Merge user-secrets + environment variables into configuration (the web SDK already
// adds appsettings.json; we add the console project's user-secrets ID too so the API
// picks up the same keys the console uses).
builder.Configuration
    .AddUserSecrets<Program>(optional: true)
    .AddEnvironmentVariables();

// Build the kernel once and register it as a singleton. Per-request chat history is
// kept in a concurrent dictionary keyed by session id.
ShipMateKernel shipMate;
try
{
    shipMate = ShipMateKernelFactory.Build(builder.Configuration);
}
catch (ShipMateConfigurationException ex)
{
    Console.Error.WriteLine($"Configuration error: {ex.Message}");
    throw;
}

builder.Services.AddSingleton(shipMate.Kernel);
builder.Services.AddSingleton(shipMate.Settings);
builder.Services.AddCors(o => o.AddDefaultPolicy(b => b.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();
app.UseCors();

// --- Chat endpoint ---------------------------------------------------------
// POST /api/chat  { "sessionId": "...", "message": "..." }
// Returns { "reply": "..." }
app.MapPost("/api/chat", async (ChatRequest req, Kernel kernel, OpenAIPromptExecutionSettings settings) =>
{
    if (string.IsNullOrWhiteSpace(req.Message))
        return Results.BadRequest(new { error = "message is required" });

    var sessionId = string.IsNullOrWhiteSpace(req.SessionId) ? "default" : req.SessionId;
    var history = ChatSessionStore.Histories.GetOrAdd(sessionId, _ =>
        new ChatHistory(ShipMateKernelFactory.SystemPrompt));

    history.AddUserMessage(req.Message);

    var chat = kernel.GetRequiredService<IChatCompletionService>();
    try
    {
        var response = await chat.GetChatMessageContentAsync(history, settings, kernel);
        var reply = response.Content ?? "(no response)";
        history.AddAssistantMessage(reply);
        return Results.Ok(new ChatResponse(reply));
    }
    catch (Exception ex)
    {
        return Results.Ok(new ChatResponse($"[error] {ex.Message}"));
    }
});

// Clear a session's history.
app.MapDelete("/api/chat/{sessionId}", (string sessionId) =>
{
    ChatSessionStore.Histories.TryRemove(sessionId, out _);
    return Results.Ok();
});

// Serve the chat UI.
app.MapGet("/", () => Results.Content(ChatPage.Html, "text/html", System.Text.Encoding.UTF8));

app.Run();

app.Run();
