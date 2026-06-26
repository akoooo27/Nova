using Chat.Application.SharedChats.Commands.Create;
using Chat.Application.SharedChats.Results;
using Chat.Application.Tests.FavoriteModels;
using Chat.Application.Tests.Turns;
using Chat.Domain.Chats;
using Chat.Domain.Chats.Entities;
using Chat.Domain.Chats.ValueObjects;
using Chat.Domain.ModelCatalog.ValueObjects;
using Chat.Domain.Shared;
using Chat.Domain.SharedChats;

using ErrorOr;

namespace Chat.Application.Tests.SharedChats;

public sealed class CreateSharedChatHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 21, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeChatRepository _chats = new();
    private readonly FakeSharedChatRepository _sharedChats = new();

    private CreateSharedChatHandler CreateHandler(string userId = SharedChatTestFactory.OwnerId) => new
    (
        userContext: new FakeUserContext(userId),
        chats: _chats,
        sharedChats: _sharedChats,
        dateTimeProvider: new FakeDateTimeProvider(Now)
    );

    [Fact]
    public async Task HandleCreatesNewShareForOwnedChatNode()
    {
        ChatThread source = SharedChatTestFactory.CreateShareableThread(Now.AddHours(-1));
        _chats.Seed(source);
        ChatMessageId node = source.CurrentMessageId;

        ErrorOr<SharedChatResult> result = await CreateHandler().Handle
        (
            new CreateSharedChatCommand(source.Id.Value, node.Value),
            CancellationToken.None
        );

        Assert.False(result.IsError);
        Assert.False(result.Value.AlreadyExists);
        Assert.NotEqual(Guid.Empty, result.Value.Id);
        Assert.Equal(source.Id.Value, result.Value.ChatId);
        Assert.Equal(node.Value, result.Value.CurrentMessageId);
        Assert.Equal(source.Title.Value, result.Value.Title);
        Assert.Equal(Now, result.Value.CreatedAt);
        Assert.Single(_sharedChats.Items);
    }

    [Fact]
    public async Task HandleReturnsExistingShareWithoutReplacingMetadata()
    {
        ChatThread source = SharedChatTestFactory.CreateShareableThread(Now.AddHours(-1));
        _chats.Seed(source);
        ChatMessageId node = source.CurrentMessageId;

        SharedChat existing = SharedChat.Create
        (
            userId: source.UserId,
            chatId: source.Id,
            currentMessageId: node,
            title: ChatTitle.FromDatabase("Frozen title"),
            createdAt: Now.AddHours(-2)
        );
        _sharedChats.Seed(existing);

        ErrorOr<SharedChatResult> result = await CreateHandler().Handle
        (
            new CreateSharedChatCommand(source.Id.Value, node.Value),
            CancellationToken.None
        );

        Assert.False(result.IsError);
        Assert.True(result.Value.AlreadyExists);
        Assert.Equal(existing.Id.Value, result.Value.Id);
        Assert.Equal("Frozen title", result.Value.Title);
        Assert.Equal(Now.AddHours(-2), result.Value.CreatedAt);
        Assert.Single(_sharedChats.Items);
        Assert.Equal(0, _sharedChats.TryAddCallCount);
    }

    [Fact]
    public async Task HandleCreatesDifferentLinkForDifferentNode()
    {
        ChatThread source = SharedChatTestFactory.CreateShareableThread(Now.AddHours(-1));
        _chats.Seed(source);
        ChatMessage root = source.Messages.Single(message => message.ParentMessageId is null);

        SharedChat existingForHead = SharedChat.Create
        (
            userId: source.UserId,
            chatId: source.Id,
            currentMessageId: source.CurrentMessageId,
            title: source.Title,
            createdAt: Now.AddHours(-2)
        );
        _sharedChats.Seed(existingForHead);

        ErrorOr<SharedChatResult> result = await CreateHandler().Handle
        (
            new CreateSharedChatCommand(source.Id.Value, root.Id.Value),
            CancellationToken.None
        );

        Assert.False(result.IsError);
        Assert.False(result.Value.AlreadyExists);
        Assert.Equal(root.Id.Value, result.Value.CurrentMessageId);
        Assert.Equal(2, _sharedChats.Items.Count);
    }

    [Fact]
    public async Task HandleReturnsNotFoundForForeignChat()
    {
        ChatThread source = SharedChatTestFactory.CreateShareableThread(Now.AddHours(-1), userId: "auth0|other-user");
        _chats.Seed(source);

        ErrorOr<SharedChatResult> result = await CreateHandler().Handle
        (
            new CreateSharedChatCommand(source.Id.Value, source.CurrentMessageId.Value),
            CancellationToken.None
        );

        Assert.True(result.IsError);
        Assert.Equal("Chat.NotFound", result.FirstError.Code);
        Assert.Empty(_sharedChats.Items);
    }

    [Fact]
    public async Task HandleReturnsNotFoundForUnknownChat()
    {
        ErrorOr<SharedChatResult> result = await CreateHandler().Handle
        (
            new CreateSharedChatCommand(Guid.CreateVersion7(), Guid.CreateVersion7()),
            CancellationToken.None
        );

        Assert.True(result.IsError);
        Assert.Equal("Chat.NotFound", result.FirstError.Code);
        Assert.Empty(_sharedChats.Items);
    }

    [Fact]
    public async Task HandleRejectsTemporaryChat()
    {
        ChatThread source = SharedChatTestFactory.CreateShareableThread(Now.AddHours(-1), isTemporary: true);
        _chats.Seed(source);

        ErrorOr<SharedChatResult> result = await CreateHandler().Handle
        (
            new CreateSharedChatCommand(source.Id.Value, source.CurrentMessageId.Value),
            CancellationToken.None
        );

        Assert.True(result.IsError);
        Assert.Equal("Chat.CannotShareTemporaryChat", result.FirstError.Code);
        Assert.Empty(_sharedChats.Items);
    }

    [Fact]
    public async Task HandleRejectsGeneratingMessage()
    {
        ChatThread source = ChatThread.Create
        (
            userId: UserId.FromDatabase(SharedChatTestFactory.OwnerId),
            title: ChatTitle.FromDatabase("Source chat"),
            firstUserMessage: MessageContent.FromDatabase("Original prompt"),
            createdAt: Now.AddHours(-1),
            isTemporary: false
        );
        ChatMessage generating = source.BeginAssistantMessage
        (
            parentMessageId: source.CurrentMessageId,
            llmModelId: LlmModelId.New(),
            createdAt: Now.AddMinutes(-59)
        ).Value;
        _chats.Seed(source);

        ErrorOr<SharedChatResult> result = await CreateHandler().Handle
        (
            new CreateSharedChatCommand(source.Id.Value, generating.Id.Value),
            CancellationToken.None
        );

        Assert.True(result.IsError);
        Assert.Equal("Chat.CannotShareGeneratingMessage", result.FirstError.Code);
        Assert.Empty(_sharedChats.Items);
    }

    [Fact]
    public async Task HandleReturnsValidationErrorForEmptyUserContext()
    {
        ChatThread source = SharedChatTestFactory.CreateShareableThread(Now.AddHours(-1));
        _chats.Seed(source);

        ErrorOr<SharedChatResult> result = await CreateHandler(userId: string.Empty).Handle
        (
            new CreateSharedChatCommand(source.Id.Value, source.CurrentMessageId.Value),
            CancellationToken.None
        );

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.Validation, result.FirstError.Type);
        Assert.Empty(_sharedChats.Items);
    }
}