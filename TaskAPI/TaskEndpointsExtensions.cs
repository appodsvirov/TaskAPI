using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

namespace TaskAPI;

public static class TaskEndpointsExtensions
{
    public static IEndpointRouteBuilder MapTaskEndpoints(this IEndpointRouteBuilder app)
    {
        var tasks = app.MapGroup("/tasks");

        tasks.MapPost("/", async (CreateTaskRequest request, TasksDbContext dbContext) =>
        {
            var validationResults = new List<ValidationResult>();
            var validationContext = new ValidationContext(request);

            if (!Validator.TryValidateObject(request, validationContext, validationResults, validateAllProperties: true) ||
                string.IsNullOrWhiteSpace(request.Title))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["title"] = ["не null, не пустой, максимум 200 символов"]
                });
            }

            if (!Enum.IsDefined(request.Priority))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["priority"] = ["Priority должен быть Low, Medium или High"]
                });
            }

            var task = new TaskItem
            {
                Id = Guid.NewGuid(),
                Title = request.Title.Trim(),
                IsCompleted = false,
                CreatedAt = DateTimeOffset.UtcNow,
                Priority = request.Priority
            };
            var taskEntity = task.ToEntity();

            dbContext.Tasks.Add(taskEntity);
            await dbContext.SaveChangesAsync();

            return Results.Created($"/tasks/{task.Id}", task);
        });

        tasks.MapGet("/", async (TasksDbContext dbContext) =>
            await dbContext.Tasks
                .AsNoTracking()
                .OrderBy(task => task.CreatedAt)
                .Select(task => task.ToModel())
                .ToListAsync());

        tasks.MapPut("/{id:guid}/complete", async (
            Guid id,
            TasksDbContext dbContext,
            IRabbitMqService publisher) =>
        {
            var task = await dbContext.Tasks.FindAsync(id);

            if (task is null)
            {
                return Results.NotFound();
            }

            if (task.IsCompleted)
            {
                return Results.Conflict(new { message = "Задача завершена" });
            }

            var completedAt = DateTimeOffset.UtcNow;
            task.IsCompleted = true;
            task.CompletedAt = completedAt;

            try
            {
                await dbContext.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                return Results.Conflict(new { message = "Задача была завершена другим запросом" });
            }

            publisher.PublishTaskCompleted(new TaskCompletedEvent(task.Id, task.Title, completedAt.UtcDateTime, task.Priority));

            return Results.Ok(task.ToModel());
        });

        tasks.MapDelete("/{id:guid}", async (Guid id, TasksDbContext dbContext) =>
        {
            var task = await dbContext.Tasks.FindAsync(id);

            if (task is null)
            {
                return Results.NotFound();
            }

            dbContext.Tasks.Remove(task);
            await dbContext.SaveChangesAsync();

            return Results.NoContent();
        });

        return app;
    }
}
