namespace eShop.Catalog.API.Services;

public sealed class InventoryReservationService(
    CatalogContext catalogContext,
    IOptions<InventoryOptions> options,
    ILogger<InventoryReservationService> logger) : IInventoryReservationService
{
    private readonly InventoryOptions _options = options.Value;

    public async Task<ReservationOutcome> ReserveAsync(
        int orderId,
        IReadOnlyCollection<ReservationRequest> lines,
        CancellationToken cancellationToken = default)
    {
        // Order items are already unique per product, but aggregate defensively: only one
        // reservation per (order, product) can exist.
        var requested = lines
            .GroupBy(line => line.ProductId)
            .Select(group => new ReservationRequest(group.Key, group.Sum(line => line.Units)))
            .ToList();

        if (requested.Count == 0)
        {
            logger.LogInformation("Order {OrderId} has no lines to reserve", orderId);
            return new ReservationOutcome(true, []);
        }

        var alreadyHeld = await catalogContext.InventoryReservations
            .Where(reservation => reservation.OrderId == orderId)
            .CountAsync(cancellationToken);

        if (alreadyHeld > 0)
        {
            // An earlier delivery of the same event already dealt with this order. Reserving
            // again would either double-count the stock or violate the unique index.
            logger.LogInformation(
                "Order {OrderId} already has {ReservationCount} reservation(s); skipping reserve",
                orderId, alreadyHeld);

            return new ReservationOutcome(
                true,
                [.. requested.Select(line => new ReservationLineResult(line.ProductId, true))]);
        }

        var productIds = requested.Select(line => line.ProductId).ToList();
        var items = await catalogContext.CatalogItems
            .Where(item => productIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, cancellationToken);

        var results = new List<ReservationLineResult>(requested.Count);
        var canReserveEverything = true;

        foreach (var line in requested)
        {
            // A product that does not exist counts as short. The stock check this replaces
            // skipped unknown products, which let an order for a deleted item pass.
            var canReserveLine = items.TryGetValue(line.ProductId, out var item)
                && item.AvailableToPromise >= line.Units;

            canReserveEverything &= canReserveLine;
            results.Add(new ReservationLineResult(line.ProductId, canReserveLine));
        }

        if (!canReserveEverything)
        {
            logger.LogInformation(
                "Insufficient stock to reserve order {OrderId}; no units were held", orderId);

            return new ReservationOutcome(false, results);
        }

        var reservedAt = DateTime.UtcNow;
        var expiresAt = reservedAt.AddMinutes(_options.ReservationTimeoutMinutes);

        foreach (var line in requested)
        {
            items[line.ProductId].Reserve(line.Units);
            catalogContext.InventoryReservations.Add(
                new InventoryReservation(orderId, line.ProductId, line.Units, reservedAt, expiresAt));
        }

        logger.LogInformation(
            "Reserved stock for order {OrderId} across {ProductCount} product(s), expiring at {ExpiresAt}",
            orderId, requested.Count, expiresAt);

        return new ReservationOutcome(true, results);
    }

    public Task<IReadOnlyList<SettledReservationLine>> CommitAsync(int orderId, CancellationToken cancellationToken = default)
        => SettleAsync(orderId, commit: true, cancellationToken);

    public Task<IReadOnlyList<SettledReservationLine>> ReleaseAsync(int orderId, CancellationToken cancellationToken = default)
        => SettleAsync(orderId, commit: false, cancellationToken);

    /// <summary>
    /// Commits or releases every hold still outstanding for an order. Reservations that have
    /// already been settled are not loaded, which is what makes a redelivered payment or
    /// cancellation event a no-op.
    /// </summary>
    private async Task<IReadOnlyList<SettledReservationLine>> SettleAsync(int orderId, bool commit, CancellationToken cancellationToken)
    {
        var outstanding = await catalogContext.InventoryReservations
            .Where(reservation => reservation.OrderId == orderId
                && reservation.Status == ReservationStatus.Reserved)
            .ToListAsync(cancellationToken);

        if (outstanding.Count == 0)
        {
            logger.LogInformation(
                "Order {OrderId} has no outstanding reservations to settle", orderId);
            return [];
        }

        var productIds = outstanding.Select(reservation => reservation.ProductId).ToList();
        var items = await catalogContext.CatalogItems
            .Where(item => productIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, cancellationToken);

        var settledAt = DateTime.UtcNow;

        foreach (var reservation in outstanding)
        {
            if (items.TryGetValue(reservation.ProductId, out var item))
            {
                if (commit)
                {
                    item.CommitReservation(reservation.Units);
                }
                else
                {
                    item.ReleaseReservation(reservation.Units);
                }
            }
            else
            {
                // The product row is gone, so there is no stock left to adjust. Settle the
                // reservation regardless so it stops counting as outstanding.
                logger.LogWarning(
                    "Product {ProductId} held by order {OrderId} no longer exists; settling the reservation without adjusting stock",
                    reservation.ProductId, orderId);
            }

            if (commit)
            {
                reservation.Commit(settledAt);
            }
            else
            {
                reservation.Release(settledAt);
            }
        }

        logger.LogInformation(
            "{Action} {ReservationCount} reservation(s) for order {OrderId}",
            commit ? "Committed" : "Released", outstanding.Count, orderId);

        return [.. outstanding.Select(reservation =>
            new SettledReservationLine(reservation.ProductId, reservation.Units))];
    }
}
