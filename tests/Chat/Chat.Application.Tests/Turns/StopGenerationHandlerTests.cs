using Chat.Application.Chats.Commands.StopGeneration;
using Chat.Application.Tests.FavoriteModels;
using Chat.Domain.Chats;
using Chat.Domain.Chats.Entities;
using Chat.Domain.Chats.ValueObjects;
using Chat.Domain.ModelCatalog.ValueObjects;
using Chat.Domain.Shared;

using ErrorOr;

namespace Chat.Application.Tests.Turns;

public sealed class StopGenerationHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 24, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeChatRepository _chats = new();
    private readonly FakeTurnStopSignal _stopSignal = new();

    [Fact]
    public async Task RequestsStopForGeneratingAssistantMessage()
    {
        (ChatThread thread, ChatMessage assistant) = SeedGeneratingAssistant();

        ErrorOr<Success> result = await CreateHandler()
            .Handle(new StopGenerationCommand(thread.Id.Value, assistant.Id.Value), CancellationToken.None);

        Assert.False(result.IsError);
        Assert.True(await _stopSignal.IsStopRequestedAsync(assistant.Id.Value, CancellationToken.None));
    }

    [Fact]
    public async Task ReturnsChatNotFoundWhenChatUnknown()
    {
        (_, ChatMessage assistant) = SeedGeneratingAssistant();

        ErrorOr<Success> result = await CreateHandler()
            .Handle(new StopGenerationCommand(Guid.CreateVersion7(), assistant.Id.Value), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("Chat.NotFound", result.FirstError.Code);
    }

    [Fact]
    public async Task ReturnsMessageNotFoundWhenMessageUnknown()
    {
        (ChatThread thread, _) = SeedGeneratingAssistant();

        ErrorOr<Success> result = await CreateHandler()
            .Handle(new StopGenerationCommand(thread.Id.Value, Guid.CreateVersion7()), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("Chat.MessageNotFound", result.FirstError.Code);
    }

    [Fact]
    public async Task ReturnsErrorWhenTargetIsUserMessage()
    {
        (ChatThread thread, _) = SeedGeneratingAssistant();
        ChatMessage userMessage = Assert.Single(thread.Messages, message => message.Role == MessageRole.User);

        ErrorOr<Success> result = await CreateHandler()
            .Handle(new StopGenerationCommand(thread.Id.Value, userMessage.Id.Value), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("Chat.StopTargetMustBeAssistant", result.FirstError.Code);
    }

    [Fact]
    public async Task ReturnsErrorWhenAssistantAlreadyTerminal()
    {
        (ChatThread thread, ChatMessage assistant) = SeedGeneratingAssistant();
        thread.CompleteAssistantMessage(assistant.Id, MessageContent.Create("Done").Value, Now);

        ErrorOr<Success> result = await CreateHandler()
            .Handle(new StopGenerationCommand(thread.Id.Value, assistant.Id.Value), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("Chat.CannotStopNonGenerating", result.FirstError.Code);
    }

    private (ChatThread Thread, ChatMessage Assistant) SeedGeneratingAssistant()
    {
        ChatThread thread = ChatThread.Create
        (
            userId: UserId.Create("auth0|user-1").Value,
            title: ChatTitle.Create("Hello").Value,
            firstUserMessage: MessageContent.Create("Hello").Value,
            createdAt: Now
        );

        ChatMessage assistant = thread.BeginAssistantMessage
        (
            parentMessageId: thread.CurrentMessageId,
            llmModelId: LlmModelId.New(),
            createdAt: Now
        ).Value;

        _chats.Seed(thread);

        return (thread, assistant);
    }

    private StopGenerationHandler CreateHandler() => new
    (
        userContext: new FakeUserContext("auth0|user-1"),
        chats: _chats,
        stopSignal: _stopSignal
    );
}