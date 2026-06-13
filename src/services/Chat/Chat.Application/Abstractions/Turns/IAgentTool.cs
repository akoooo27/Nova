namespace Chat.Application.Abstractions.Turns;

/// <summary>
/// Provider-agnostic agent tool seam (spec Rule 1/5). Carries no Agent Framework types;
/// <c>AgentFrameworkRunner</c> is the only code that adapts this to a framework AIFunction.
/// </summary>
public interface IAgentTool
{
    /// <summary>Stable identifier (e.g. "web_search"). Used for per-turn toggling and telemetry.</summary>
    string Name { get; }

    /// <summary>
    /// The delegate the model invokes. Its parameters and [Description] attributes drive the
    /// generated JSON schema. Returns the string the model reads back as the tool result.
    /// </summary>
    Delegate CreateInvocation();
}