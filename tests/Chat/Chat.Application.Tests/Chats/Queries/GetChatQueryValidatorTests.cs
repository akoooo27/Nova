using Chat.Application.Chats.Queries.GetChat;

using FluentValidation.Results;

namespace Chat.Application.Tests.Chats.Queries;

public sealed class GetChatQueryValidatorTests
{
    private readonly GetChatQueryValidator _validator = new();

    [Fact]
    public void ValidateAcceptsNonEmptyChatId()
    {
        GetChatQuery query = new(ChatId: Guid.CreateVersion7());

        ValidationResult result = _validator.Validate(query);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void ValidateRejectsEmptyChatId()
    {
        GetChatQuery query = new(ChatId: Guid.Empty);

        ValidationResult result = _validator.Validate(query);

        ValidationFailure failure = Assert.Single(result.Errors);
        Assert.Equal(nameof(GetChatQuery.ChatId), failure.PropertyName);
    }
}