using Chat.Application.SharedChats.Commands.Create;

using FluentValidation.Results;

namespace Chat.Application.Tests.SharedChats;

public sealed class CreateSharedChatCommandValidatorTests
{
    private readonly CreateSharedChatCommandValidator _validator = new();

    [Fact]
    public void ValidateAcceptsPopulatedIds()
    {
        CreateSharedChatCommand command = new
        (
            ChatId: Guid.CreateVersion7(),
            CurrentMessageId: Guid.CreateVersion7()
        );

        ValidationResult result = _validator.Validate(command);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void ValidateRejectsEmptyChatId()
    {
        CreateSharedChatCommand command = new
        (
            ChatId: Guid.Empty,
            CurrentMessageId: Guid.CreateVersion7()
        );

        ValidationResult result = _validator.Validate(command);

        Assert.Contains(result.Errors, failure => failure.PropertyName == nameof(CreateSharedChatCommand.ChatId));
    }

    [Fact]
    public void ValidateRejectsEmptyCurrentMessageId()
    {
        CreateSharedChatCommand command = new
        (
            ChatId: Guid.CreateVersion7(),
            CurrentMessageId: Guid.Empty
        );

        ValidationResult result = _validator.Validate(command);

        Assert.Contains(result.Errors, failure => failure.PropertyName == nameof(CreateSharedChatCommand.CurrentMessageId));
    }
}