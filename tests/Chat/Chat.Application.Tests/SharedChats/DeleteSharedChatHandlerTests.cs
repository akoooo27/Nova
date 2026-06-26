using Chat.Application.SharedChats.Commands.Delete;
using Chat.Application.Tests.FavoriteModels;
using Chat.Application.Tests.ModelCatalog.LlmProviders;
using Chat.Domain.Chats.ValueObjects;
using Chat.Domain.Shared;
using Chat.Domain.SharedChats;
using Chat.Domain.SharedChats.ValueObjects;

using ErrorOr;

namespace Chat.Application.Tests.SharedChats;

public sealed class DeleteSharedChatHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 21, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeSharedChatRepository _sharedChats = new();
    private readonly FakeUnitOfWork _unitOfWork = new();

    private DeleteSharedChatHandler CreateHandler(string userId = SharedChatTestFactory.OwnerId) => new
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
    public async Task HandleRemovesOwnedShare()
    {
        SharedChat share = CreateShare(SharedChatTestFactory.OwnerId);
        _sharedChats.Seed(share);

        ErrorOr<Deleted> result = await CreateHandler().Handle
        (
            new DeleteSharedChatCommand(share.Id.Value),
            CancellationToken.None
        );

        Assert.False(result.IsError);
        Assert.Empty(_sharedChats.Items);
        Assert.Equal(1, _unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task HandleReturnsNotFoundForForeignShare()
    {
        SharedChat share = CreateShare("auth0|other-user");
        _sharedChats.Seed(share);

        ErrorOr<Deleted> result = await CreateHandler().Handle
        (
            new DeleteSharedChatCommand(share.Id.Value),
            CancellationToken.None
        );

        Assert.True(result.IsError);
        Assert.Equal("SharedChatId.NotFound", result.FirstError.Code);
        Assert.Single(_sharedChats.Items);
        Assert.Equal(0, _unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task HandleReturnsNotFoundForMissingShare()
    {
        ErrorOr<Deleted> result = await CreateHandler().Handle
        (
            new DeleteSharedChatCommand(Guid.NewGuid()),
            CancellationToken.None
        );

        Assert.True(result.IsError);
        Assert.Equal("SharedChatId.NotFound", result.FirstError.Code);
        Assert.Equal(0, _unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task HandleReturnsValidationErrorForEmptySharedChatId()
    {
        ErrorOr<Deleted> result = await CreateHandler().Handle
        (
            new DeleteSharedChatCommand(Guid.Empty),
            CancellationToken.None
        );

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.Validation, result.FirstError.Type);
        Assert.Equal(0, _unitOfWork.SaveChangesCallCount);
    }
}