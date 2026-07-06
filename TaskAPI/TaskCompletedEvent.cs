namespace TaskAPI;

public sealed record TaskCompletedEvent(
    Guid TaskId,
    string Title,
    DateTime CompletedAt,
    Priority Priority);
