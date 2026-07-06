using System.ComponentModel.DataAnnotations;

namespace TaskAPI;

public sealed class CreateTaskRequest
{
    [Required(AllowEmptyStrings = false)]
    [MaxLength(200)]
    [RegularExpression(@".*\S.*", ErrorMessage = "Название задачи не должно быть пустым.")]
    public string? Title { get; init; }

    public Priority Priority { get; init; } = Priority.Medium;
}
