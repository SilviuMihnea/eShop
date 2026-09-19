namespace eShop.Catalog.API.IntegrationEvents;

public interface ICatalogIntegrationEventService
{
    Task SaveEventAndCatalogContextChangesAsync(IntegrationEvent evt);

    /// <summary>
    /// Saves several integration events alongside the pending catalog changes in a single
    /// transaction, for the cases where one thing happening produces more than one announcement
    /// and losing either of them would leave the system inconsistent.
    /// </summary>
    Task SaveEventsAndCatalogContextChangesAsync(IReadOnlyCollection<IntegrationEvent> events);

    Task PublishThroughEventBusAsync(IntegrationEvent evt);
}
