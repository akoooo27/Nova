using Chat.Application.Abstractions.AgentRuns;
using Chat.Application.Abstractions.Turns;
using Chat.Application.AgentRuns;
using Chat.Application.Tests.ModelCatalog;
using Chat.Application.Tests.ModelCatalog.LlmProviders;
using Chat.Domain.AgentRuns;
using Chat.Domain.AgentRuns.ValueObjects;
using Chat.Domain.Chats;
using Chat.Domain.Chats.Entities;
using Chat.Domain.Chats.ValueObjects;
using Chat.Domain.ModelCatalog;
using Chat.Domain.ModelCatalog.Entities;
using Chat.Domain.Shared;

using ErrorOr;

namespace Chat.Application.Tests.AgentRuns;

public sealed class AgentRunContextBuilderTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 12, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeLlmProviderRepository _providers = new();

    private static (ChatThread Thread, ChatMessage Assistant, AgentRun Run) SeedRunInConversation(LlmModel model)
    {
        ChatThread thread = ChatThread.Create
        (
            userId: UserId.Create("auth0|user-1").Value,
            title: ChatTitle.Create("Hello").Value,
            firstUserMessage: MessageContent.Create("What is Redis?").Value,
            createdAt: Now
        );

        ChatMessage firstAssistant = thread.BeginAssistantMessage(thread.CurrentMessageId, model.Id, Now).Value;
        thread.CompleteAssistantMessage(firstAssistant.Id, MessageContent.Create("An in-memory store.").Value, Now);

        ChatMessage taskMessage = thread.AddUserMessage
        (
            parentMessageId: firstAssistant.Id,
            content: MessageContent.Create("Research Redis Streams adoption").Value,
            createdAt: Now
        ).Value;

        ChatMessage assistant = thread.BeginAssistantMessage
        (
            parentMessageId: taskMessage.Id,
            llmModelId: model.Id,
            createdAt: Now,
            kind: MessageKind.AgentRun
        ).Value;

        AgentRun run = AgentRun.Start
        (
            kind: AgentRunKind.Research,
            chatId: thread.Id,
            assistantMessageId: assistant.Id,
            userId: thread.UserId,
            task: AgentTask.Create("Research Redis Streams adoption").Value,
            llmModelId: model.Id,
            startedAt: Now
        );

        return (thread, assistant, run);
    }

    private LlmModel SeedModel()
    {
        LlmProvider provider = TestCatalogFactory.CreateProvider();
        LlmModel model = provider.AddModel
        (
            externalModelId: TestCatalogFactory.CreateExternalModelId("gpt-4.1"),
            profile: TestCatalogFactory.CreateProfile()
        ).Value;
        _providers.AddExistingProvider(provider);
        return model;
    }

    [Fact]
    public async Task BuildContextAsyncProducesTaskModelAndPriorConversationExcludingTheTaskMessage()
    {
        LlmModel model = SeedModel();
        (ChatThread thread, ChatMessage assistant, AgentRun run) = SeedRunInConversation(model);

        AgentRunContextBuilder builder = new(_providers);

        ErrorOr<AgentRunContext> context =
            await builder.BuildContextAsync(thread, assistant, run, CancellationToken.None);

        Assert.False(context.IsError);
        Assert.Equal(assistant.Id.Value, context.Value.TurnId);
        Assert.Equal(run.Id.Value, context.Value.RunId);
        Assert.Equal(thread.Id.Value, context.Value.ChatId);
        Assert.Equal(thread.UserId.Value, context.Value.UserId);
        Assert.Equal(AgentRunKind.Research, context.Value.Kind);
        Assert.Equal("Research Redis Streams adoption", context.Value.Task);
        Assert.Equal("gpt-4.1", context.Value.ExternalModelId);

        // Prior conversation is the completed exchange ABOVE the task user message, oldest first;
        // the task message itself is excluded (it travels as run.Task).
        Assert.Equal(2, context.Value.PriorConversation.Count);
        Assert.Equal(TurnRole.User, context.Value.PriorConversation[0].Role);
        Assert.Equal("What is Redis?", context.Value.PriorConversation[0].Text);
        Assert.Equal(TurnRole.Assistant, context.Value.PriorConversation[1].Role);
        Assert.Equal("An in-memory store.", context.Value.PriorConversation[1].Text);
    }

    [Fact]
    public async Task BuildContextAsyncWhenModelUnknownReturnsModelNotFound()
    {
        LlmModel model = SeedModel();
        (ChatThread thread, ChatMessage assistant, AgentRun run) = SeedRunInConversation(model);

        // Fresh repository with nothing seeded: the run's model cannot be resolved.
        AgentRunContextBuilder builder = new(new FakeLlmProviderRepository());

        ErrorOr<AgentRunContext> context =
            await builder.BuildContextAsync(thread, assistant, run, CancellationToken.None);

        Assert.True(context.IsError);
        Assert.Equal("AgentRun.ModelNotFound", context.FirstError.Code);
    }
}