using System.Text.Json;

namespace Chat.Application.Abstractions.AgentRuns;


public sealed record WorkflowCheckpoint(string CheckpointId, JsonElement State);

public interface IWorkflowCheckpointStore
{
    Task SaveAsync(Guid runId, string checkpointId, JsonElement state, CancellationToken cancellationToken);

    Task<WorkflowCheckpoint?> GetLatestAsync(Guid runId, CancellationToken cancellationToken);

    Task DeleteAllAsync(Guid runId, CancellationToken cancellationToken);
}