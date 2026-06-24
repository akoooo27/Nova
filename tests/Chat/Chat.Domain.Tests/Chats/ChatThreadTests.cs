using Chat.Domain.Chats;
using Chat.Domain.Chats.Entities;
using Chat.Domain.Chats.ValueObjects;
using Chat.Domain.ModelCatalog.ValueObjects;

using ErrorOr;

namespace Chat.Domain.Tests.Chats;

public sealed class ChatThreadTests
{
    [Fact]
    public void CreateSeedsSingleRootUserMessageAndPointsHeadAtIt()
    {
        ChatThread chat = TestChatFactory.CreateThread();

        ChatMessage root = Assert.Single(chat.Messages);
        Assert.NotEqual(Guid.Empty, chat.Id.Value);
        Assert.Equal(TestChatFactory.CreateUserId(), chat.UserId);
        Assert.Equal(TestChatFactory.CreateTitle(), chat.Title);
        Assert.Equal(root.Id, chat.CurrentMessageId);
        Assert.Equal(TestChatFactory.CreatedAt, chat.CreatedAt);
        Assert.Equal(TestChatFactory.CreatedAt, chat.UpdatedAt);
        Assert.Equal(chat.Id, root.ChatId);
        Assert.Null(root.ParentMessageId);
        Assert.Equal(MessageRole.User, root.Role);
        Assert.Equal(MessageStatus.Completed, root.Status);
        Assert.Equal(TestChatFactory.CreateContent("Hello"), root.Content);
        Assert.Null(root.LlmModelId);
        Assert.Equal(TestChatFactory.CreatedAt, root.CreatedAt);
        Assert.Equal(TestChatFactory.CreatedAt, root.CompletedAt);
        Assert.Equal(0, root.SiblingIndex.Value);
    }

    [Fact]
    public void CreateDefaultsToNonTemporary()
    {
        ChatThread chat = TestChatFactory.CreateThread();

        Assert.False(chat.IsTemporary);
    }

    [Fact]
    public void CreateMarksThreadTemporaryWhenRequested()
    {
        ChatThread chat = TestChatFactory.CreateThread(isTemporary: true);

        Assert.True(chat.IsTemporary);
    }

    [Fact]
    public void BeginAssistantMessageAddsGeneratingAssistantUnderUserParentAndMovesHead()
    {
        ChatThread chat = TestChatFactory.CreateThread();
        ChatMessage root = TestChatFactory.RootMessage(chat);
        LlmModelId llmModelId = LlmModelId.New();
        DateTimeOffset createdAt = TestChatFactory.CreatedAt.AddMinutes(1);

        ErrorOr<ChatMessage> result = chat.BeginAssistantMessage(root.Id, llmModelId, createdAt);

        Assert.False(result.IsError);
        ChatMessage message = result.Value;
        Assert.Same(message, Assert.Single(chat.Messages, m => m.Id == message.Id));
        Assert.Equal(chat.Id, message.ChatId);
        Assert.Equal(root.Id, message.ParentMessageId);
        Assert.Equal(MessageRole.Assistant, message.Role);
        Assert.Equal(MessageStatus.Generating, message.Status);
        Assert.Null(message.Content);
        Assert.Equal(llmModelId, message.LlmModelId);
        Assert.Equal(createdAt, message.CreatedAt);
        Assert.Null(message.CompletedAt);
        Assert.Equal(0, message.SiblingIndex.Value);
        Assert.Equal(message.Id, chat.CurrentMessageId);
        Assert.Equal(createdAt, chat.UpdatedAt);
    }

    [Fact]
    public void BeginAssistantMessageReturnsAssistantParentMustBeUserWhenParentIsAssistant()
    {
        ChatThread chat = TestChatFactory.CreateThread();
        ChatMessage assistant = BeginAssistant(chat);

        ErrorOr<ChatMessage> result = chat.BeginAssistantMessage
        (
            parentMessageId: assistant.Id,
            llmModelId: LlmModelId.New(),
            createdAt: TestChatFactory.CreatedAt.AddMinutes(2)
        );

        AssertError(result, ErrorType.Conflict, "Chat.AssistantParentMustBeUser");
    }

    [Fact]
    public void BeginAssistantMessageReturnsParentMessageNotFoundWhenParentDoesNotExist()
    {
        ChatThread chat = TestChatFactory.CreateThread();

        ErrorOr<ChatMessage> result = chat.BeginAssistantMessage
        (
            parentMessageId: ChatMessageId.New(),
            llmModelId: LlmModelId.New(),
            createdAt: TestChatFactory.CreatedAt.AddMinutes(1)
        );

        AssertError(result, ErrorType.NotFound, "Chat.ParentMessageNotFound");
    }

    [Fact]
    public void AddUserMessageAddsUserMessageUnderAssistantParentAndMovesHead()
    {
        ChatThread chat = TestChatFactory.CreateThread();
        ChatMessage assistant = CompleteAssistant(chat);
        MessageContent content = TestChatFactory.CreateContent("Follow up");
        DateTimeOffset createdAt = TestChatFactory.CreatedAt.AddMinutes(2);

        ErrorOr<ChatMessage> result = chat.AddUserMessage(assistant.Id, content, createdAt);

        Assert.False(result.IsError);
        ChatMessage message = result.Value;
        Assert.Same(message, Assert.Single(chat.Messages, m => m.Id == message.Id));
        Assert.Equal(chat.Id, message.ChatId);
        Assert.Equal(assistant.Id, message.ParentMessageId);
        Assert.Equal(MessageRole.User, message.Role);
        Assert.Equal(MessageStatus.Completed, message.Status);
        Assert.Equal(content, message.Content);
        Assert.Null(message.LlmModelId);
        Assert.Equal(createdAt, message.CreatedAt);
        Assert.Equal(createdAt, message.CompletedAt);
        Assert.Equal(0, message.SiblingIndex.Value);
        Assert.Equal(message.Id, chat.CurrentMessageId);
        Assert.Equal(createdAt, chat.UpdatedAt);
    }

