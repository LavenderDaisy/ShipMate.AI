using Microsoft.Extensions.Configuration;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using OpenTelemetry.Resources;
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

// Enable Semantic Kernel's GenAI OpenTelemetry diagnostics BEFORE the kernel is built,
// so LLM calls, token usage, prompts, and tool invocations are emitted as spans that
// Langfuse can ingest. IncludeSensitive controls whether prompt/completion text is
// captured (defaults to true so the demo shows the full conversation in Langfuse).
var includeSensitive = builder.Configuration.GetValue("Langfuse:IncludeSensitive", true);
LangfuseTracing.EnableSemanticKernelDiagnostics(includeSensitive);

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
// Collects spans from ShipMateTelemetry.ActivitySource, Semantic Kernel's GenAI
// diagnostics, and the ASP.NET Core HTTP pipeline. Exports to the console (for demo
// visibility) and, when configured, to Langfuse over OTLP for LLM-specific
// observability (token cost, prompts, tool-call traces).
builder.Services.AddOpenTelemetry()
    .WithTracing(tp =>
    {
        tp.SetResourceBuilder(
                ResourceBuilder.CreateDefault().AddService("ShipMate.AI"))
            .AddSource(ShipMateTelemetry.ActivitySource.Name)
            .AddSource("Microsoft.SemanticKernel*")
            .AddAspNetCoreInstrumentation()
            .AddConsoleExporter()
            .AddLangfuseExporter(builder.Configuration);
    });

if (LangfuseTracing.IsConfigured(builder.Configuration))
{
    Console.WriteLine(
        $"Langfuse tracing enabled -> {builder.Configuration["Langfuse:Host"] ?? "https://cloud.langfuse.com"}");
}
else
{
    Console.WriteLine("Langfuse tracing disabled (set Langfuse:PublicKey / Langfuse:SecretKey to enable).");
}

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

    // Begin capturing the ReAct tool-call chain for this request (think → act → observe).
    var steps = ReActStepCollector.BeginScope();

    var chat = kernel.GetRequiredService<IChatCompletionService>();
    try
    {
        var response = await chat.GetChatMessageContentAsync(history, settings, kernel);
        var reply = response.Content ?? "(no response)";
        history.AddAssistantMessage(reply);
        span?.SetTag("chat.reply_length", reply.Length);
        span?.SetTag("react.step_count", steps.Count);
        span?.SetStatus(System.Diagnostics.ActivityStatusCode.Ok);
        return Results.Ok(new ChatResponse(reply, steps.ToArray()));
    }
    catch (Exception ex)
    {
        span?.SetStatus(System.Diagnostics.ActivityStatusCode.Error, ex.Message);
        return Results.Ok(new ChatResponse($"[error] {ex.Message}", steps.ToArray()));
    }
    finally
    {
        ReActStepCollector.EndScope();
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
