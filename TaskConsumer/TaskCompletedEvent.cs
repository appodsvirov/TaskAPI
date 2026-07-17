namespace TaskConsumer;

public sealed record TaskCompletedEvent(
    Guid TaskId,
    string Title,
    DateTime CompletedAt,
    Priority Priority);

public enum Priority
{
    Low,
    Medium,
    High
}