    [Fact]
    public void AddUserMessageReturnsParentStillGeneratingWhenParentAssistantIsGenerating()
    {
        ChatThread chat = TestChatFactory.CreateThread();
        ChatMessage assistant = BeginAssistant(chat);

        ErrorOr<ChatMessage> result = chat.AddUserMessage
        (
            parentMessageId: assistant.Id,
            content: TestChatFactory.CreateContent("Too eager"),
            createdAt: TestChatFactory.CreatedAt.AddMinutes(2)
        );

        AssertError(result, ErrorType.Conflict, "Chat.ParentStillGenerating");
    }

    [Fact]
    public void AddUserMessageReturnsUserParentMustBeAssistantWhenParentIsUser()
    {
        ChatThread chat = TestChatFactory.CreateThread();
        ChatMessage root = TestChatFactory.RootMessage(chat);

        ErrorOr<ChatMessage> result = chat.AddUserMessage
        (
            parentMessageId: root.Id,
            content: TestChatFactory.CreateContent("Follow up"),
            createdAt: TestChatFactory.CreatedAt.AddMinutes(1)
        );

        AssertError(result, ErrorType.Conflict, "Chat.UserParentMustBeAssistant");
    }

    [Fact]
    public void AddUserMessageReturnsParentMessageNotFoundWhenParentDoesNotExist()
    {
        ChatThread chat = TestChatFactory.CreateThread();

        ErrorOr<ChatMessage> result = chat.AddUserMessage
        (
            parentMessageId: ChatMessageId.New(),
            content: TestChatFactory.CreateContent("Follow up"),
            createdAt: TestChatFactory.CreatedAt.AddMinutes(1)
        );

        AssertError(result, ErrorType.NotFound, "Chat.ParentMessageNotFound");
    }

    [Fact]
    public void AddUserMessageAndBeginAssistantMessageAssignNextSiblingIndexWithinParentGroup()
    {
        ChatThread chat = TestChatFactory.CreateThread();
        ChatMessage root = TestChatFactory.RootMessage(chat);

        ChatMessage firstAssistant = CompleteAssistant(chat, root.Id, TestChatFactory.CreatedAt.AddMinutes(1));
        ChatMessage secondAssistant = CompleteAssistant(chat, root.Id, TestChatFactory.CreatedAt.AddMinutes(2));
        ChatMessage firstUser = AddUser(chat, firstAssistant.Id, TestChatFactory.CreatedAt.AddMinutes(3));
        ChatMessage secondUser = AddUser(chat, firstAssistant.Id, TestChatFactory.CreatedAt.AddMinutes(4));

        Assert.Equal(0, firstAssistant.SiblingIndex.Value);
        Assert.Equal(1, secondAssistant.SiblingIndex.Value);
        Assert.Equal(0, firstUser.SiblingIndex.Value);
        Assert.Equal(1, secondUser.SiblingIndex.Value);
    }

    [Fact]
    public void CompleteAssistantMessageSetsContentCompletedStatusAndCompletedAtAndUpdatesThread()
    {
        ChatThread chat = TestChatFactory.CreateThread();
        ChatMessage assistant = BeginAssistant(chat);
        MessageContent content = TestChatFactory.CreateContent("Assistant reply");
        DateTimeOffset completedAt = TestChatFactory.CreatedAt.AddMinutes(2);

        ErrorOr<ChatMessage> result = chat.CompleteAssistantMessage(assistant.Id, content, completedAt);

        Assert.False(result.IsError);
        Assert.Same(assistant, result.Value);
        Assert.Equal(content, assistant.Content);
        Assert.Equal(MessageStatus.Completed, assistant.Status);
        Assert.Equal(completedAt, assistant.CompletedAt);
        Assert.Null(assistant.FailureReason);
        Assert.Equal(completedAt, chat.UpdatedAt);
    }

    [Fact]
    public void CompleteAssistantMessageReturnsCannotCompleteNonGeneratingWhenTargetIsAlreadyTerminal()
    {
        ChatThread chat = TestChatFactory.CreateThread();
        ChatMessage assistant = CompleteAssistant(chat);

        ErrorOr<ChatMessage> result = chat.CompleteAssistantMessage
        (
            messageId: assistant.Id,
            content: TestChatFactory.CreateContent("New reply"),
            completedAt: TestChatFactory.CreatedAt.AddMinutes(3)
        );

        AssertError(result, ErrorType.Conflict, "Chat.CannotCompleteNonGenerating");
    }

    [Fact]
    public void CompleteAssistantMessageReturnsMessageNotFoundWhenTargetDoesNotExist()
    {
        ChatThread chat = TestChatFactory.CreateThread();

        ErrorOr<ChatMessage> result = chat.CompleteAssistantMessage
        (
            messageId: ChatMessageId.New(),
            content: TestChatFactory.CreateContent("Reply"),
            completedAt: TestChatFactory.CreatedAt.AddMinutes(2)
        );

        AssertError(result, ErrorType.NotFound, "Chat.MessageNotFound");
    }

