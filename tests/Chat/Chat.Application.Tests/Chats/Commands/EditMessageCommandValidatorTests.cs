using Chat.Application.Chats.Commands.EditMessage;
using Chat.Domain.Chats.ValueObjects;

using FluentValidation.Results;

namespace Chat.Application.Tests.Chats.Commands;

public sealed class EditMessageCommandValidatorTests
{
    private readonly EditMessageCommandValidator _validator = new();

    [Fact]
    public void ValidateAcceptsPopulatedCommand()
    {
        EditMessageCommand command = new
        (
            ChatId: Guid.CreateVersion7(),
            MessageId: Guid.CreateVersion7(),
            Message: "Edited text",
            LlmModelId: Guid.CreateVersion7()
        );

        ValidationResult result = _validator.Validate(command);

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData("chat")]
    [InlineData("message")]
    [InlineData("model")]
    public void ValidateRejectsEmptyRequiredId(string emptyField)
    {
        EditMessageCommand command = new
        (
            ChatId: emptyField == "chat" ? Guid.Empty : Guid.CreateVersion7(),
            MessageId: emptyField == "message" ? Guid.Empty : Guid.CreateVersion7(),
            Message: "Edited text",
            LlmModelId: emptyField == "model" ? Guid.Empty : Guid.CreateVersion7()
        );

        ValidationResult result = _validator.Validate(command);

        string expectedProperty = emptyField switch
        {
            "chat" => nameof(EditMessageCommand.ChatId),
            "message" => nameof(EditMessageCommand.MessageId),
            _ => nameof(EditMessageCommand.LlmModelId)
        };
        Assert.Contains(result.Errors, failure => failure.PropertyName == expectedProperty);
    }

    [Fact]
    public void ValidateRejectsEmptyMessage()
    {
        EditMessageCommand command = new
        (
            ChatId: Guid.CreateVersion7(),
            MessageId: Guid.CreateVersion7(),
            Message: string.Empty,
            LlmModelId: Guid.CreateVersion7()
        );

        ValidationResult result = _validator.Validate(command);

        Assert.Contains(result.Errors, failure => failure.PropertyName == nameof(EditMessageCommand.Message));
    }

    [Fact]
    public void ValidateRejectsOversizedMessage()
    {
        EditMessageCommand command = new
        (
            ChatId: Guid.CreateVersion7(),
            MessageId: Guid.CreateVersion7(),
            Message: new string('x', MessageContent.MaxLength + 1),
            LlmModelId: Guid.CreateVersion7()
        );

        ValidationResult result = _validator.Validate(command);

        Assert.Contains(result.Errors, failure => failure.PropertyName == nameof(EditMessageCommand.Message));
    }
}