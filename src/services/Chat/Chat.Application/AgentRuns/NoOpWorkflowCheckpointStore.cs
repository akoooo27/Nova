using System.Text.Json;

using Chat.Application.Abstractions.AgentRuns;

namespace Chat.Application.AgentRuns;

/// <summary>
/// Deliberate no-op (spec decision 2, the NoOpMemoryRetriever pattern): PR #2 runs restart
/// from scratch on redelivery. Do NOT implement persistence here — PR #3 replaces this
/// registration with the Postgres store and the resume path.
/// </summary>
public sealed class NoOpWorkflowCheckpointStore : IWorkflowCheckpointStore
{
    public Task SaveAsync(Guid runId, string checkpointId, JsonElement state, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public Task<WorkflowCheckpoint?> GetLatestAsync(Guid runId, CancellationToken cancellationToken) =>
        Task.FromResult<WorkflowCheckpoint?>(null);

    public Task DeleteAllAsync(Guid runId, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}