    [Fact]
    public void FailAssistantMessageSetsFailedStatusFailureReasonAndCompletedAtAndUpdatesThread()
    {
        ChatThread chat = TestChatFactory.CreateThread();
        ChatMessage assistant = BeginAssistant(chat);
        FailureReason reason = TestChatFactory.CreateFailureReason();
        DateTimeOffset failedAt = TestChatFactory.CreatedAt.AddMinutes(2);

        ErrorOr<ChatMessage> result = chat.FailAssistantMessage(assistant.Id, reason, failedAt);

        Assert.False(result.IsError);
        Assert.Same(assistant, result.Value);
        Assert.Equal(MessageStatus.Failed, assistant.Status);
        Assert.Equal(reason, assistant.FailureReason);
        Assert.Equal(failedAt, assistant.CompletedAt);
        Assert.Null(assistant.Content);
        Assert.Equal(failedAt, chat.UpdatedAt);
    }

    [Fact]
    public void FailAssistantMessageReturnsCannotFailNonGeneratingWhenTargetIsAlreadyTerminal()
    {
        ChatThread chat = TestChatFactory.CreateThread();
        ChatMessage assistant = CompleteAssistant(chat);

        ErrorOr<ChatMessage> result = chat.FailAssistantMessage
        (
            messageId: assistant.Id,
            reason: TestChatFactory.CreateFailureReason(),
            failedAt: TestChatFactory.CreatedAt.AddMinutes(3)
        );

        AssertError(result, ErrorType.Conflict, "Chat.CannotFailNonGenerating");
    }

    [Fact]
    public void FailAssistantMessageReturnsMessageNotFoundWhenTargetDoesNotExist()
    {
        ChatThread chat = TestChatFactory.CreateThread();

        ErrorOr<ChatMessage> result = chat.FailAssistantMessage
        (
            messageId: ChatMessageId.New(),
            reason: TestChatFactory.CreateFailureReason(),
            failedAt: TestChatFactory.CreatedAt.AddMinutes(2)
        );

        AssertError(result, ErrorType.NotFound, "Chat.MessageNotFound");
    }

    [Fact]
    public void StopAssistantMessageWhenGeneratingStoresPartialContentAndMarksStopped()
    {
        ChatThread chat = TestChatFactory.CreateThread();
        ChatMessage assistant = BeginAssistant(chat);
        MessageContent partial = TestChatFactory.CreateContent("Partial answer");
        DateTimeOffset stoppedAt = TestChatFactory.CreatedAt.AddMinutes(2);

        ErrorOr<ChatMessage> result = chat.StopAssistantMessage(assistant.Id, partial, stoppedAt);

        Assert.False(result.IsError);
        Assert.Same(assistant, result.Value);
        Assert.Equal(MessageStatus.Stopped, assistant.Status);
        Assert.Equal(partial, assistant.Content);
        Assert.Equal(stoppedAt, assistant.CompletedAt);
        Assert.Equal(stoppedAt, chat.UpdatedAt);
    }

    [Fact]
    public void StopAssistantMessageWhenNoTextGeneratedMarksStoppedWithNullContent()
    {
        ChatThread chat = TestChatFactory.CreateThread();
        ChatMessage assistant = BeginAssistant(chat);
        DateTimeOffset stoppedAt = TestChatFactory.CreatedAt.AddMinutes(2);

        ErrorOr<ChatMessage> result = chat.StopAssistantMessage(assistant.Id, content: null, stoppedAt: stoppedAt);

        Assert.False(result.IsError);
        Assert.Equal(MessageStatus.Stopped, assistant.Status);
        Assert.Null(assistant.Content);
        Assert.Equal(stoppedAt, assistant.CompletedAt);
        Assert.Equal(stoppedAt, chat.UpdatedAt);
    }

    [Fact]
    public void StopAssistantMessageReturnsStopTargetMustBeAssistantForUserTarget()
    {
        ChatThread chat = TestChatFactory.CreateThread();

        ErrorOr<ChatMessage> result = chat.StopAssistantMessage
        (
            messageId: chat.CurrentMessageId,
            content: null,
            stoppedAt: TestChatFactory.CreatedAt.AddMinutes(1)
        );

        AssertError(result, ErrorType.Conflict, "Chat.StopTargetMustBeAssistant");
    }

    [Fact]
    public void StopAssistantMessageReturnsCannotStopNonGeneratingWhenTargetAlreadyTerminal()
    {
        ChatThread chat = TestChatFactory.CreateThread();
        ChatMessage assistant = CompleteAssistant(chat);

        ErrorOr<ChatMessage> result = chat.StopAssistantMessage
        (
            messageId: assistant.Id,
            content: TestChatFactory.CreateContent("Late"),
            stoppedAt: TestChatFactory.CreatedAt.AddMinutes(3)
        );

        AssertError(result, ErrorType.Conflict, "Chat.CannotStopNonGenerating");
    }

