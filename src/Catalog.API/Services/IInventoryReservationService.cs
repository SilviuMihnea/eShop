namespace eShop.Catalog.API.Services;

/// <summary>One order line's worth of stock to hold.</summary>
public readonly record struct ReservationRequest(int ProductId, int Units);

/// <summary>Whether a single requested line could be held.</summary>
public readonly record struct ReservationLineResult(int ProductId, bool Reserved);

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
    /// <returns>The number of reservations committed. Zero when there was nothing left to commit.</returns>
    Task<int> CommitAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gives the order's held stock back without touching physical stock.
    /// </summary>
    /// <returns>The number of reservations released. Zero when there was nothing left to release.</returns>
    Task<int> ReleaseAsync(int orderId, CancellationToken cancellationToken = default);
}
