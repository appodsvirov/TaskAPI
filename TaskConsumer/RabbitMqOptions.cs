namespace TaskConsumer;

public sealed class RabbitMqOptions
{
    public required string Host { get; init; }

    public int Port { get; init; }

    public required string UserName { get; init; }

    public required string Password { get; init; }

    public required string VirtualHost { get; init; }
}
