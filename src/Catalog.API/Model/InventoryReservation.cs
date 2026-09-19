namespace eShop.Catalog.API.Model;

/// <summary>
/// A hold placed on a catalog item's stock for one line of one order.
///
/// Reservations are the record of what inventory is promised but not yet sold. There is at
/// most one reservation per (order, product) pair, which is what makes the handlers that
/// create and settle them safe to run more than once: the event bus delivers at least once,
/// so every transition has to tolerate redelivery.
/// </summary>
public class InventoryReservation
{
    public int Id { get; set; }

    public int OrderId { get; set; }

    public int ProductId { get; set; }

    public int Units { get; set; }

    public ReservationStatus Status { get; set; }

    public DateTime ReservedAt { get; set; }

    /// <summary>
    /// When the hold stops being honoured. An order that has not been paid for by this point
    /// has its units given back so they are not promised to nobody indefinitely.
    /// </summary>
    public DateTime ExpiresAt { get; set; }

    public DateTime? CommittedAt { get; set; }

    public DateTime? ReleasedAt { get; set; }

    public InventoryReservation(int orderId, int productId, int units, DateTime reservedAt, DateTime expiresAt)
    {
        if (units <= 0)
        {
            throw new CatalogDomainException($"Reservation units must be greater than zero, but was {units}");
        }

        if (expiresAt <= reservedAt)
        {
            throw new CatalogDomainException("A reservation cannot expire at or before the moment it is taken");
        }

        OrderId = orderId;
        ProductId = productId;
        Units = units;
        Status = ReservationStatus.Reserved;
        ReservedAt = reservedAt;
        ExpiresAt = expiresAt;
    }

    /// <summary>
    /// Marks the hold as sold. Only the transition out of <see cref="ReservationStatus.Reserved"/>
    /// is legal, so a redelivered payment event cannot commit the same units twice.
    /// </summary>
    public void Commit(DateTime committedAt)
    {
        if (Status != ReservationStatus.Reserved)
        {
            throw new CatalogDomainException(
                $"Reservation {Id} for order {OrderId} cannot be committed because it is {Status}");
        }

        Status = ReservationStatus.Committed;
        CommittedAt = committedAt;
    }

    /// <summary>
    /// Gives the hold back. Only legal from <see cref="ReservationStatus.Reserved"/>, so a
    /// cancellation that arrives after payment cannot claw back committed stock.
    /// </summary>
    public void Release(DateTime releasedAt)
    {
        if (Status != ReservationStatus.Reserved)
        {
            throw new CatalogDomainException(
                $"Reservation {Id} for order {OrderId} cannot be released because it is {Status}");
        }

        Status = ReservationStatus.Released;
        ReleasedAt = releasedAt;
    }

    public bool IsExpired(DateTime asOf) => Status == ReservationStatus.Reserved && ExpiresAt <= asOf;
}
