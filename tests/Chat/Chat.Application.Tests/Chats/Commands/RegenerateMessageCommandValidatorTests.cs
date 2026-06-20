using Chat.Application.Chats.Commands.RegenerateMessage;

using FluentValidation.Results;

namespace Chat.Application.Tests.Chats.Commands;

public sealed class RegenerateMessageCommandValidatorTests
{
    private readonly RegenerateMessageCommandValidator _validator = new();

    [Fact]
    public void ValidateAcceptsChatIdAndMessageIdWithoutModelId()
    {
        RegenerateMessageCommand command = new
        (
            ChatId: Guid.CreateVersion7(),
            MessageId: Guid.CreateVersion7()
        );

        ValidationResult result = _validator.Validate(command);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void ValidateAcceptsSuppliedModelId()
    {
        RegenerateMessageCommand command = new
        (
            ChatId: Guid.CreateVersion7(),
            MessageId: Guid.CreateVersion7(),
            ModelId: Guid.CreateVersion7()
        );

        ValidationResult result = _validator.Validate(command);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void ValidateRejectsEmptyChatId()
    {
        RegenerateMessageCommand command = new
        (
            ChatId: Guid.Empty,
            MessageId: Guid.CreateVersion7()
        );

        ValidationResult result = _validator.Validate(command);

        Assert.Contains(result.Errors, failure => failure.PropertyName == nameof(RegenerateMessageCommand.ChatId));
    }

    [Fact]
    public void ValidateRejectsEmptyMessageId()
    {
        RegenerateMessageCommand command = new
        (
            ChatId: Guid.CreateVersion7(),
            MessageId: Guid.Empty
        );

        ValidationResult result = _validator.Validate(command);

        Assert.Contains(result.Errors, failure => failure.PropertyName == nameof(RegenerateMessageCommand.MessageId));
    }

    [Fact]
    public void ValidateRejectsSuppliedButEmptyModelId()
    {
        RegenerateMessageCommand command = new
        (
            ChatId: Guid.CreateVersion7(),
            MessageId: Guid.CreateVersion7(),
            ModelId: Guid.Empty
        );

        ValidationResult result = _validator.Validate(command);

        Assert.Contains(result.Errors, failure => failure.PropertyName == nameof(RegenerateMessageCommand.ModelId));
    }
}