    [Fact]
    public void StopAssistantMessageReturnsMessageNotFoundWhenTargetDoesNotExist()
    {
        ChatThread chat = TestChatFactory.CreateThread();

        ErrorOr<ChatMessage> result = chat.StopAssistantMessage
        (
            messageId: ChatMessageId.New(),
            content: null,
            stoppedAt: TestChatFactory.CreatedAt.AddMinutes(1)
        );

        AssertError(result, ErrorType.NotFound, "Chat.MessageNotFound");
    }

    [Fact]
    public void EditUserMessageCreatesUserSiblingWithoutMutatingOriginalAndMovesHead()
    {
        ChatThread chat = TestChatFactory.CreateThread();
        ChatMessage assistant = CompleteAssistant(chat);
        ChatMessage original = AddUser(chat, assistant.Id, TestChatFactory.CreatedAt.AddMinutes(2), "Original");
        MessageContent editedContent = TestChatFactory.CreateContent("Edited");
        DateTimeOffset editedAt = TestChatFactory.CreatedAt.AddMinutes(3);

        ErrorOr<ChatMessage> result = chat.EditUserMessage(original.Id, editedContent, editedAt);

        Assert.False(result.IsError);
        ChatMessage edited = result.Value;
        Assert.NotEqual(original.Id, edited.Id);
        Assert.Equal(original.ParentMessageId, edited.ParentMessageId);
        Assert.Equal(MessageRole.User, edited.Role);
        Assert.Equal(MessageStatus.Completed, edited.Status);
        Assert.Equal(editedContent, edited.Content);
        Assert.Equal(1, edited.SiblingIndex.Value);
        Assert.Equal(TestChatFactory.CreateContent("Original"), original.Content);
        Assert.Equal(0, original.SiblingIndex.Value);
        Assert.Equal(edited.Id, chat.CurrentMessageId);
        Assert.Equal(editedAt, chat.UpdatedAt);
    }

    [Fact]
    public void EditUserMessageCreatesRootLevelSiblingWhenEditingRoot()
    {
        ChatThread chat = TestChatFactory.CreateThread();
        ChatMessage root = TestChatFactory.RootMessage(chat);

        ErrorOr<ChatMessage> result = chat.EditUserMessage
        (
            messageId: root.Id,
            content: TestChatFactory.CreateContent("Edited root"),
            createdAt: TestChatFactory.CreatedAt.AddMinutes(1)
        );

        Assert.False(result.IsError);
        ChatMessage edited = result.Value;
        Assert.Null(edited.ParentMessageId);
        Assert.Equal(1, edited.SiblingIndex.Value);
        Assert.Equal(root.Id, Assert.Single(chat.Messages, m => m.Id == root.Id).Id);
        Assert.Equal(edited.Id, chat.CurrentMessageId);
    }

    [Fact]
    public void EditUserMessageReturnsEditTargetMustBeUserForAssistantTarget()
    {
        ChatThread chat = TestChatFactory.CreateThread();
        ChatMessage assistant = BeginAssistant(chat);

        ErrorOr<ChatMessage> result = chat.EditUserMessage
        (
            messageId: assistant.Id,
            content: TestChatFactory.CreateContent("Edited"),
            createdAt: TestChatFactory.CreatedAt.AddMinutes(2)
        );

        AssertError(result, ErrorType.Conflict, "Chat.EditTargetMustBeUser");
    }

    [Fact]
    public void EditUserMessageReturnsMessageNotFoundWhenTargetDoesNotExist()
    {
        ChatThread chat = TestChatFactory.CreateThread();

        ErrorOr<ChatMessage> result = chat.EditUserMessage
        (
            messageId: ChatMessageId.New(),
            content: TestChatFactory.CreateContent("Edited"),
            createdAt: TestChatFactory.CreatedAt.AddMinutes(1)
        );

        AssertError(result, ErrorType.NotFound, "Chat.MessageNotFound");
    }

    [Fact]
    public void RegenerateAssistantCreatesAssistantSiblingForCompletedTargetAndMovesHead()
    {
        ChatThread chat = TestChatFactory.CreateThread();
        ChatMessage target = CompleteAssistant(chat);
        LlmModelId llmModelId = LlmModelId.New();
        DateTimeOffset createdAt = TestChatFactory.CreatedAt.AddMinutes(3);

        ErrorOr<ChatMessage> result = chat.RegenerateAssistant(target.Id, llmModelId, createdAt);

        Assert.False(result.IsError);
        ChatMessage regenerated = result.Value;
        Assert.NotEqual(target.Id, regenerated.Id);
        Assert.Equal(target.ParentMessageId, regenerated.ParentMessageId);
        Assert.Equal(MessageRole.Assistant, regenerated.Role);
        Assert.Equal(MessageStatus.Generating, regenerated.Status);
        Assert.Null(regenerated.Content);
        Assert.Equal(llmModelId, regenerated.LlmModelId);
        Assert.Equal(1, regenerated.SiblingIndex.Value);
        Assert.Equal(regenerated.Id, chat.CurrentMessageId);
        Assert.Equal(createdAt, chat.UpdatedAt);
    }

