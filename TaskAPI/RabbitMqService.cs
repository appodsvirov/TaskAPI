using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;

namespace TaskAPI;

public interface IRabbitMqService
{
    void PublishTaskCompleted(TaskCompletedEvent taskCompletedEvent);
}

public sealed class RabbitMqService(
    IOptions<RabbitMqOptions> options,
    ILogger<RabbitMqService> logger) : BackgroundService, IRabbitMqService
{
    private const string ExchangeName = "task.events";
    private const string RoutingKey = "task.completed";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly RabbitMqOptions _options = options.Value;
    private readonly Channel<TaskCompletedEvent> _taskCompletedEvents = Channel.CreateUnbounded<TaskCompletedEvent>(
        new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });
    private readonly SemaphoreSlim _channelLock = new(1, 1);
    private IConnection? _connection;
    private IChannel? _channel;
    private volatile bool _isStopping;

    public void PublishTaskCompleted(TaskCompletedEvent taskCompletedEvent)
    {
        if (_isStopping || !_taskCompletedEvents.Writer.TryWrite(taskCompletedEvent))
        {
            logger.LogWarning(
                "RabbitMQ останавливается. Событие завершения задачи не поставлено в очередь. TaskId: {TaskId}",
                taskCompletedEvent.TaskId);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var taskCompletedEvent in _taskCompletedEvents.Reader.ReadAllAsync())
        {
            try
            {
                await PublishTaskCompletedAsync(taskCompletedEvent, CancellationToken.None);
            }
            catch (Exception exception)
            {
                logger.LogWarning(
                    exception,
                    "Не удалось опубликовать сообщение в RabbitMQ. Задача завершена, но событие не опубликовано. TaskId: {TaskId}",
                    taskCompletedEvent.TaskId);
            }
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _isStopping = true;
        _taskCompletedEvents.Writer.TryComplete();

        using var shutdownTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var linkedTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, shutdownTimeout.Token);

        try
        {
            await base.StopAsync(linkedTokenSource.Token);
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning("Истекло время ожидания завершения публикаций в RabbitMQ при остановке приложения.");
        }
        finally
        {
            await CloseRabbitMqAsync(CancellationToken.None);
        }
    }

    private async Task PublishTaskCompletedAsync(TaskCompletedEvent taskCompletedEvent, CancellationToken cancellationToken)
    {
        var channel = await EnsureChannelAsync(cancellationToken);
        var body = JsonSerializer.SerializeToUtf8Bytes(taskCompletedEvent, JsonOptions);
        var properties = new BasicProperties
        {
            ContentType = "application/json",
            DeliveryMode = DeliveryModes.Persistent
        };

        await _channelLock.WaitAsync(cancellationToken);
        try
        {
            await channel.BasicPublishAsync(
                exchange: ExchangeName,
                routingKey: RoutingKey,
                mandatory: false,
                basicProperties: properties,
                body: body,
                cancellationToken: cancellationToken);
        }
        finally
        {
            _channelLock.Release();
        }
    }

    private async Task<IChannel> EnsureChannelAsync(CancellationToken cancellationToken)
    {
        if (_channel?.IsOpen == true)
        {
            return _channel;
        }

        await _channelLock.WaitAsync(cancellationToken);
        try
        {
            if (_channel?.IsOpen == true)
            {
                return _channel;
            }

            await CloseRabbitMqAsync(cancellationToken);

            var factory = new ConnectionFactory
            {
                HostName = _options.Host,
                Port = _options.Port,
                UserName = _options.UserName,
                Password = _options.Password,
                VirtualHost = _options.VirtualHost,
                AutomaticRecoveryEnabled = true
            };

            _connection = await factory.CreateConnectionAsync(cancellationToken);
            _channel = await _connection.CreateChannelAsync(cancellationToken: cancellationToken);
            await _channel.ExchangeDeclareAsync(
                exchange: ExchangeName,
                type: ExchangeType.Topic,
                durable: true,
                autoDelete: false,
                cancellationToken: cancellationToken);

            return _channel;
        }
        finally
        {
            _channelLock.Release();
        }
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
                logger.LogDebug(exception, "Не удалось закрыть канал RabbitMQ.");
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
                logger.LogDebug(exception, "Не удалось закрыть соединение RabbitMQ.");
            }
            finally
            {
                _connection = null;
            }
        }
    }

    public override void Dispose()
    {
        _channelLock.Dispose();
        base.Dispose();
    }
}
