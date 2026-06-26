using FluentValidation;

namespace Chat.Application.SharedChats.Queries.GetSharedChats;

internal sealed class GetSharedChatsQueryValidator : AbstractValidator<GetSharedChatsQuery>
{
    public GetSharedChatsQueryValidator()
    {
        RuleFor(x => x.Limit)
            .InclusiveBetween(SharedChatLimits.MinQueryLimit, SharedChatLimits.MaxQueryLimit);

        RuleFor(x => x.Offset)
            .GreaterThanOrEqualTo(0);
    }
}