    [Fact]
    public void RegenerateAssistantCreatesAssistantSiblingForFailedTarget()
    {
        ChatThread chat = TestChatFactory.CreateThread();
        ChatMessage target = BeginAssistant(chat);
        _ = chat.FailAssistantMessage
        (
            messageId: target.Id,
            reason: TestChatFactory.CreateFailureReason(),
            failedAt: TestChatFactory.CreatedAt.AddMinutes(2)
        );

        ErrorOr<ChatMessage> result = chat.RegenerateAssistant
        (
            messageId: target.Id,
            llmModelId: LlmModelId.New(),
            createdAt: TestChatFactory.CreatedAt.AddMinutes(3)
        );

        Assert.False(result.IsError);
        ChatMessage regenerated = result.Value;
        Assert.Equal(target.ParentMessageId, regenerated.ParentMessageId);
        Assert.Equal(MessageRole.Assistant, regenerated.Role);
        Assert.Equal(MessageStatus.Generating, regenerated.Status);
        Assert.Equal(1, regenerated.SiblingIndex.Value);
    }

    [Fact]
    public void RegenerateAssistantReturnsCannotRegenerateWhileGeneratingForGeneratingTarget()
    {
        ChatThread chat = TestChatFactory.CreateThread();
        ChatMessage assistant = BeginAssistant(chat);

        ErrorOr<ChatMessage> result = chat.RegenerateAssistant
        (
            messageId: assistant.Id,
            llmModelId: LlmModelId.New(),
            createdAt: TestChatFactory.CreatedAt.AddMinutes(2)
        );

        AssertError(result, ErrorType.Conflict, "Chat.CannotRegenerateWhileGenerating");
    }

    [Fact]
    public void RegenerateAssistantReturnsRegenerationTargetMustBeAssistantForUserTarget()
    {
        ChatThread chat = TestChatFactory.CreateThread();
        ChatMessage root = TestChatFactory.RootMessage(chat);

        ErrorOr<ChatMessage> result = chat.RegenerateAssistant
        (
            messageId: root.Id,
            llmModelId: LlmModelId.New(),
            createdAt: TestChatFactory.CreatedAt.AddMinutes(1)
        );

        AssertError(result, ErrorType.Conflict, "Chat.RegenerationTargetMustBeAssistant");
    }

    [Fact]
    public void RegenerateAssistantReturnsMessageNotFoundWhenTargetDoesNotExist()
    {
        ChatThread chat = TestChatFactory.CreateThread();

        ErrorOr<ChatMessage> result = chat.RegenerateAssistant
        (
            messageId: ChatMessageId.New(),
            llmModelId: LlmModelId.New(),
            createdAt: TestChatFactory.CreatedAt.AddMinutes(1)
        );

        AssertError(result, ErrorType.NotFound, "Chat.MessageNotFound");
    }

    [Fact]
    public void SelectMessageMovesHeadForExistingGeneratingAssistant()
    {
        ChatThread chat = TestChatFactory.CreateThread();
        ChatMessage root = TestChatFactory.RootMessage(chat);
        ChatMessage assistant = BeginAssistant(chat);
        DateTimeOffset selectedAt = TestChatFactory.CreatedAt.AddMinutes(2);

        ErrorOr<Success> result = chat.SelectMessage(root.Id, TestChatFactory.CreatedAt.AddMinutes(3));
        Assert.False(result.IsError);

        result = chat.SelectMessage(assistant.Id, selectedAt);

        Assert.False(result.IsError);
        Assert.Equal(assistant.Id, chat.CurrentMessageId);
        Assert.Equal(selectedAt, chat.UpdatedAt);
    }

    [Fact]
    public void SelectMessageReturnsMessageNotFoundWhenTargetDoesNotExist()
    {
        ChatThread chat = TestChatFactory.CreateThread();

        ErrorOr<Success> result = chat.SelectMessage
        (
            messageId: ChatMessageId.New(),
            updatedAt: TestChatFactory.CreatedAt.AddMinutes(1)
        );

        AssertError(result, ErrorType.NotFound, "Chat.MessageNotFound");
    }

    [Fact]
    public void CreateDefaultsToUnpinnedAndUnarchived()
    {
        ChatThread chat = TestChatFactory.CreateThread();

        Assert.Null(chat.PinnedAt);
        Assert.False(chat.IsPinned);
        Assert.False(chat.IsArchived);
    }

    [Fact]
    public void PinStoresPinnedTimestampAndMarksThreadPinned()
    {
        ChatThread chat = TestChatFactory.CreateThread();
        DateTimeOffset pinnedAt = TestChatFactory.CreatedAt.AddMinutes(5);

        chat.Pin(pinnedAt);

        Assert.Equal(pinnedAt, chat.PinnedAt);
        Assert.True(chat.IsPinned);
    }

    [Fact]
    public void PinKeepsOriginalPinnedTimestampWhenAlreadyPinned()
    {
        ChatThread chat = TestChatFactory.CreateThread();
        DateTimeOffset firstPinnedAt = TestChatFactory.CreatedAt.AddMinutes(5);
        DateTimeOffset secondPinnedAt = TestChatFactory.CreatedAt.AddMinutes(10);

        chat.Pin(firstPinnedAt);
        chat.Pin(secondPinnedAt);

        Assert.Equal(firstPinnedAt, chat.PinnedAt);
    }

    [Fact]
    public void UnpinClearsPinnedTimestampAndMarksThreadUnpinned()
    {
        ChatThread chat = TestChatFactory.CreateThread();

        chat.Pin(TestChatFactory.CreatedAt.AddMinutes(5));
        chat.Unpin();

        Assert.Null(chat.PinnedAt);
        Assert.False(chat.IsPinned);
    }

