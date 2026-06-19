using FluentValidation;

namespace Chat.Application.Chats.Queries.GetChat;

internal sealed class GetChatQueryValidator : AbstractValidator<GetChatQuery>
{
    public GetChatQueryValidator()
    {
        RuleFor(x => x.ChatId)
            .NotEmpty();
    }
}