using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TaskConsumer;

public sealed class TaskCompletedConsumer(
    IOptions<RabbitMqOptions> options,
    TaskEventStore taskEventStore,
    ILogger<TaskCompletedConsumer> logger) : BackgroundService
{
    private const string ExchangeName = "task.events";
    private const string QueueName = "task.completed.ui";
    private const string RoutingKey = "task.completed";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly RabbitMqOptions _options = options.Value;
    private IConnection? _connection;
    private IChannel? _channel;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await StartConsumingAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "RabbitMQ consumer stopped. Reconnecting in 5 seconds.");
                await CloseRabbitMqAsync(CancellationToken.None);
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    private async Task StartConsumingAsync(CancellationToken stoppingToken)
    {
        var factory = new ConnectionFactory
        {
            HostName = _options.Host,
            Port = _options.Port,
            UserName = _options.UserName,
            Password = _options.Password,
            VirtualHost = _options.VirtualHost,
            AutomaticRecoveryEnabled = true
        };

        _connection = await factory.CreateConnectionAsync(stoppingToken);
        _channel = await _connection.CreateChannelAsync(cancellationToken: stoppingToken);

        await _channel.ExchangeDeclareAsync(
            exchange: ExchangeName,
            type: ExchangeType.Topic,
            durable: true,
            autoDelete: false,
            cancellationToken: stoppingToken);

        await _channel.QueueDeclareAsync(
            queue: QueueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            cancellationToken: stoppingToken);

        await _channel.QueueBindAsync(
            queue: QueueName,
            exchange: ExchangeName,
            routingKey: RoutingKey,
            cancellationToken: stoppingToken);

        await _channel.BasicQosAsync(
            prefetchSize: 0,
            prefetchCount: 10,
            global: false,
            cancellationToken: stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(_channel);

        consumer.ReceivedAsync += async (_, args) =>
        {
            try
            {
                var taskCompletedEvent = JsonSerializer.Deserialize<TaskCompletedEvent>(
                    args.Body.Span,
                    JsonOptions);

                if (taskCompletedEvent is null)
                {
                    logger.LogWarning("Received an empty task.completed message.");
                    await _channel.BasicNackAsync(args.DeliveryTag, multiple: false, requeue: false);
                    return;
                }

                taskEventStore.Add(new ConsumedTaskEvent(
                    taskCompletedEvent.TaskId,
                    taskCompletedEvent.Title,
                    taskCompletedEvent.CompletedAt,
                    taskCompletedEvent.Priority,
                    DateTimeOffset.UtcNow,
                    args.DeliveryTag));

                logger.LogInformation(
                    "Consumed task.completed event. TaskId: {TaskId}",
                    taskCompletedEvent.TaskId);

                await _channel.BasicAckAsync(args.DeliveryTag, multiple: false);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Failed to process task.completed message.");
                await _channel.BasicNackAsync(args.DeliveryTag, multiple: false, requeue: false);
            }
        };

        await _channel.BasicConsumeAsync(
            queue: QueueName,
            autoAck: false,
            consumer: consumer,
            cancellationToken: stoppingToken);

        await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await CloseRabbitMqAsync(cancellationToken);
        await base.StopAsync(cancellationToken);
    }

    private async Task CloseRabbitMqAsync(CancellationToken cancellationToken)
    {
        if (_channel is not null)
        {
            try
            {
                await _channel.CloseAsync(cancellationToken);
                await _channel.DisposeAsync();
            }
            catch (Exception exception)
            {
                logger.LogDebug(exception, "Failed to close RabbitMQ channel.");
            }
            finally
            {
                _channel = null;
            }
        }

        if (_connection is not null)
        {
            try
            {
                await _connection.CloseAsync(cancellationToken);
                await _connection.DisposeAsync();
            }
            catch (Exception exception)
            {
                logger.LogDebug(exception, "Failed to close RabbitMQ connection.");
            }
            finally
            {
                _connection = null;
            }
        }
    }
}
