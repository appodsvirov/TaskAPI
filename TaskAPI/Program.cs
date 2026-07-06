using Microsoft.EntityFrameworkCore;
using System.Text.Json.Serialization;
using TaskAPI;

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddDbContext<TasksDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services
    .AddOptions<RabbitMqOptions>()
    .Bind(builder.Configuration.GetSection("RabbitMq"));
builder.Services.AddSingleton<RabbitMqService>();
builder.Services.AddSingleton<IRabbitMqService>(provider => provider.GetRequiredService<RabbitMqService>());
builder.Services.AddHostedService(provider => provider.GetRequiredService<RabbitMqService>());

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

await using (var scope = app.Services.CreateAsyncScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<TasksDbContext>();
    await dbContext.Database.EnsureCreatedAsync();
}

app.MapGet("/", () => Results.Redirect("/swagger"));
app.MapTaskEndpoints();

app.Run();

public partial class Program;
