using Microsoft.Extensions.Configuration;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using OpenTelemetry.Trace;
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

// --- OpenTelemetry tracing -------------------------------------------------
// Collects spans from ShipMateTelemetry.ActivitySource and the ASP.NET Core HTTP
// pipeline, then exports them to the console (for demo visibility). In production
// you'd swap the console exporter for an OTLP exporter pointing at Jaeger/Tempo/etc.
builder.Services.AddOpenTelemetry()
    .WithTracing(tp => tp
        .AddSource(ShipMateTelemetry.ActivitySource.Name)
        .AddAspNetCoreInstrumentation()
        .AddConsoleExporter());

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

    // Root span for the entire chat request. Child spans (tool calls) will nest under it.
    using var span = ShipMateTelemetry.StartSpan("chat.request");
    span?.SetTag("session.id", sessionId);
    span?.SetTag("chat.message_length", req.Message.Length);

    var history = ChatSessionStore.Histories.GetOrAdd(sessionId, _ =>
        new ChatHistory(ShipMateKernelFactory.SystemPrompt));

    history.AddUserMessage(req.Message);

    var chat = kernel.GetRequiredService<IChatCompletionService>();
    try
    {
        var response = await chat.GetChatMessageContentAsync(history, settings, kernel);
        var reply = response.Content ?? "(no response)";
        history.AddAssistantMessage(reply);
        span?.SetTag("chat.reply_length", reply.Length);
        span?.SetStatus(System.Diagnostics.ActivityStatusCode.Ok);
        return Results.Ok(new ChatResponse(reply));
    }
    catch (Exception ex)
    {
        span?.SetStatus(System.Diagnostics.ActivityStatusCode.Error, ex.Message);
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
