namespace eShop.Ordering.API.Application.IntegrationEvents.EventHandling;

/// <summary>
/// When Catalog gives back a hold because the reservation window elapsed, the order must not
/// proceed to payment against stock it no longer has. Cancelling is the same command payment
/// failure uses, so the rest of the saga (notifications, a no-op Catalog release) stays intact.
/// </summary>
public class InventoryReservationExpiredIntegrationEventHandler(
    IMediator mediator,
    ILogger<InventoryReservationExpiredIntegrationEventHandler> logger) :
    IIntegrationEventHandler<InventoryReservationExpiredIntegrationEvent>
{
    public async Task Handle(InventoryReservationExpiredIntegrationEvent @event)
    {
        logger.LogInformation("Handling integration event: {IntegrationEventId} - ({@IntegrationEvent})", @event.Id, @event);

        var command = new CancelOrderCommand(@event.OrderId);

        logger.LogInformation(
            "Sending command: {CommandName} - {IdProperty}: {CommandId} ({@Command})",
            command.GetGenericTypeName(),
            nameof(command.OrderNumber),
            command.OrderNumber,
            command);

        await mediator.Send(command);
    }
}
