using System.ComponentModel;

using Chat.Application.Abstractions.Gmail;
using Chat.Application.Abstractions.Turns;

namespace Chat.Application.Turns.Tools.Gmail;

public sealed class GmailWhoAmITool(IGmailToolClient gmail) : IAgentTool
{
    public string Name => AgentToolNames.GmailWhoAmI;

    public Delegate CreateInvocation(TurnToolContext context) =>
        new Invocation(gmail, context).WhoAmIAsync;

    private sealed class Invocation(IGmailToolClient gmail, TurnToolContext context)
    {
        [Description("Get the connected Gmail account profile for the current user.")]
        public async Task<GmailToolResult> WhoAmIAsync(CancellationToken cancellationToken = default) =>
            await gmail.WhoAmIAsync
            (
                userId: context.UserId,
                cancellationToken: cancellationToken
            );
    }
}