namespace Chat.Infrastructure.Agents;

public sealed class WorkflowExecutionException : Exception
{
    public string? ExecutorId { get; }

    public WorkflowExecutionException()
    {
    }

    public WorkflowExecutionException(string message)
        : base(message)
    {
    }

    public WorkflowExecutionException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }

    public WorkflowExecutionException(string message, Exception? innerException, string? executorId)
        : base(message, innerException)
    {
        ExecutorId = executorId;
    }
}