    [Fact]
    public void ArchiveAndUnarchiveDoNotChangePinnedState()
    {
        ChatThread chat = TestChatFactory.CreateThread();
        DateTimeOffset pinnedAt = TestChatFactory.CreatedAt.AddMinutes(5);

        chat.Pin(pinnedAt);
        chat.Archive();

        Assert.True(chat.IsArchived);
        Assert.True(chat.IsPinned);
        Assert.Equal(pinnedAt, chat.PinnedAt);

        chat.Unarchive();

        Assert.False(chat.IsArchived);
        Assert.True(chat.IsPinned);
        Assert.Equal(pinnedAt, chat.PinnedAt);
    }

    [Fact]
    public void UnpinDoesNotChangeArchivedState()
    {
        ChatThread chat = TestChatFactory.CreateThread();

        chat.Pin(TestChatFactory.CreatedAt.AddMinutes(5));
        chat.Archive();
        chat.Unpin();

        Assert.True(chat.IsArchived);
        Assert.False(chat.IsPinned);
        Assert.Null(chat.PinnedAt);
    }

    [Fact]
    public void RenameReplacesTitleWithoutUpdatingActivityTimestamp()
    {
        ChatThread chat = TestChatFactory.CreateThread();
        ChatTitle title = TestChatFactory.CreateTitle("Renamed chat");

        chat.Rename(title);

        Assert.Equal(title, chat.Title);
        Assert.Equal(TestChatFactory.CreatedAt, chat.UpdatedAt);
    }

    [Fact]
    public void MetadataStateChangesDoNotUpdateActivityTimestamp()
    {
        ChatThread chat = TestChatFactory.CreateThread();
        DateTimeOffset pinnedAt = TestChatFactory.CreatedAt.AddMinutes(5);

        chat.Pin(pinnedAt);
        chat.Archive();
        chat.Unpin();
        chat.Unarchive();

        Assert.Equal(TestChatFactory.CreatedAt, chat.UpdatedAt);
    }

    [Fact]
    public void FindMessageReturnsExistingMessageAndNullForUnknownMessage()
    {
        ChatThread chat = TestChatFactory.CreateThread();
        ChatMessage root = TestChatFactory.RootMessage(chat);

        Assert.Same(root, chat.FindMessage(root.Id));
        Assert.Null(chat.FindMessage(ChatMessageId.New()));
    }

    [Fact]
    public void BranchFromCopiesOnlySelectedPathWithNewIdsAndIndependentMetadata()
    {
        ChatThread source = TestChatFactory.CreateThread();
        source.Pin(TestChatFactory.CreatedAt.AddMinutes(1));
        source.Archive();
        ChatMessage root = TestChatFactory.RootMessage(source);
        ChatMessage firstAssistant = CompleteAssistant(source, root.Id, TestChatFactory.CreatedAt.AddMinutes(2));
        ChatMessage followUp = AddUser(source, firstAssistant.Id, TestChatFactory.CreatedAt.AddMinutes(4), "Follow up");
        ChatMessage branchPoint = CompleteAssistant(source, followUp.Id, TestChatFactory.CreatedAt.AddMinutes(5));
        _ = AddUser(source, branchPoint.Id, TestChatFactory.CreatedAt.AddMinutes(7), "Excluded descendant");
        _ = CompleteAssistant(source, followUp.Id, TestChatFactory.CreatedAt.AddMinutes(8));
        ChatMessage[] sourcePath = [root, firstAssistant, followUp, branchPoint];
        DateTimeOffset branchedAt = TestChatFactory.CreatedAt.AddHours(1);

        ErrorOr<ChatThread> result = ChatThread.BranchFrom(source, branchPoint.Id, branchedAt);

        Assert.False(result.IsError);
        ChatThread branch = result.Value;
        ChatMessage[] copiedPath = GetActivePath(branch);
        Assert.Equal(sourcePath.Length, copiedPath.Length);
        Assert.Equal(sourcePath.Length, branch.Messages.Count);
        Assert.DoesNotContain(branch.Messages, copied => source.Messages.Any(original => original.Id == copied.Id));

        for (int index = 0; index < sourcePath.Length; index++)
        {
            Assert.Equal(sourcePath[index].Role, copiedPath[index].Role);
            Assert.Equal(sourcePath[index].Content, copiedPath[index].Content);
            Assert.Equal(sourcePath[index].LlmModelId, copiedPath[index].LlmModelId);
            Assert.Equal(sourcePath[index].Status, copiedPath[index].Status);
            Assert.Equal(sourcePath[index].FailureReason, copiedPath[index].FailureReason);
            Assert.Equal(sourcePath[index].CreatedAt, copiedPath[index].CreatedAt);
            Assert.Equal(sourcePath[index].CompletedAt, copiedPath[index].CompletedAt);
            Assert.Equal(0, copiedPath[index].SiblingIndex.Value);
            Assert.Equal(index == 0 ? null : copiedPath[index - 1].Id, copiedPath[index].ParentMessageId);
        }

        Assert.Equal(source.UserId, branch.UserId);
        Assert.Equal("Branch: Planning chat", branch.Title.Value);
        Assert.False(branch.IsTemporary);
        Assert.False(branch.IsPinned);
        Assert.False(branch.IsArchived);
        Assert.Equal(branchedAt, branch.CreatedAt);
        Assert.Equal(branchedAt, branch.UpdatedAt);
        Assert.Equal(copiedPath[^1].Id, branch.CurrentMessageId);
        Assert.Equal(source.Id, branch.BranchOrigin!.SourceChatId);
        Assert.Equal(branchPoint.Id, branch.BranchOrigin.SourceMessageId);
        Assert.Equal(6, source.Messages.Count);
    }

