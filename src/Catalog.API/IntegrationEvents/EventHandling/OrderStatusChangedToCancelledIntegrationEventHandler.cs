using eShop.Catalog.API.Services;

namespace eShop.Catalog.API.IntegrationEvents.EventHandling;

public class OrderStatusChangedToCancelledIntegrationEventHandler(
    IInventoryReservationService reservationService,
    ICatalogIntegrationEventService catalogIntegrationEventService,
    StockConcurrencyRetry retry,
    ILogger<OrderStatusChangedToCancelledIntegrationEventHandler> logger) :
    IIntegrationEventHandler<OrderStatusChangedToCancelledIntegrationEvent>
{
    public async Task Handle(OrderStatusChangedToCancelledIntegrationEvent @event)
    {
        logger.LogInformation("Handling integration event: {IntegrationEventId} - ({@IntegrationEvent})", @event.Id, @event);

        // Releasing only ever affects holds that are still outstanding, so a cancellation that
        // arrives after payment cannot claw back stock that has already been sold.
        InventoryReservationReleasedIntegrationEvent? releasedEvent = null;

        var settled = await retry.TryExecuteAsync("release reservations", @event.OrderId, async () =>
        {
            releasedEvent = null;

            var releasedLines = await reservationService.ReleaseAsync(@event.OrderId);

            if (releasedLines.Count == 0)
            {
                return;
            }

            releasedEvent = new InventoryReservationReleasedIntegrationEvent(
                @event.OrderId,
                [.. releasedLines.Select(line => new OrderStockItem(line.ProductId, line.Units))],
                DateTime.UtcNow,
                ReservationReleaseReason.Cancelled);

            await catalogIntegrationEventService.SaveEventAndCatalogContextChangesAsync(releasedEvent);
        });

        if (!settled)
        {
            // Unlike committing, failing to release has a safety net: the hold keeps its expiry,
            // so the reservation sweep will give the units back later. Nothing is lost, the stock
            // is just unavailable for longer than it should be.
            logger.LogWarning(
                "Could not release the stock held by cancelled order {OrderId}; the hold stands until it expires",
                @event.OrderId);

            return;
        }

        if (releasedEvent is null)
        {
            // The common case for an order cancelled because its stock was rejected: reserving is
            // all-or-nothing, so a rejected order never held anything to give back.
            logger.LogInformation(
                "Cancelled order {OrderId} had no outstanding reservations to release", @event.OrderId);

            return;
        }

        await catalogIntegrationEventService.PublishThroughEventBusAsync(releasedEvent);
    }
}
