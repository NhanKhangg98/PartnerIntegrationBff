namespace PartnerIntegration.Core.Interfaces;

public interface IMessagePublisher
{
    Task PublishAsync<T>(string queueName, T message, CancellationToken ct = default);
}