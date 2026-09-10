using PartnerIntegration.Core.Interfaces;
using System.Collections.Concurrent;

namespace PartnerIntegration.UnitTests.Helpers;

/// <summary>
/// In-memory IMessagePublisher for integration tests.
/// Captures published messages so tests can inspect what was enqueued
/// without requiring a real RabbitMQ instance.
/// </summary>
public sealed class InMemoryMessagePublisher : IMessagePublisher
{
    private readonly ConcurrentBag<(string QueueName, object Message)> _published = new();

    /// <summary>Snapshot of all messages published so far.</summary>
    public IReadOnlyList<(string QueueName, object Message)> Published =>
        _published.ToList().AsReadOnly();

    public Task PublishAsync<T>(string queueName, T message, CancellationToken ct = default)
    {
        _published.Add((queueName, message!));
        return Task.CompletedTask;
    }

    /// <summary>Resets the captured messages. Useful between tests sharing the same fixture.</summary>
    public void Clear() => _published.Clear();
}
