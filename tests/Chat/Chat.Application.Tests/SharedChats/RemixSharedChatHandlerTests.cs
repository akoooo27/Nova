using Chat.Application.SharedChats.Commands.Remix;
using Chat.Application.SharedChats.Results;
using Chat.Application.Tests.FavoriteModels;
using Chat.Application.Tests.ModelCatalog.LlmProviders;
using Chat.Application.Tests.Turns;
using Chat.Domain.Chats;
using Chat.Domain.Chats.Entities;
using Chat.Domain.Chats.ValueObjects;
using Chat.Domain.Shared;
using Chat.Domain.SharedChats;

using ErrorOr;

namespace Chat.Application.Tests.SharedChats;

public sealed class RemixSharedChatHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 21, 12, 0, 0, TimeSpan.Zero);

    private const string RemixerUserId = "auth0|remixer";

    private readonly FakeChatRepository _chats = new();
    private readonly FakeSharedChatRepository _sharedChats = new();
    private readonly FakeUnitOfWork _unitOfWork = new();

    private RemixSharedChatHandler CreateHandler(string userId = RemixerUserId) => new
    (
        userContext: new FakeUserContext(userId),
        sharedChats: _sharedChats,
        chats: _chats,
        unitOfWork: _unitOfWork,
        dateTimeProvider: new FakeDateTimeProvider(Now)
    );

    private SharedChat SeedShare(bool allowRemix)
    {
        ChatThread source = SharedChatTestFactory.CreateShareableThread(Now.AddHours(-1));
        _chats.Seed(source);

        SharedChat share = SharedChat.Create
        (
            userId: source.UserId,
            chatId: source.Id,
            currentMessageId: source.CurrentMessageId,
            title: source.Title,
            createdAt: Now.AddMinutes(-30),
            allowRemix: allowRemix
        );
        _sharedChats.Seed(share);

        return share;
    }

    [Fact]
    public async Task HandleCopiesSharedPathIntoNewChatOwnedByRemixer()
    {
        SharedChat share = SeedShare(allowRemix: true);

        ErrorOr<RemixSharedChatResult> result = await CreateHandler().Handle
        (
            new RemixSharedChatCommand(share.Id.Value),
            CancellationToken.None
        );

        Assert.False(result.IsError);
        ChatThread added = Assert.Single(_chats.AddedThreads);
        Assert.Equal(UserId.FromDatabase(RemixerUserId), added.UserId);
        Assert.Equal(added.Id.Value, result.Value.ChatId);
        Assert.Equal(share.Title.Value, result.Value.Title);
        Assert.Equal(Now, result.Value.CreatedAt);
        Assert.Equal(share.Id, added.RemixOrigin!.ShareId);
        Assert.Equal(share.ChatId, added.RemixOrigin.SourceChatId);
        Assert.Equal(share.CurrentMessageId, added.RemixOrigin.SourceMessageId);
        Assert.False(added.IsTemporary);
        Assert.Equal(1, _unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task HandleReturnsNotFoundWhenShareMissing()
    {
        ErrorOr<RemixSharedChatResult> result = await CreateHandler().Handle
        (
            new RemixSharedChatCommand(Guid.CreateVersion7()),
            CancellationToken.None
        );

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.NotFound, result.FirstError.Type);
        Assert.Empty(_chats.AddedThreads);
        Assert.Equal(0, _unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task HandleReturnsForbiddenWhenRemixNotAllowed()
    {
        SharedChat share = SeedShare(allowRemix: false);

        ErrorOr<RemixSharedChatResult> result = await CreateHandler().Handle
        (
            new RemixSharedChatCommand(share.Id.Value),
            CancellationToken.None
        );

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.Forbidden, result.FirstError.Type);
        Assert.Equal("SharedChat.RemixNotAllowed", result.FirstError.Code);
        Assert.Empty(_chats.AddedThreads);
        Assert.Equal(0, _unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task HandleReturnsValidationErrorForEmptyUserContext()
    {
        SharedChat share = SeedShare(allowRemix: true);

        ErrorOr<RemixSharedChatResult> result = await CreateHandler(userId: string.Empty).Handle
        (
            new RemixSharedChatCommand(share.Id.Value),
            CancellationToken.None
        );

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.Validation, result.FirstError.Type);
        Assert.Empty(_chats.AddedThreads);
        Assert.Equal(0, _unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task HandlePerformsPassiveCopyWithoutStartingGeneration()
    {
        SharedChat share = SeedShare(allowRemix: true);

        ErrorOr<RemixSharedChatResult> result = await CreateHandler().Handle
        (
            new RemixSharedChatCommand(share.Id.Value),
            CancellationToken.None
        );

        Assert.False(result.IsError);
        ChatThread added = Assert.Single(_chats.AddedThreads);

        // Passive copy: the head is the copied completed assistant, and no generating message exists.
        Assert.Equal(added.CurrentMessageId, added.Messages.Single(message => message.Id == added.CurrentMessageId).Id);
        Assert.DoesNotContain(added.Messages, message => message.Status == MessageStatus.Generating);
    }
}