namespace TaskConsumer;

public sealed record ConsumedTaskEvent(
    Guid TaskId,
    string Title,
    DateTime CompletedAt,
    Priority Priority,
    DateTimeOffset ReceivedAt,
    ulong DeliveryTag);
