using eShop.Catalog.API.Services;

namespace eShop.Catalog.API.IntegrationEvents.EventHandling;

public class OrderStatusChangedToPaidIntegrationEventHandler(
    IInventoryReservationService reservationService,
    ICatalogIntegrationEventService catalogIntegrationEventService,
    StockConcurrencyRetry retry,
    ILogger<OrderStatusChangedToPaidIntegrationEventHandler> logger) :
    IIntegrationEventHandler<OrderStatusChangedToPaidIntegrationEvent>
{
    public async Task Handle(OrderStatusChangedToPaidIntegrationEvent @event)
    {
        logger.LogInformation("Handling integration event: {IntegrationEventId} - ({@IntegrationEvent})", @event.Id, @event);

        // The units were already taken out of circulation when the order was validated, so
        // paying for them turns the hold into a sale rather than decrementing stock afresh.
        // The event's OrderStockItems are deliberately ignored: what gets committed is what
        // was actually held, which is the only record that cannot disagree with the ledger.
        InventoryReservationCommittedIntegrationEvent? committedEvent = null;

        var settled = await retry.TryExecuteAsync("commit reservations", @event.OrderId, async () =>
        {
            committedEvent = null;

            var committedLines = await reservationService.CommitAsync(@event.OrderId);

            if (committedLines.Count == 0)
            {
                return;
            }

            committedEvent = new InventoryReservationCommittedIntegrationEvent(
                @event.OrderId,
                [.. committedLines.Select(line => new OrderStockItem(line.ProductId, line.Units))],
                DateTime.UtcNow);

            await catalogIntegrationEventService.SaveEventAndCatalogContextChangesAsync(committedEvent);
        });

        if (!settled)
        {
            // The order is paid but its stock is still only held, so availability is now
            // overstated by the held quantity. There is no safe automatic compensation: the
            // money has been taken, so the hold must not be released.
            logger.LogError(
                "Order {OrderId} was paid but its reserved stock could not be committed; the hold is still outstanding and needs reconciling",
                @event.OrderId);

            return;
        }

        if (committedEvent is null)
        {
            logger.LogWarning(
                "Order {OrderId} had no outstanding reservations to commit; the payment event was either redelivered or the hold was already released",
                @event.OrderId);

            return;
        }

        await catalogIntegrationEventService.PublishThroughEventBusAsync(committedEvent);
    }
}
