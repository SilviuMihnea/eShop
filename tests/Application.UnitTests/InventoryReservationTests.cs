using eShop.Catalog.API.Infrastructure.Exceptions;
using eShop.Catalog.API.Model;

namespace eShop.Application.UnitTests;

[TestClass]
public class CatalogItemReservationTests
{
    private static CatalogItem ItemWithStock(int availableStock) =>
        new("Test Item") { Id = 1, AvailableStock = availableStock, MaxStockThreshold = 200 };

    [TestMethod]
    public void ReserveHoldsUnitsWithoutTouchingPhysicalStock()
    {
        var item = ItemWithStock(10);

        item.Reserve(3);

        Assert.AreEqual(10, item.AvailableStock);
        Assert.AreEqual(3, item.ReservedStock);
        Assert.AreEqual(7, item.AvailableToPromise);
    }

    [TestMethod]
    public void ReserveAccumulatesAcrossOrders()
    {
        var item = ItemWithStock(10);

        item.Reserve(3);
        item.Reserve(4);

        Assert.AreEqual(7, item.ReservedStock);
        Assert.AreEqual(3, item.AvailableToPromise);
    }

    [TestMethod]
    public void ReserveCanConsumeAllRemainingStock()
    {
        var item = ItemWithStock(5);

        item.Reserve(5);

        Assert.AreEqual(0, item.AvailableToPromise);
    }

    [TestMethod]
    public void ReserveBeyondAvailableToPromiseThrows()
    {
        var item = ItemWithStock(5);
        item.Reserve(4);

        // Only one unit is left to promise even though physical stock still reads five.
        Assert.ThrowsExactly<CatalogDomainException>(() => item.Reserve(2));

        Assert.AreEqual(5, item.AvailableStock);
        Assert.AreEqual(4, item.ReservedStock);
    }

    [TestMethod]
    public void ReserveRejectsNonPositiveUnits()
    {
        var item = ItemWithStock(5);

        Assert.ThrowsExactly<CatalogDomainException>(() => item.Reserve(0));
        Assert.ThrowsExactly<CatalogDomainException>(() => item.Reserve(-1));
        Assert.AreEqual(0, item.ReservedStock);
    }

    [TestMethod]
    public void CommitReservationTakesUnitsOutOfPhysicalStock()
    {
        var item = ItemWithStock(10);
        item.Reserve(4);

        item.CommitReservation(4);

        Assert.AreEqual(6, item.AvailableStock);
        Assert.AreEqual(0, item.ReservedStock);
        Assert.AreEqual(6, item.AvailableToPromise);
    }

    [TestMethod]
    public void CommitReservationLeavesOtherHoldsInPlace()
    {
        var item = ItemWithStock(10);
        item.Reserve(3);
        item.Reserve(2);

        item.CommitReservation(3);

        Assert.AreEqual(7, item.AvailableStock);
        Assert.AreEqual(2, item.ReservedStock);
        Assert.AreEqual(5, item.AvailableToPromise);
    }

    [TestMethod]
    public void CommitMoreThanIsReservedThrows()
    {
        var item = ItemWithStock(10);
        item.Reserve(2);

        Assert.ThrowsExactly<CatalogDomainException>(() => item.CommitReservation(3));

        Assert.AreEqual(10, item.AvailableStock);
        Assert.AreEqual(2, item.ReservedStock);
    }

    [TestMethod]
    public void ReleaseReservationGivesUnitsBackWithoutChangingPhysicalStock()
    {
        var item = ItemWithStock(10);
        item.Reserve(4);

        item.ReleaseReservation(4);

        Assert.AreEqual(10, item.AvailableStock);
        Assert.AreEqual(0, item.ReservedStock);
        Assert.AreEqual(10, item.AvailableToPromise);
    }

    [TestMethod]
    public void ReleaseMoreThanIsReservedThrows()
    {
        var item = ItemWithStock(10);
        item.Reserve(1);

        Assert.ThrowsExactly<CatalogDomainException>(() => item.ReleaseReservation(2));

        Assert.AreEqual(1, item.ReservedStock);
    }

    [TestMethod]
    public void ReservedStockNeverExceedsPhysicalStock()
    {
        var item = ItemWithStock(3);
        item.Reserve(3);

        // Committing everything and then releasing is not possible: the hold is already gone.
        item.CommitReservation(3);

        Assert.AreEqual(0, item.AvailableStock);
        Assert.AreEqual(0, item.ReservedStock);
        Assert.ThrowsExactly<CatalogDomainException>(() => item.Reserve(1));
    }
}

