using FluentValidation;

namespace Chat.Application.Chats.Queries.SearchChats;

internal sealed class SearchChatsQueryValidator : AbstractValidator<SearchChatsQuery>
{
    public SearchChatsQueryValidator()
    {
        RuleFor(x => x.Query)
            .Must(query => !string.IsNullOrWhiteSpace(query))
            .WithMessage("Search query is required.")
            .MaximumLength(ChatLimits.MaxSearchQueryLength);

        RuleFor(x => x.Limit)
            .InclusiveBetween(ChatLimits.MinQueryLimit, ChatLimits.MaxQueryLimit);

        RuleFor(x => x.Offset)
            .GreaterThanOrEqualTo(0);
    }
}