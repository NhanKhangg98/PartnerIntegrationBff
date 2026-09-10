using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PartnerIntegration.Core.Interfaces;
using RabbitMQ.Client;

namespace PartnerIntegration.Infrastructure.Messaging;

public class RabbitMqPublisher : IMessagePublisher, IAsyncDisposable
{
    private readonly ConnectionFactory _factory;
    private readonly RabbitMqOptions _options;
    private readonly ILogger<RabbitMqPublisher> _logger;
    private IConnection? _connection;
    private IChannel? _channel;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    public RabbitMqPublisher(IOptions<RabbitMqOptions> options, ILogger<RabbitMqPublisher> logger)
    {
        _logger = logger;
        _options = options.Value;

        _factory = new ConnectionFactory
        {
            HostName = _options.Host,
            Port = _options.Port,
            UserName = _options.Username,
            Password = _options.Password
        };
    }

    private async Task EnsureChannelCreatedAsync(CancellationToken ct)
    {
        // Check both null and IsOpen — a non-null but closed channel means the broker
        // restarted or the connection was dropped; we need to reconnect.
        if (_channel is { IsOpen: true }) return;

        await _initLock.WaitAsync(ct);
        try
        {
            if (_channel is { IsOpen: true }) return;

            _connection = await _factory.CreateConnectionAsync(ct);
            _channel = await _connection.CreateChannelAsync(cancellationToken: ct);

            await _channel.QueueDeclareAsync(
                queue: _options.QueueName,
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: null,
                cancellationToken: ct
            );
        }
        finally
        {
            _initLock.Release();
        }
    }

    public async Task PublishAsync<T>(string queueName, T message, CancellationToken ct = default)
    {
        await EnsureChannelCreatedAsync(ct);

        var targetQueue = string.IsNullOrWhiteSpace(queueName) ? _options.QueueName : queueName;
        var json = JsonSerializer.Serialize(message);
        var body = Encoding.UTF8.GetBytes(json);

        var properties = new BasicProperties
        {
            DeliveryMode = DeliveryModes.Persistent
        };

        await _channel!.BasicPublishAsync(
            exchange: string.Empty,
            routingKey: targetQueue,
            mandatory: false,
            basicProperties: properties,
            body: body,
            cancellationToken: ct
        );

        _logger.LogInformation("Successfully published message to queue: {QueueName}", targetQueue);
    }

    public async ValueTask DisposeAsync()
    {
        if (_channel is not null)
        {
            await _channel.CloseAsync();
            await _channel.DisposeAsync();
        }

        if (_connection is not null)
        {
            await _connection.CloseAsync();
            await _connection.DisposeAsync();
        }

        _initLock.Dispose();
    }
}