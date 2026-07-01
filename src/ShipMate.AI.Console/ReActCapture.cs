using System.Diagnostics;
using Microsoft.SemanticKernel;

namespace ShipMate.AI.Console;

/// <summary>
/// One step in a ReAct (Reasoning + Acting) trace: a single tool the LLM decided to invoke,
/// with the arguments it chose and the result it observed. A sequence of these reconstructs
/// the agent's think → act → observe loop for display.
/// </summary>
public sealed record ReActStep
{
    public required string Tool { get; init; }
    public required string Arguments { get; init; }
    public required string Result { get; init; }
    public required long DurationMs { get; init; }
}

/// <summary>
/// Collects ReAct steps for the current logical request using an <see cref="AsyncLocal{T}"/>,
/// so a single shared (singleton) kernel can still record steps isolated per request/async
/// flow. Call <see cref="BeginScope"/> at the start of a request, run the kernel, then read
/// the returned list.
/// </summary>
public static class ReActStepCollector
{
    private static readonly AsyncLocal<List<ReActStep>?> Current = new();

    /// <summary>Starts a fresh collection scope for the current async flow.</summary>
    public static IReadOnlyList<ReActStep> BeginScope()
    {
        var list = new List<ReActStep>();
        Current.Value = list;
        return list;
    }

    /// <summary>Ends the current scope so later work doesn't append to a stale list.</summary>
    public static void EndScope() => Current.Value = null;

    /// <summary>Records a step if a scope is active; no-op otherwise.</summary>
    public static void Record(ReActStep step) => Current.Value?.Add(step);
}

/// <summary>
/// Semantic Kernel filter that intercepts every tool (function) invocation and records it as
/// a <see cref="ReActStep"/>. This is how the agent's tool-call chain is captured for the UI
/// without changing any plugin code — the filter sits in the kernel's invocation pipeline.
/// </summary>
public sealed class ReActCaptureFilter : IFunctionInvocationFilter
{
    public async Task OnFunctionInvocationAsync(
        FunctionInvocationContext context, Func<FunctionInvocationContext, Task> next)
    {
        var sw = Stopwatch.StartNew();
        await next(context);
        sw.Stop();

        var args = string.Join(", ",
            context.Arguments.Select(a => $"{a.Key}={Truncate(a.Value?.ToString(), 60)}"));

        ReActStepCollector.Record(new ReActStep
        {
            Tool = context.Function.Name,
            Arguments = args,
            Result = Truncate(context.Result.GetValue<object?>()?.ToString(), 200),
            DurationMs = sw.ElapsedMilliseconds
        });
    }

    private static string Truncate(string? value, int max)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        var oneLine = value.ReplaceLineEndings(" ");
        return oneLine.Length <= max ? oneLine : oneLine[..max] + "…";
    }
}
