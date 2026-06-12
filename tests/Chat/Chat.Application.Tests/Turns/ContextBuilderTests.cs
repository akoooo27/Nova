using Chat.Application.Abstractions.Turns;
using Chat.Application.Tests.ModelCatalog;
using Chat.Application.Tests.ModelCatalog.LlmProviders;
using Chat.Application.Turns;
using Chat.Domain.Chats;
using Chat.Domain.Chats.Entities;
using Chat.Domain.Chats.ValueObjects;
using Chat.Domain.ModelCatalog;
using Chat.Domain.ModelCatalog.Entities;
using Chat.Domain.ModelCatalog.ValueObjects;
using Chat.Domain.Shared;

using ErrorOr;

namespace Chat.Application.Tests.Turns;

public sealed class ContextBuilderTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 12, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeLlmProviderRepository _providers = new();

    [Fact]
    public async Task BuildAsyncProducesChronologicalHistoryEndingAtTheUserMessage()
    {
        (ChatThread thread, ChatMessage assistant, _) = CreateThreadWithPendingTurn();

        ContextBuilder builder = new(_providers);

        ErrorOr<TurnContext> context = await builder.BuildAsync
        (
            thread: thread,
            assistantMessage: assistant,
            memories: RetrievedMemories.Empty,
            cancellationToken: CancellationToken.None
        );

        Assert.False(context.IsError);
        Assert.Equal(assistant.Id.Value, context.Value.TurnId);
        Assert.Equal(thread.Id.Value, context.Value.ChatId);
        Assert.Equal(thread.UserId.Value, context.Value.UserId);
        Assert.Equal("gpt-4.1", context.Value.ExternalModelId);
        Assert.Equal("You are Nova, a helpful AI assistant.", context.Value.SystemPrompt);

        TurnMessage message = Assert.Single(context.Value.Messages);
        Assert.Equal(TurnRole.User, message.Role);
        Assert.Equal("What is Redis?", message.Text);
    }

    [Fact]
    public async Task BuildAsyncWhenModelIsUnknownReturnsModelNotFound()
    {
        (ChatThread thread, ChatMessage assistant, LlmModel model) = CreateThreadWithPendingTurn();

        ContextBuilder builder = new(new FakeLlmProviderRepository());

        ErrorOr<TurnContext> context = await builder.BuildAsync
        (
            thread: thread,
            assistantMessage: assistant,
            memories: RetrievedMemories.Empty,
            cancellationToken: CancellationToken.None
        );

        Assert.True(context.IsError);
        Assert.Equal("Turn.ModelNotFound", context.FirstError.Code);
        Assert.Contains(model.Id.Value.ToString(), context.FirstError.Description, StringComparison.Ordinal);
    }

    private (ChatThread Thread, ChatMessage Assistant, LlmModel Model) CreateThreadWithPendingTurn()
    {
        LlmProvider provider = TestCatalogFactory.CreateProvider();
        LlmModel model = provider.AddModel
        (
            externalModelId: ExternalModelId.FromDatabase("gpt-4.1"),
            profile: TestCatalogFactory.CreateProfile()
        ).Value;

        _providers.AddExistingProvider(provider);

        ChatThread thread = ChatThread.Create
        (
            userId: UserId.Create("auth0|user-1").Value,
            title: ChatTitle.Create("Hello").Value,
            firstUserMessage: MessageContent.Create("What is Redis?").Value,
            createdAt: Now
        );

        ChatMessage assistant = thread.BeginAssistantMessage
        (
            parentMessageId: thread.CurrentMessageId,
            llmModelId: model.Id,
            createdAt: Now
        ).Value;

        return (thread, assistant, model);
    }
}