    [Fact]
    public void BranchFromPreservesFailedAssistantState()
    {
        ChatThread source = TestChatFactory.CreateThread();
        ChatMessage assistant = BeginAssistant(source);
        FailureReason reason = TestChatFactory.CreateFailureReason("Rate limited");
        DateTimeOffset failedAt = TestChatFactory.CreatedAt.AddMinutes(2);
        _ = source.FailAssistantMessage(assistant.Id, reason, failedAt);

        ErrorOr<ChatThread> result = ChatThread.BranchFrom
        (
            source,
            assistant.Id,
            TestChatFactory.CreatedAt.AddHours(1)
        );

        Assert.False(result.IsError);
        ChatMessage copied = GetActivePath(result.Value)[^1];
        Assert.Equal(MessageStatus.Failed, copied.Status);
        Assert.Equal(reason, copied.FailureReason);
        Assert.Equal(failedAt, copied.CompletedAt);
        Assert.Null(copied.Content);
    }

    [Fact]
    public void BranchFromRejectsTemporarySourceChat()
    {
        ChatThread source = TestChatFactory.CreateThread(isTemporary: true);
        ChatMessage assistant = CompleteAssistant(source);

        ErrorOr<ChatThread> result = ChatThread.BranchFrom(source, assistant.Id, TestChatFactory.CreatedAt.AddHours(1));

        AssertError(result, ErrorType.Conflict, "Chat.CannotBranchTemporaryChat");
    }

    [Fact]
    public void BranchFromRejectsUserBranchPoint()
    {
        ChatThread source = TestChatFactory.CreateThread();
        ChatMessage root = TestChatFactory.RootMessage(source);

        ErrorOr<ChatThread> result = ChatThread.BranchFrom(source, root.Id, TestChatFactory.CreatedAt);

        AssertError(result, ErrorType.Conflict, "Chat.BranchPointMustBeAssistant");
    }

    [Fact]
    public void BranchFromRejectsGeneratingAssistantBranchPoint()
    {
        ChatThread source = TestChatFactory.CreateThread();
        ChatMessage assistant = BeginAssistant(source);

        ErrorOr<ChatThread> result = ChatThread.BranchFrom(source, assistant.Id, TestChatFactory.CreatedAt);

        AssertError(result, ErrorType.Conflict, "Chat.CannotBranchWhileGenerating");
    }

    [Fact]
    public void BranchFromReturnsMessageNotFoundForUnknownBranchPoint()
    {
        ChatThread source = TestChatFactory.CreateThread();

        ErrorOr<ChatThread> result = ChatThread.BranchFrom
        (
            source,
            ChatMessageId.New(),
            TestChatFactory.CreatedAt
        );

        AssertError(result, ErrorType.NotFound, "Chat.MessageNotFound");
    }

    [Fact]
    public void BranchFromRejectsCyclicPersistedPath()
    {
        ChatThread source = TestChatFactory.CreateThread();
        ChatMessage root = TestChatFactory.RootMessage(source);
        ChatMessage assistant = CompleteAssistant(source);
        SetParentForCorruptionTest(root, assistant.Id);

        ErrorOr<ChatThread> result = ChatThread.BranchFrom(source, assistant.Id, TestChatFactory.CreatedAt);

        AssertError(result, ErrorType.Unexpected, "Chat.InvalidBranchPath");
    }

    [Fact]
    public void BranchFromRejectsMissingPersistedAncestor()
    {
        ChatThread source = TestChatFactory.CreateThread();
        ChatMessage root = TestChatFactory.RootMessage(source);
        ChatMessage assistant = CompleteAssistant(source);
        SetParentForCorruptionTest(root, ChatMessageId.New());

        ErrorOr<ChatThread> result = ChatThread.BranchFrom(source, assistant.Id, TestChatFactory.CreatedAt);

        AssertError(result, ErrorType.Unexpected, "Chat.InvalidBranchPath");
    }

    [Fact]
    public void BranchFromRejectsAssistantAsPersistedRoot()
    {
        ChatThread source = TestChatFactory.CreateThread();
        ChatMessage assistant = CompleteAssistant(source);
        SetParentForCorruptionTest(assistant, null);

        ErrorOr<ChatThread> result = ChatThread.BranchFrom(source, assistant.Id, TestChatFactory.CreatedAt);

        AssertError(result, ErrorType.Unexpected, "Chat.InvalidBranchPath");
    }

    [Fact]
    public void ValidateShareAtRejectsTemporaryChat()
    {
        ChatThread temporary = TestChatFactory.CreateThread(isTemporary: true);
        ChatMessage node = CompleteAssistant(temporary);

        AssertError(temporary.ValidateShareAt(node.Id), ErrorType.Conflict, "Chat.CannotShareTemporaryChat");
    }

    [Fact]
    public void ValidateShareAtReturnsMessageNotFoundForUnknownNode()
    {
        ChatThread chat = TestChatFactory.CreateThread();
        _ = CompleteAssistant(chat);

        AssertError(chat.ValidateShareAt(ChatMessageId.New()), ErrorType.NotFound, "Chat.MessageNotFound");
    }

