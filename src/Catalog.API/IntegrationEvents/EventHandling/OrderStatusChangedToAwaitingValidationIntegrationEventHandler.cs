using eShop.Catalog.API.Services;

namespace eShop.Catalog.API.IntegrationEvents.EventHandling;

public class OrderStatusChangedToAwaitingValidationIntegrationEventHandler(
    CatalogContext catalogContext,
    IInventoryReservationService reservationService,
    ICatalogIntegrationEventService catalogIntegrationEventService,
    IOptions<InventoryOptions> options,
    ILogger<OrderStatusChangedToAwaitingValidationIntegrationEventHandler> logger) :
    IIntegrationEventHandler<OrderStatusChangedToAwaitingValidationIntegrationEvent>
{
    public async Task Handle(OrderStatusChangedToAwaitingValidationIntegrationEvent @event)
    {
        logger.LogInformation("Handling integration event: {IntegrationEventId} - ({@IntegrationEvent})", @event.Id, @event);

        var lines = @event.OrderStockItems
            .Select(orderStockItem => new ReservationRequest(orderStockItem.ProductId, orderStockItem.Units))
            .ToList();

        var maxAttempts = Math.Max(1, options.Value.MaxReservationAttempts);

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                var outcome = await reservationService.ReserveAsync(@event.OrderId, lines);

                IntegrationEvent resultEvent = outcome.Success
                    ? new OrderStockConfirmedIntegrationEvent(@event.OrderId)
                    : new OrderStockRejectedIntegrationEvent(
                        @event.OrderId,
                        [.. outcome.Lines.Select(line => new ConfirmedOrderStockItem(line.ProductId, line.Reserved))]);

                // Commits the held units, the reservation rows and the outbox entry together, so
                // stock is never held without the order being told, or released without the hold.
                await catalogIntegrationEventService.SaveEventAndCatalogContextChangesAsync(resultEvent);
                await catalogIntegrationEventService.PublishThroughEventBusAsync(resultEvent);

                return;
            }
            catch (DbUpdateConcurrencyException ex)
            {
                // Another order changed the same product's stock between our read and our write.
                // Nothing was committed, so start over from fresh state.
                catalogContext.ChangeTracker.Clear();

                if (attempt < maxAttempts)
                {
                    logger.LogWarning(ex,
                        "Concurrent stock change while reserving order {OrderId}; retrying ({Attempt} of {MaxAttempts})",
                        @event.OrderId, attempt, maxAttempts);
                    continue;
                }

                logger.LogError(ex,
                    "Giving up reserving order {OrderId} after {MaxAttempts} attempt(s); rejecting the order rather than risking an oversell",
                    @event.OrderId, maxAttempts);

                await RejectAsync(@event, lines);
                return;
            }
        }
    }

    /// <summary>
    /// Rejects the order when the hold could not be secured under contention. The order has to be
    /// told something: the event bus acknowledges messages even when a handler throws, so letting
    /// the exception escape would leave the order waiting for validation forever.
    /// </summary>
    private async Task RejectAsync(
        OrderStatusChangedToAwaitingValidationIntegrationEvent @event,
        List<ReservationRequest> lines)
    {
        // We could not secure any of the lines, so none of them is reported as having stock.
        var rejection = new OrderStockRejectedIntegrationEvent(
            @event.OrderId,
            [.. lines.Select(line => new ConfirmedOrderStockItem(line.ProductId, false))]);

        await catalogIntegrationEventService.SaveEventAndCatalogContextChangesAsync(rejection);
        await catalogIntegrationEventService.PublishThroughEventBusAsync(rejection);
    }
}
