using Chat.Application.SharedChats.Errors;
using Chat.Domain.Shared;
using Chat.Domain.SharedChats.ValueObjects;

using ErrorOr;

using Mediator;

using Shared.Application.Authentication;

namespace Chat.Application.SharedChats.Queries.GetPublicSharedChat;

internal sealed class GetPublicSharedChatHandler(IUserContext userContext, IPublicSharedChatReader reader)
    : IQueryHandler<GetPublicSharedChatQuery, ErrorOr<PublicSharedChatReadModel>>
{
    public async ValueTask<ErrorOr<PublicSharedChatReadModel>> Handle(GetPublicSharedChatQuery query, CancellationToken cancellationToken)
    {
        ErrorOr<UserId> userIdResult = UserId.Create(userContext.UserId);
        ErrorOr<SharedChatId> sharedChatIdResult = SharedChatId.Create(query.SharedChatId);

        List<Error> errors = [];

        if (userIdResult.IsError)
        {
            errors.AddRange(userIdResult.Errors);
        }

        if (sharedChatIdResult.IsError)
        {
            errors.AddRange(sharedChatIdResult.Errors);
        }

        if (errors.Count > 0)
        {
            return errors;
        }

        SharedChatId sharedChatId = sharedChatIdResult.Value;

        PublicSharedChatReadModel? sharedChat = await reader.GetAsync(sharedChatId, cancellationToken);

        if (sharedChat is null)
        {
            return SharedChatOperationErrors.SharedChatNotFound(sharedChatId);
        }

        return sharedChat;
    }
}