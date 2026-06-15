using Chat.Application.Chats.Commands.UpdateChat;
using Chat.Domain.Chats.ValueObjects;

using FluentValidation.Results;

namespace Chat.Application.Tests.Chats.Commands;

public sealed class UpdateChatCommandValidatorTests
{
    private readonly UpdateChatCommandValidator _validator = new();

    [Fact]
    public void ValidateAcceptsFullEditableMetadataState()
    {
        UpdateChatCommand command = new
        (
            ChatId: Guid.CreateVersion7(),
            Title: "Renamed chat",
            IsPinned: true,
            IsArchived: false
        );

        ValidationResult result = _validator.Validate(command);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void ValidateRejectsEmptyChatId()
    {
        UpdateChatCommand command = new
        (
            ChatId: Guid.Empty,
            Title: "Renamed chat",
            IsPinned: true,
            IsArchived: false
        );

        ValidationResult result = _validator.Validate(command);

        ValidationFailure failure = Assert.Single(result.Errors);
        Assert.Equal(nameof(UpdateChatCommand.ChatId), failure.PropertyName);
    }

    [Fact]
    public void ValidateRejectsBlankTitle()
    {
        UpdateChatCommand command = new
        (
            ChatId: Guid.CreateVersion7(),
            Title: "",
            IsPinned: false,
            IsArchived: false
        );

        ValidationResult result = _validator.Validate(command);

        Assert.Contains(result.Errors, failure => failure.PropertyName == nameof(UpdateChatCommand.Title));
    }

    [Fact]
    public void ValidateRejectsTitleLongerThanDomainLimit()
    {
        UpdateChatCommand command = new
        (
            ChatId: Guid.CreateVersion7(),
            Title: new string('a', ChatTitle.MaxLength + 1),
            IsPinned: false,
            IsArchived: false
        );

        ValidationResult result = _validator.Validate(command);

        Assert.Contains(result.Errors, failure => failure.PropertyName == nameof(UpdateChatCommand.Title));
    }
}