    [Fact]
    public void ValidateShareAtRejectsGeneratingNode()
    {
        ChatThread chat = TestChatFactory.CreateThread();
        ChatMessage generating = BeginAssistant(chat);

        AssertError(chat.ValidateShareAt(generating.Id), ErrorType.Conflict, "Chat.CannotShareGeneratingMessage");
    }

    [Fact]
    public void ValidateShareAtAcceptsCompletedRootUserMessage()
    {
        ChatThread chat = TestChatFactory.CreateThread();
        ChatMessage root = TestChatFactory.RootMessage(chat);

        Assert.False(chat.ValidateShareAt(root.Id).IsError);
    }

    [Fact]
    public void ValidateShareAtAcceptsHistoricalTerminalNodeThatIsNotCurrentHead()
    {
        ChatThread chat = TestChatFactory.CreateThread();
        ChatMessage historical = CompleteAssistant(chat);
        ChatMessage followUp = AddUser(chat, historical.Id, TestChatFactory.CreatedAt.AddMinutes(3));
        ChatMessage current = CompleteAssistant(chat, followUp.Id, TestChatFactory.CreatedAt.AddMinutes(4));

        Assert.NotEqual(historical.Id, chat.CurrentMessageId);
        Assert.Equal(current.Id, chat.CurrentMessageId);
        Assert.False(chat.ValidateShareAt(historical.Id).IsError);
    }

    [Fact]
    public void ValidateShareAtRejectsCyclicPersistedPath()
    {
        ChatThread chat = TestChatFactory.CreateThread();
        ChatMessage root = TestChatFactory.RootMessage(chat);
        ChatMessage assistant = CompleteAssistant(chat);
        SetParentForCorruptionTest(root, assistant.Id);

        AssertError(chat.ValidateShareAt(assistant.Id), ErrorType.Unexpected, "Chat.InvalidSharePath");
    }

    [Fact]
    public void ValidateShareAtRejectsMissingPersistedAncestor()
    {
        ChatThread chat = TestChatFactory.CreateThread();
        ChatMessage root = TestChatFactory.RootMessage(chat);
        ChatMessage assistant = CompleteAssistant(chat);
        SetParentForCorruptionTest(root, ChatMessageId.New());

        AssertError(chat.ValidateShareAt(assistant.Id), ErrorType.Unexpected, "Chat.InvalidSharePath");
    }

    [Fact]
    public void ValidateShareAtRejectsAssistantAsPersistedRoot()
    {
        ChatThread chat = TestChatFactory.CreateThread();
        ChatMessage assistant = CompleteAssistant(chat);
        SetParentForCorruptionTest(assistant, null);

        AssertError(chat.ValidateShareAt(assistant.Id), ErrorType.Unexpected, "Chat.InvalidSharePath");
    }

    private static ChatMessage[] GetActivePath(ChatThread chat)
    {
        List<ChatMessage> path = [];
        ChatMessage? cursor = chat.FindMessage(chat.CurrentMessageId);

        while (cursor is not null)
        {
            path.Add(cursor);
            cursor = cursor.ParentMessageId is null ? null : chat.FindMessage(cursor.ParentMessageId);
        }

        path.Reverse();
        return [.. path];
    }

    private static void SetParentForCorruptionTest(ChatMessage message, ChatMessageId? parentMessageId)
    {
        typeof(ChatMessage)
            .GetProperty(nameof(ChatMessage.ParentMessageId))!
            .SetValue(message, parentMessageId);
    }

    private static ChatMessage BeginAssistant
    (
        ChatThread chat,
        ChatMessageId? parentMessageId = null,
        DateTimeOffset? createdAt = null
    )
    {
        ChatMessage parent = parentMessageId is null
            ? TestChatFactory.RootMessage(chat)
            : chat.FindMessage(parentMessageId) ?? throw new InvalidOperationException("Parent was not found.");

        ErrorOr<ChatMessage> result = chat.BeginAssistantMessage
        (
            parentMessageId: parent.Id,
            llmModelId: LlmModelId.New(),
            createdAt: createdAt ?? TestChatFactory.CreatedAt.AddMinutes(1)
        );

        Assert.False(result.IsError);
        return result.Value;
    }

    private static ChatMessage AddUser
    (
        ChatThread chat,
        ChatMessageId parentMessageId,
        DateTimeOffset createdAt,
        string content = "Follow up"
    )
    {
        ErrorOr<ChatMessage> result = chat.AddUserMessage
        (
            parentMessageId: parentMessageId,
            content: TestChatFactory.CreateContent(content),
            createdAt: createdAt
        );

        Assert.False(result.IsError);
        return result.Value;
    }

    private static ChatMessage CompleteAssistant
    (
        ChatThread chat,
        ChatMessageId? parentMessageId = null,
        DateTimeOffset? createdAt = null
    )
    {
        ChatMessage assistant = BeginAssistant(chat, parentMessageId, createdAt);
        ErrorOr<ChatMessage> result = chat.CompleteAssistantMessage
        (
            messageId: assistant.Id,
            content: TestChatFactory.CreateContent("Assistant reply"),
            completedAt: (createdAt ?? TestChatFactory.CreatedAt.AddMinutes(1)).AddMinutes(1)
        );

        Assert.False(result.IsError);
        return result.Value;
    }

    private static void AssertError<T>(ErrorOr<T> result, ErrorType type, string code)
    {
        Assert.True(result.IsError);
        Error error = Assert.Single(result.Errors);
        Assert.Equal(type, error.Type);
        Assert.Equal(code, error.Code);
    }
}