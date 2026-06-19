using FluentValidation;

namespace Chat.Application.Chats.Queries.GetChats;

internal sealed class GetChatsQueryValidator : AbstractValidator<GetChatsQuery>
{
    public GetChatsQueryValidator()
    {
        RuleFor(x => x.Limit)
            .InclusiveBetween(ChatLimits.MinQueryLimit, ChatLimits.MaxQueryLimit);

        RuleFor(x => x.Offset)
            .GreaterThanOrEqualTo(0);
    }
}