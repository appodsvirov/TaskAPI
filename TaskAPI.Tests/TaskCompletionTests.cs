using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using TaskAPI;

namespace TaskAPI.Tests;

public sealed class TaskCompletionTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    [Fact]
    public async Task CompleteTask_PublishesRabbitMqMessage()
    {
        await using var factory = new TaskApiFactory();
        var client = factory.CreateClient();

        var createResponse = await client.PostAsJsonAsync("/tasks", new CreateTaskRequest
        {
            Title = "Buy milk",
            Priority = Priority.High
        }, JsonOptions);
        createResponse.EnsureSuccessStatusCode();

        var createdTask = await createResponse.Content.ReadFromJsonAsync<TaskItem>(JsonOptions);
        Assert.NotNull(createdTask);

        var completeResponse = await client.PutAsync($"/tasks/{createdTask.Id}/complete", content: null);
        completeResponse.EnsureSuccessStatusCode();

        var publishedEvent = await factory.RabbitMqService.WaitForPublishedEventAsync();

        Assert.Equal(createdTask.Id, publishedEvent.TaskId);
        Assert.Equal("Buy milk", publishedEvent.Title);
        Assert.Equal(Priority.High, publishedEvent.Priority);
        Assert.True(publishedEvent.CompletedAt <= DateTime.UtcNow);
        Assert.Equal(DateTimeKind.Utc, publishedEvent.CompletedAt.Kind);
    }

    private sealed class TaskApiFactory : WebApplicationFactory<Program>
    {
        private readonly string _databaseName = $"tasks-{Guid.NewGuid()}";

        public TestRabbitMqService RabbitMqService { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureServices(services =>
            {
                var dbContextServices = services
                    .Where(service =>
                    service.ServiceType == typeof(TasksDbContext) ||
                    service.ServiceType == typeof(DbContextOptions) ||
                    service.ServiceType == typeof(DbContextOptions<TasksDbContext>) ||
                    service.ServiceType.FullName?.Contains(nameof(TasksDbContext), StringComparison.Ordinal) == true)
                    .ToList();

                foreach (var service in dbContextServices)
                {
                    services.Remove(service);
                }

                services.RemoveAll<RabbitMqService>();
                services.RemoveAll<IHostedService>();
                services.RemoveAll<IRabbitMqService>();

                services.AddDbContext<TasksDbContext>(options =>
                    options.UseInMemoryDatabase(_databaseName));
                services.AddSingleton<IRabbitMqService>(RabbitMqService);
            });
        }
    }

    private sealed class TestRabbitMqService : IRabbitMqService
    {
        private readonly TaskCompletionSource<TaskCompletedEvent> _publishedEvent = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public void PublishTaskCompleted(TaskCompletedEvent taskCompletedEvent)
        {
            _publishedEvent.TrySetResult(taskCompletedEvent);
        }

        public async Task<TaskCompletedEvent> WaitForPublishedEventAsync()
        {
            var timeout = Task.Delay(TimeSpan.FromSeconds(3));
            var completedTask = await Task.WhenAny(_publishedEvent.Task, timeout);

            if (completedTask == timeout)
            {
                throw new TimeoutException("RabbitMQ сообщение не было опубликовано.");
            }

            return await _publishedEvent.Task;
        }
    }
}
