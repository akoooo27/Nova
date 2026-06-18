using System.ClientModel;
using System.Runtime.CompilerServices;

using Chat.Application.Abstractions.Turns;
using Chat.Application.Turns;
using Chat.Application.Turns.Tools;
using Chat.Infrastructure.Options;

using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

using OpenAI;
using OpenAI.Chat;

using AIChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace Chat.Infrastructure.Agents;

internal sealed class AgentFrameworkRunner : IAgentRunner
{
    private readonly OpenAIClient _client;
    private readonly IReadOnlyList<IAgentTool> _tools;

    public AgentFrameworkRunner(IOptions<AgentOptions> options, IEnumerable<IAgentTool> tools)
    {
        AgentOptions value = options.Value;

        _client = new OpenAIClient
        (
            new ApiKeyCredential(value.ApiKey),
            new OpenAIClientOptions { Endpoint = new Uri(value.BaseUrl.ToString()) }
        );

        _tools = tools.ToList();
    }

    public async IAsyncEnumerable<TurnEvent> RunAsync
    (
        TurnContext context,
        [EnumeratorCancellation] CancellationToken cancellationToken
    )
    {
        // The tools are ALWAYS exposed. Whether the model is free to
        // decide, or required to call it, is controlled below via ToolMode.
        IList<AITool> tools = _tools
            .Select(tool => (AITool)AIFunctionFactory.Create
            (
                method: tool.CreateInvocation(),
                new AIFunctionFactoryOptions { Name = tool.Name }
            ))
            .ToList();

        AIAgent agent = _client
            .GetChatClient(context.ExternalModelId)
            .AsAIAgent(instructions: context.SystemPrompt, tools: tools);

        List<AIChatMessage> messages = context.Messages
            .Select(message => new AIChatMessage
            (
                message.Role == TurnRole.User
                    ? ChatRole.User
                    : ChatRole.Assistant,
                message.Text
            ))
            .ToList();

        ChatOptions chatOptions = new()
        {
            ToolMode = SelectToolMode(context.GenerationOptions)
        };

        ChatClientAgentRunOptions runOptions = new()
        {
            ChatOptions = chatOptions,
        };

        await foreach (AgentResponseUpdate update in agent.RunStreamingAsync(messages: messages, options: runOptions, cancellationToken: cancellationToken))
        {
            foreach (TurnEvent turnEvent in TurnEventMapper.Map(turnId: context.TurnId, modelId: context.ExternalModelId, update: update)
            )
            {
                yield return turnEvent;
            }
        }
    }

    private static ChatToolMode SelectToolMode(TurnGenerationOptions options)
    {
        return options.ForceUseSearch
            ? ChatToolMode.RequireSpecific(AgentToolNames.WebSearch)
            : ChatToolMode.Auto;
    }
}