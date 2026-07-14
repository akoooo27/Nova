using ErrorOr;

using Mediator;

namespace Chat.Application.AgentRuns.Queries.GetAgentRun;

public sealed record GetAgentRunQuery(Guid ChatId, Guid MessageId) : IQuery<ErrorOr<AgentRunDetailResult>>;
