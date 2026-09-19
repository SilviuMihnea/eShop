namespace eShop.Catalog.API.Services;

/// <summary>One order line's worth of stock to hold.</summary>
public readonly record struct ReservationRequest(int ProductId, int Units);

/// <summary>Whether a single requested line could be held.</summary>
public readonly record struct ReservationLineResult(int ProductId, bool Reserved);

/// <summary>A hold that has just been settled, either committed or released.</summary>
public readonly record struct SettledReservationLine(int ProductId, int Units);

/// <summary>
/// The result of trying to hold stock for a whole order. Reserving is all-or-nothing, so when
/// <paramref name="Success"/> is false nothing was held and <paramref name="Lines"/> says which
/// products were short.
/// </summary>
public sealed record ReservationOutcome(bool Success, IReadOnlyList<ReservationLineResult> Lines);

/// <summary>
/// Holds, commits and releases catalog stock on behalf of an order.
///
/// IMPORTANT: none of these methods call SaveChanges. They mutate the tracked
/// <see cref="CatalogContext"/> and leave persistence to the caller, so that a reservation and
/// the integration event announcing it can be committed in a single transaction via
/// <see cref="ICatalogIntegrationEventService.SaveEventAndCatalogContextChangesAsync"/>. This is
/// the same division of labour the catalog API's update path already uses.
///
/// Every method is safe to call more than once for the same order, because the event bus
/// delivers at least once.
/// </summary>
public interface IInventoryReservationService
{
    /// <summary>
    /// Holds stock for every line of an order, or none of it. Lines are aggregated by product,
    /// and a product that does not exist counts as short rather than being skipped.
    /// </summary>
    /// <returns>
    /// The outcome. If the order already has reservations the call is a no-op reporting success,
    /// since the hold was taken by an earlier delivery of the same event.
    /// </returns>
    Task<ReservationOutcome> ReserveAsync(
        int orderId,
        IReadOnlyCollection<ReservationRequest> lines,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Turns the order's held stock into sold stock: physical stock is decremented and the
    /// reservations are marked committed.
    /// </summary>
    /// <returns>
    /// What was committed. Empty when there was nothing left to commit, which is how a
    /// redelivered payment event is distinguished from a real one.
    /// </returns>
    Task<IReadOnlyList<SettledReservationLine>> CommitAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gives the order's held stock back without touching physical stock.
    /// </summary>
    /// <returns>What was released. Empty when there was nothing left to release.</returns>
    Task<IReadOnlyList<SettledReservationLine>> ReleaseAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds orders still holding stock whose hold has lapsed, oldest order first.
    /// </summary>
    /// <param name="asOf">The moment to judge expiry against.</param>
    /// <param name="maxOrders">
    /// How many orders to return at most, so one sweep of a large backlog cannot monopolise the
    /// database.
    /// </param>
    Task<IReadOnlyList<int>> GetExpiredOrderIdsAsync(
        DateTime asOf,
        int maxOrders,
        CancellationToken cancellationToken = default);
}
