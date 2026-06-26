using Chat.Application.SharedChats.Commands.DeleteAll;
using Chat.Application.Tests.FavoriteModels;
using Chat.Application.Tests.ModelCatalog.LlmProviders;
using Chat.Domain.Chats.ValueObjects;
using Chat.Domain.Shared;
using Chat.Domain.SharedChats;
using Chat.Domain.SharedChats.ValueObjects;

using ErrorOr;

namespace Chat.Application.Tests.SharedChats;

public sealed class DeleteAllSharedChatsHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 21, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeSharedChatRepository _sharedChats = new();
    private readonly FakeUnitOfWork _unitOfWork = new();

    private DeleteAllSharedChatsHandler CreateHandler(string userId = SharedChatTestFactory.OwnerId) => new
    (
        userContext: new FakeUserContext(userId),
        sharedChats: _sharedChats,
        unitOfWork: _unitOfWork
    );

    private static SharedChat CreateShare(string userId) => SharedChat.Create
    (
        userId: UserId.FromDatabase(userId),
        chatId: ChatId.New(),
        currentMessageId: ChatMessageId.New(),
        title: ChatTitle.FromDatabase("Shared chat"),
        createdAt: Now
    );

    [Fact]
    public async Task HandleRemovesOnlyCurrentUsersShares()
    {
        _sharedChats.Seed(CreateShare(SharedChatTestFactory.OwnerId));
        _sharedChats.Seed(CreateShare(SharedChatTestFactory.OwnerId));
        SharedChat foreignShare = CreateShare("auth0|other-user");
        _sharedChats.Seed(foreignShare);

        ErrorOr<Deleted> result = await CreateHandler().Handle
        (
            new DeleteAllSharedChatsCommand(),
            CancellationToken.None
        );

        Assert.False(result.IsError);
        SharedChat remaining = Assert.Single(_sharedChats.Items);
        Assert.Equal(foreignShare.Id, remaining.Id);
        Assert.Equal(1, _unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task HandleSucceedsWhenOwnerHasNoShares()
    {
        ErrorOr<Deleted> result = await CreateHandler().Handle
        (
            new DeleteAllSharedChatsCommand(),
            CancellationToken.None
        );

        Assert.False(result.IsError);
        Assert.Empty(_sharedChats.Items);
        Assert.Equal(1, _unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task HandleReturnsValidationErrorForEmptyUserContext()
    {
        _sharedChats.Seed(CreateShare(SharedChatTestFactory.OwnerId));

        ErrorOr<Deleted> result = await CreateHandler(userId: string.Empty).Handle
        (
            new DeleteAllSharedChatsCommand(),
            CancellationToken.None
        );

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.Validation, result.FirstError.Type);
        Assert.Single(_sharedChats.Items);
        Assert.Equal(0, _unitOfWork.SaveChangesCallCount);
    }
}