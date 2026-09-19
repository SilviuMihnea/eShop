namespace eShop.Catalog.API.Services;

/// <summary>
/// Gives back stock held for orders that never completed.
///
/// Reserving is the only step of the order saga with no counterpart when nothing further happens:
/// payment commits a hold and cancellation releases it, but an order that simply stalls would hold
/// its units forever. This sweep is that missing counterpart, and it follows the same shape as
/// OrderProcessor's GracePeriodManagerService — poll on an interval, act on what it finds.
/// </summary>
public sealed class ReservationExpiryService(
    IServiceScopeFactory scopeFactory,
    IOptions<InventoryOptions> options,
    ILogger<ReservationExpiryService> logger) : BackgroundService
{
    private readonly InventoryOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var delay = TimeSpan.FromSeconds(Math.Max(1, _options.ExpiryCheckSeconds));

        logger.LogInformation(
            "Reservation expiry sweep starting, running every {Delay} for holds older than {Timeout} minute(s)",
            delay, _options.ReservationTimeoutMinutes);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ReleaseExpiredReservationsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // A failed sweep must not take the service down with it; the next tick retries.
                logger.LogError(ex, "Error releasing expired reservations");
            }

            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        logger.LogInformation("Reservation expiry sweep stopping");
    }

    /// <summary>
    /// Runs one sweep. Each order is settled in its own scope so that a conflict on one order
    /// cannot disturb the others, the same way the event bus gives every message its own scope.
    /// </summary>
    internal async Task ReleaseExpiredReservationsAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<int> orderIds;

        using (var scope = scopeFactory.CreateScope())
        {
            var reservationService = scope.ServiceProvider.GetRequiredService<IInventoryReservationService>();
            orderIds = await reservationService.GetExpiredOrderIdsAsync(
                DateTime.UtcNow, Math.Max(1, _options.ExpirySweepBatchSize), cancellationToken);
        }

        if (orderIds.Count == 0)
        {
            return;
        }

        logger.LogInformation("Releasing lapsed stock reservations for {OrderCount} order(s)", orderIds.Count);

        foreach (var orderId in orderIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await ReleaseExpiredAsync(orderId, cancellationToken);
        }
    }

    private async Task ReleaseExpiredAsync(int orderId, CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var reservationService = scope.ServiceProvider.GetRequiredService<IInventoryReservationService>();
        var eventService = scope.ServiceProvider.GetRequiredService<ICatalogIntegrationEventService>();
        var retry = scope.ServiceProvider.GetRequiredService<StockConcurrencyRetry>();

        InventoryReservationReleasedIntegrationEvent? releasedEvent = null;
        InventoryReservationExpiredIntegrationEvent? expiredEvent = null;

        var settled = await retry.TryExecuteAsync("release expired reservations", orderId, async () =>
        {
            releasedEvent = null;
            expiredEvent = null;

            var releasedLines = await reservationService.ReleaseAsync(orderId, cancellationToken);

            if (releasedLines.Count == 0)
            {
                // Another sweep, or a cancellation, got there first.
                return;
            }

            var expiredAt = DateTime.UtcNow;

            releasedEvent = new InventoryReservationReleasedIntegrationEvent(
                orderId,
                [.. releasedLines.Select(line => new OrderStockItem(line.ProductId, line.Units))],
                expiredAt,
                ReservationReleaseReason.Expired);

            expiredEvent = new InventoryReservationExpiredIntegrationEvent(orderId, expiredAt);

            // Both announcements go in one transaction with the release. If the stock came back
            // but the expiry notice were lost, the order would sit waiting on a hold that no
            // longer exists and nothing would ever move it.
            await eventService.SaveEventsAndCatalogContextChangesAsync([releasedEvent, expiredEvent]);
        });

        if (!settled)
        {
            // The hold keeps its expiry, so the next sweep will try again.
            logger.LogWarning(
                "Could not release the lapsed reservation for order {OrderId}; it stays queued for the next sweep",
                orderId);

            return;
        }

        if (releasedEvent is null || expiredEvent is null)
        {
            return;
        }

        await eventService.PublishThroughEventBusAsync(releasedEvent);
        await eventService.PublishThroughEventBusAsync(expiredEvent);
    }
}
