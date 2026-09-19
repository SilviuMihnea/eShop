namespace eShop.Catalog.API.Services;

/// <summary>
/// Runs a unit of work that reads stock and writes it back, retrying when a concurrent order
/// changed the same rows in between.
///
/// Stock carries an optimistic concurrency token, so the losing writer gets a
/// <see cref="DbUpdateConcurrencyException"/> and commits nothing. Retrying matters more here
/// than it normally would: the event bus acknowledges a message even when its handler throws,
/// with no dead-letter exchange, so an unhandled conflict abandons the order silently instead
/// of being redelivered.
/// </summary>
public sealed class StockConcurrencyRetry(
    CatalogContext catalogContext,
    IOptions<InventoryOptions> options,
    ILogger<StockConcurrencyRetry> logger)
{
    /// <summary>
    /// Runs <paramref name="work"/> until it commits or the configured attempts are used up.
    /// Callers decide what to do when it does not commit, because the safe fallback differs:
    /// an order awaiting validation can be rejected, but one that has already been paid cannot.
    /// </summary>
    /// <returns>True when the work committed, false when every attempt hit a conflict.</returns>
    public async Task<bool> TryExecuteAsync(string operation, int orderId, Func<Task> work)
    {
        var maxAttempts = Math.Max(1, options.Value.MaxReservationAttempts);

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await work();
                return true;
            }
            catch (DbUpdateConcurrencyException ex)
            {
                // Nothing was committed, so the next attempt has to start from fresh state
                // rather than from the entities this one already modified.
                catalogContext.ChangeTracker.Clear();

                if (attempt >= maxAttempts)
                {
                    logger.LogError(ex,
                        "Giving up on {Operation} for order {OrderId} after {MaxAttempts} attempt(s)",
                        operation, orderId, maxAttempts);

                    return false;
                }

                logger.LogWarning(ex,
                    "Concurrent stock change during {Operation} for order {OrderId}; retrying ({Attempt} of {MaxAttempts})",
                    operation, orderId, attempt, maxAttempts);
            }
        }
    }
}
