using Chat.Domain.Chats;
using Chat.Domain.Chats.Entities;
using Chat.Domain.Chats.ValueObjects;
using Chat.Domain.ModelCatalog.ValueObjects;
using Chat.Domain.Shared;

using ErrorOr;

namespace Chat.Domain.Tests.Chats;

public sealed class MessageKindTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 12, 12, 0, 0, TimeSpan.Zero);

    private static ChatThread CreateThread(bool isTemporary = false) =>
        ChatThread.Create
        (
            userId: UserId.Create("auth0|user-1").Value,
            title: ChatTitle.Create("Research").Value,
            firstUserMessage: MessageContent.Create("Research the topic").Value,
            createdAt: Now,
            isTemporary: isTemporary
        );

    [Fact]
    public void CreateRootUserMessageHasTextKind()
    {
        ChatThread thread = CreateThread();

        Assert.Equal(MessageKind.Text, thread.FindMessage(thread.CurrentMessageId)!.Kind);
    }

    [Fact]
    public void BeginAssistantMessageDefaultsToTextKind()
    {
        ChatThread thread = CreateThread();

        ChatMessage assistant = thread.BeginAssistantMessage(thread.CurrentMessageId, LlmModelId.New(), Now).Value;

        Assert.Equal(MessageKind.Text, assistant.Kind);
    }

    [Fact]
    public void BeginAssistantMessageWithAgentRunKindSetsKind()
    {
        ChatThread thread = CreateThread();

        ChatMessage assistant = thread.BeginAssistantMessage
        (
            parentMessageId: thread.CurrentMessageId,
            llmModelId: LlmModelId.New(),
            createdAt: Now,
            kind: MessageKind.AgentRun
        ).Value;

        Assert.Equal(MessageKind.AgentRun, assistant.Kind);
    }

    [Fact]
    public void BeginAssistantMessageAgentRunKindOnTemporaryChatReturnsCannotStart()
    {
        ChatThread thread = CreateThread(isTemporary: true);

        ErrorOr<ChatMessage> result = thread.BeginAssistantMessage
        (
            parentMessageId: thread.CurrentMessageId,
            llmModelId: LlmModelId.New(),
            createdAt: Now,
            kind: MessageKind.AgentRun
        );

        Assert.True(result.IsError);
        Assert.Equal("Chat.CannotStartAgentRunInTemporaryChat", result.FirstError.Code);
    }

    [Fact]
    public void BeginAssistantMessageTextKindOnTemporaryChatStillAllowed()
    {
        ChatThread thread = CreateThread(isTemporary: true);

        ErrorOr<ChatMessage> result = thread.BeginAssistantMessage(thread.CurrentMessageId, LlmModelId.New(), Now);

        Assert.False(result.IsError);
    }

    [Fact]
    public void RegenerateAssistantOnAgentRunMessageReturnsCannotRegenerate()
    {
        ChatThread thread = CreateThread();

        ChatMessage assistant = thread.BeginAssistantMessage
        (
            parentMessageId: thread.CurrentMessageId,
            llmModelId: LlmModelId.New(),
            createdAt: Now,
            kind: MessageKind.AgentRun
        ).Value;

        thread.CompleteAssistantMessage(assistant.Id, MessageContent.Create("# Report").Value, Now);

        ErrorOr<ChatMessage> result = thread.RegenerateAssistant(assistant.Id, LlmModelId.New(), Now);

        Assert.True(result.IsError);
        Assert.Equal("Chat.CannotRegenerateAgentRun", result.FirstError.Code);
    }

    [Fact]
    public void BranchFromPreservesAgentRunKindOnCopies()
    {
        ChatThread thread = CreateThread();

        ChatMessage assistant = thread.BeginAssistantMessage
        (
            parentMessageId: thread.CurrentMessageId,
            llmModelId: LlmModelId.New(),
            createdAt: Now,
            kind: MessageKind.AgentRun
        ).Value;

        thread.CompleteAssistantMessage(assistant.Id, MessageContent.Create("# Report").Value, Now);

        ChatThread branch = ChatThread.BranchFrom(thread, assistant.Id, Now).Value;

        Assert.Contains(branch.Messages, message => message.Kind == MessageKind.AgentRun);
    }
}