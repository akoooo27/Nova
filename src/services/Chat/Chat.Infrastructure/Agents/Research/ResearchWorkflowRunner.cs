using System.ClientModel;
using System.Runtime.CompilerServices;

using Chat.Application.Abstractions.AgentRuns;
using Chat.Application.Abstractions.Turns;
using Chat.Application.Abstractions.WebRead;
using Chat.Application.Abstractions.WebSearch;
using Chat.Application.Turns;
using Chat.Infrastructure.Agents.Research.Executors;
using Chat.Infrastructure.Options;

using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.Options;

using OpenAI;
using OpenAI.Chat;

using AgentRunContext = Chat.Application.Abstractions.AgentRuns.AgentRunContext;

namespace Chat.Infrastructure.Agents.Research;

internal sealed class ResearchWorkflowRunner : IAgentRunRunner
{
    private readonly OpenAIClient _client;
    private readonly ResearchOptions _research;
    private readonly IWebSearchClient _searchClient;
    private readonly IUrlReader _urlReader;

    public ResearchWorkflowRunner
    (
        IOptions<AgentOptions> agentOptions,
        IOptions<ResearchOptions> researchOptions,
        IWebSearchClient searchClient,
        IUrlReader urlReader
    )
    {
        AgentOptions agent = agentOptions.Value;

        _client = new OpenAIClient
        (
            new ApiKeyCredential(agent.ApiKey),
            new OpenAIClientOptions { Endpoint = new Uri(agent.BaseUrl.ToString()) }
        );

        _research = researchOptions.Value;
        _searchClient = searchClient;
        _urlReader = urlReader;
    }

    public async IAsyncEnumerable<TurnEvent> RunAsync
    (
        AgentRunContext context,
        WorkflowCheckpoint? checkpoint,
        [EnumeratorCancellation] CancellationToken cancellationToken
    )
    {
        _ = checkpoint;

        Workflow workflow = BuildWorkflow(context);

        ResearchState input = ResearchState.Start(context.Task, FlattenHistory(context));

        StreamingRun run = await InProcessExecution.RunStreamingAsync
        (
            workflow: workflow,
            input: input,
            cancellationToken: cancellationToken
        );

        await foreach (WorkflowEvent workflowEvent in run.WatchStreamAsync(cancellationToken))
        {
            switch (workflowEvent)
            {
                case ResearchProgressEvent progress:
                    yield return new AgentActivityEvent
                    (
                        TurnId: context.TurnId,
                        Sequence: progress.Progress.Sequence,
                        Kind: progress.Progress.Kind,
                        Type: progress.Progress.Type,
                        Title: progress.Progress.Title,
                        DetailJson: progress.Progress.DetailJson
                    );
                    break;

                case ResearchUsageEvent usage:
                    yield return new UsageEvent
                    (
                        TurnId: context.TurnId,
                        Model: context.ExternalModelId,
                        InputTokens: usage.InputTokens,
                        OutputTokens: usage.OutputTokens
                    );
                    break;

                case WorkflowOutputEvent output:
                    yield return new TokenEvent
                    (
                        TurnId: context.TurnId,
                        Text: output.Data?.ToString() ?? string.Empty
                    );
                    break;
            }
        }
    }

    private Workflow BuildWorkflow(AgentRunContext context)
    {
        ChatClient? chatClient = _client.GetChatClient(context.ExternalModelId);

        AIAgent planner = chatClient.AsAIAgent(ResearchPrompts.PlannerInstructions);
        AIAgent condenser = chatClient.AsAIAgent(ResearchPrompts.CondenserInstructions);
        AIAgent critic = chatClient.AsAIAgent(ResearchPrompts.CriticInstructions);
        AIAgent writer = chatClient.AsAIAgent(ResearchPrompts.WriterInstructions);

        PlannerExecutor plannerExecutor = new(planner);
        SearchExecutor searchExecutor = new(_searchClient, _research);
        ReadExecutor readExecutor = new(_urlReader, condenser, _research);
        CriticExecutor criticExecutor = new(critic, _research);
        WriterExecutor writerExecutor = new(writer);

        return new WorkflowBuilder(plannerExecutor)
            .AddEdge(plannerExecutor, searchExecutor)
            .AddEdge(searchExecutor, readExecutor)
            .AddEdge(readExecutor, criticExecutor)
            .AddEdge<ResearchState>
                (
                    criticExecutor,
                    writerExecutor,
                    state => state!.OpenQuestions.Count == 0
                )
            .AddEdge<ResearchState>
                (
                    criticExecutor,
                    searchExecutor,
                    state => state!.OpenQuestions.Count > 0
                )
            .WithOutputFrom(writerExecutor)
            .Build();
    }

    private static List<string> FlattenHistory(AgentRunContext context) =>
        context.PriorConversation
            .Select(message => $"{(message.Role == TurnRole.User ? "user" : "assistant")}: {message.Text}")
            .ToList();
}