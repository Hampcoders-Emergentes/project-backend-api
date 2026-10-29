namespace Hampcoders.Electrolink.API.Assets.Interfaces.ACL;

/// <summary>
/// Public facade of the Assets BC.
/// Consumed by Service Design BC and Service Operation BC via DI.
/// </summary>
public interface IAssetsContextFacade
{
    // ── Inventory ─────────────────────────────────────────
    /// <summary>Returns true if an inventory already exists for the technician.</summary>
    Task<bool> ExistsInventoryForTechnicianAsync(string technicianId);

    /// <summary>
    /// Creates an empty inventory for the technician.
    /// Returns the new inventoryId, or empty string on failure.
    /// </summary>
    Task<string> CreateTechnicianInventoryAsync(string technicianId);

    Task<bool> HasTechnicianEnoughStockAsync(string technicianId, string componentTypeId, int requiredQuantity);
    
    Task<bool> CheckAllComponentsInStockAsync(string technicianId,IReadOnlyList<(string ComponentTypeId, int quantity)> requirements);
    
    Task<bool> ComponentTypeExistsAndIsActiveAsync(string componentTypeId);
    
    Task<string?> GetComponentTypeNameAsync(string componentTypeId);
    
    Task<bool> AdjustTechnicianStockAsync(string technicianId,IReadOnlyList<(string ComponentId, int quantityAdjustment)> adjustments);
    
    Task<bool> ReserveComponentsForServiceAsync(string technicianId,string serviceId,IReadOnlyList<(string ComponentId, int quantity)> components);
    /// <summary>
    /// Returns true if the technician has enough available (unreserved) stock
    /// for every (componentTypeId, quantity) pair.
    /// </summary>
    Task<bool> HasSufficientStockAsync(
        string technicianId,
        IReadOnlyList<(string ComponentId, int Quantity)> requirements);

    /// <summary>
    /// Atomically reserves all listed components for the given serviceId.
    /// Returns true on success, false if any component lacks available stock.
    /// </summary>
    Task<bool> ReserveComponentsAsync(
        string technicianId,
        string serviceId,
        IReadOnlyList<(string ComponentId, int Quantity)> components);

    /// <summary>
    /// Consumes the reserved components for serviceId (decrements both
    /// reserved and available quantities). Returns true on success.
    /// </summary>
    Task<bool> ConsumeReservedComponentsAsync(string technicianId, string serviceId);
    
    Task<bool> ReleaseComponentReservationAsync(string technicianId, string serviceId, string reason);

    /// <summary>
    /// Releases the reservation for serviceId, restoring available quantities.
    /// </summary>
    Task ReleaseReservationAsync(string technicianId, string serviceId, string reason);

    // ── Properties ────────────────────────────────────────

    /// <summary>
    /// Returns (latitude, longitude) for the property, or null if not found.
    /// </summary>
    Task<(double Latitude, double Longitude)?> GetPropertyGeolocationAsync(string propertyId, string homeownerId);

    /// <summary>
    /// Returns the full street address of the property as a single formatted string,
    /// or empty string if not found.
    /// </summary>
    Task<string> GetPropertyAddressAsync(string propertyId);

    /// <summary>
    /// Returns true if the property belongs to the given ownerId and is IN_PORTFOLIO.
    /// </summary>
    Task<bool> PropertyBelongsToOwnerAsync(string propertyId, string ownerId);
    

    // ── Portfolio ─────────────────────────────────────────

    /// <summary>Returns true if a portfolio already exists for the homeowner.</summary>
    Task<bool> PortfolioExistsAsync(string homeownerId);

    /// <summary>
    /// Creates an empty portfolio for the homeowner.
    /// Returns the new portfolioId, or empty string on failure.
    /// </summary>
    Task<string> CreatePortfolioAsync(string homeownerId);

    /// <summary>
    /// Returns true if the homeowner has at least one property with status IN_PORTFOLIO.
    /// </summary>
    Task<bool> HomeownerHasPropertiesAsync(string homeownerId);

    // ── Maintenance ───────────────────────────────────────

    /// <summary>
    /// Records a completed maintenance event on the property.
    /// Called by Service Operation BC after ServiceCompleted.
    /// </summary>
    Task<bool> RecordMaintenanceForPropertyAsync(string propertyId, string serviceId, string technicianId, string workSummary, DateTime completedAt);

    // ── IoT ── 🆕 ─────────────────────────────────────────

    /// <summary>
    /// Retorna la cantidad de dispositivos activos (INSTALLED + MAINTENANCE) para facturación.
    /// Invocado por Subscriptions BC al inicio de cada ciclo.
    /// </summary>
    Task<int> GetActiveDeviceCountForBillingAsync(string ownerId);

    /// <summary>
    /// Verifica si la propiedad tiene al menos un dispositivo IoT INSTALLED.
    /// Invocado por Service Design BC para contextualizar solicitudes.
    /// </summary>
    Task<bool> PropertyHasInstalledDeviceAsync(string propertyId);

    /// <summary>
    /// Retorna el ID del dispositivo IoT asociado al número de serie, o null si no existe.
    /// Invocado por Monitoring BC para identificar dispositivos durante operaciones.
    /// </summary>
    Task<string?> GetDeviceIdBySerialNumberAsync(string serialNumber);
}