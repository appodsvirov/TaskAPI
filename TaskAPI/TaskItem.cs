namespace TaskAPI;

public class TaskItem
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public bool IsCompleted { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public Priority Priority { get; set; } = Priority.Medium;
}

public class TaskItemEntity
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public bool IsCompleted { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public Priority Priority { get; set; } = Priority.Medium;
    public uint RowVersion { get; set; }
}

public enum Priority
{
    Low, Medium, High
}


public static class TaskItemMappingExtensions
{
    public static TaskItem ToModel(this TaskItemEntity entity) => new()
    {
        Id = entity.Id,
        Title = entity.Title,
        IsCompleted = entity.IsCompleted,
        CreatedAt = entity.CreatedAt,
        CompletedAt = entity.CompletedAt,
        Priority = entity.Priority
    };

    public static TaskItemEntity ToEntity(this TaskItem model) => new()
    {
        Id = model.Id,
        Title = model.Title,
        IsCompleted = model.IsCompleted,
        CreatedAt = model.CreatedAt,
        CompletedAt = model.CompletedAt,
        Priority = model.Priority
    };
}
