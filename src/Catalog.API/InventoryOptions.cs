namespace eShop.Catalog.API;

public class InventoryOptions
{
    /// <summary>
    /// How long a reservation is honoured before it is eligible to be given back. An order
    /// that has not been paid for within this window has its held units released so they
    /// are not promised to an order that never completes.
    /// </summary>
    public int ReservationTimeoutMinutes { get; set; } = 15;

    /// <summary>
    /// How often expired reservations are swept up and released.
    /// </summary>
    public int ExpiryCheckSeconds { get; set; } = 60;

    /// <summary>
    /// How many distinct orders one sweep will release at most. Caps the work a single tick can
    /// do when a backlog of lapsed holds has built up.
    /// </summary>
    public int ExpirySweepBatchSize { get; set; } = 20;

    /// <summary>
    /// How many times reserving an order is retried when a concurrent order changes the same
    /// product's stock. Once these are used up the order is rejected rather than risking an
    /// oversell.
    /// </summary>
    public int MaxReservationAttempts { get; set; } = 3;
}