[TestClass]
public class InventoryReservationTests
{
    private static readonly DateTime ReservedAt = new(2026, 9, 20, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime ExpiresAt = ReservedAt.AddMinutes(15);

    private static InventoryReservation NewReservation(int units = 2) =>
        new(orderId: 42, productId: 7, units: units, reservedAt: ReservedAt, expiresAt: ExpiresAt);

    [TestMethod]
    public void NewReservationStartsReserved()
    {
        var reservation = NewReservation();

        Assert.AreEqual(ReservationStatus.Reserved, reservation.Status);
        Assert.AreEqual(42, reservation.OrderId);
        Assert.AreEqual(7, reservation.ProductId);
        Assert.AreEqual(2, reservation.Units);
        Assert.IsNull(reservation.CommittedAt);
        Assert.IsNull(reservation.ReleasedAt);
    }

    [TestMethod]
    public void ReservationRejectsNonPositiveUnits()
    {
        Assert.ThrowsExactly<CatalogDomainException>(
            () => new InventoryReservation(42, 7, 0, ReservedAt, ExpiresAt));
    }

    [TestMethod]
    public void ReservationRejectsExpiryAtOrBeforeCreation()
    {
        Assert.ThrowsExactly<CatalogDomainException>(
            () => new InventoryReservation(42, 7, 1, ReservedAt, ReservedAt));
        Assert.ThrowsExactly<CatalogDomainException>(
            () => new InventoryReservation(42, 7, 1, ReservedAt, ReservedAt.AddMinutes(-1)));
    }

    [TestMethod]
    public void CommitMarksReservationCommitted()
    {
        var reservation = NewReservation();
        var committedAt = ReservedAt.AddMinutes(1);

        reservation.Commit(committedAt);

        Assert.AreEqual(ReservationStatus.Committed, reservation.Status);
        Assert.AreEqual(committedAt, reservation.CommittedAt);
    }

    // A redelivered payment event must not commit the same units twice.
    [TestMethod]
    public void CommitTwiceThrows()
    {
        var reservation = NewReservation();
        reservation.Commit(ReservedAt.AddMinutes(1));

        Assert.ThrowsExactly<CatalogDomainException>(() => reservation.Commit(ReservedAt.AddMinutes(2)));
    }

    [TestMethod]
    public void ReleaseMarksReservationReleased()
    {
        var reservation = NewReservation();
        var releasedAt = ReservedAt.AddMinutes(1);

        reservation.Release(releasedAt);

        Assert.AreEqual(ReservationStatus.Released, reservation.Status);
        Assert.AreEqual(releasedAt, reservation.ReleasedAt);
    }

    [TestMethod]
    public void ReleaseTwiceThrows()
    {
        var reservation = NewReservation();
        reservation.Release(ReservedAt.AddMinutes(1));

        Assert.ThrowsExactly<CatalogDomainException>(() => reservation.Release(ReservedAt.AddMinutes(2)));
    }

    // A cancellation racing a payment must not claw back stock that was already sold.
    [TestMethod]
    public void CommittedReservationCannotBeReleased()
    {
        var reservation = NewReservation();
        reservation.Commit(ReservedAt.AddMinutes(1));

        Assert.ThrowsExactly<CatalogDomainException>(() => reservation.Release(ReservedAt.AddMinutes(2)));
        Assert.AreEqual(ReservationStatus.Committed, reservation.Status);
    }

    [TestMethod]
    public void ReleasedReservationCannotBeCommitted()
    {
        var reservation = NewReservation();
        reservation.Release(ReservedAt.AddMinutes(1));

        Assert.ThrowsExactly<CatalogDomainException>(() => reservation.Commit(ReservedAt.AddMinutes(2)));
        Assert.AreEqual(ReservationStatus.Released, reservation.Status);
    }

    [TestMethod]
    public void IsExpiredOnlyWhileStillReserved()
    {
        var reservation = NewReservation();

        Assert.IsFalse(reservation.IsExpired(ExpiresAt.AddSeconds(-1)));
        Assert.IsTrue(reservation.IsExpired(ExpiresAt));
        Assert.IsTrue(reservation.IsExpired(ExpiresAt.AddMinutes(1)));

        reservation.Commit(ReservedAt.AddMinutes(1));

        // A settled reservation is never a candidate for the expiry sweep.
        Assert.IsFalse(reservation.IsExpired(ExpiresAt.AddMinutes(1)));
    }
}
