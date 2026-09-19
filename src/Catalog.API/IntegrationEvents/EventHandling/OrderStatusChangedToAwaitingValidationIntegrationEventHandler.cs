using eShop.Catalog.API.Services;

namespace eShop.Catalog.API.IntegrationEvents.EventHandling;

public class OrderStatusChangedToAwaitingValidationIntegrationEventHandler(
    IInventoryReservationService reservationService,
    ICatalogIntegrationEventService catalogIntegrationEventService,
    StockConcurrencyRetry retry,
    ILogger<OrderStatusChangedToAwaitingValidationIntegrationEventHandler> logger) :
    IIntegrationEventHandler<OrderStatusChangedToAwaitingValidationIntegrationEvent>
{
    public async Task Handle(OrderStatusChangedToAwaitingValidationIntegrationEvent @event)
    {
        logger.LogInformation("Handling integration event: {IntegrationEventId} - ({@IntegrationEvent})", @event.Id, @event);

        var lines = @event.OrderStockItems
            .Select(orderStockItem => new ReservationRequest(orderStockItem.ProductId, orderStockItem.Units))
            .ToList();

        IntegrationEvent? resultEvent = null;

        var reserved = await retry.TryExecuteAsync("reserve stock", @event.OrderId, async () =>
        {
            resultEvent = null;

            var outcome = await reservationService.ReserveAsync(@event.OrderId, lines);

            resultEvent = outcome.Success
                ? new OrderStockConfirmedIntegrationEvent(@event.OrderId)
                : new OrderStockRejectedIntegrationEvent(
                    @event.OrderId,
                    [.. outcome.Lines.Select(line => new ConfirmedOrderStockItem(line.ProductId, line.Reserved))]);

            // Commits the held units, the reservation rows and the outbox entry together, so
            // stock is never held without the order being told, or announced without being held.
            await catalogIntegrationEventService.SaveEventAndCatalogContextChangesAsync(resultEvent);
        });

        if (!reserved)
        {
            // The order has to be told something. The event bus acknowledges messages even when
            // a handler throws, so staying silent would leave the order awaiting validation
            // forever. Rejecting is the safe direction: no units were secured.
            logger.LogError(
                "Could not secure stock for order {OrderId} under contention; rejecting it rather than risking an oversell",
                @event.OrderId);

            var rejection = new OrderStockRejectedIntegrationEvent(
                @event.OrderId,
                [.. lines.Select(line => new ConfirmedOrderStockItem(line.ProductId, false))]);

            await catalogIntegrationEventService.SaveEventAndCatalogContextChangesAsync(rejection);
            await catalogIntegrationEventService.PublishThroughEventBusAsync(rejection);

            return;
        }

        // Non-null whenever the work committed.
        await catalogIntegrationEventService.PublishThroughEventBusAsync(resultEvent!);
    }
}
