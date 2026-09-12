namespace Puluj.Infrastructure.Messaging;

public interface INotifyPublisher
{
    Task PublishAsync(PulujEvent evt, CancellationToken ct = default);
}
