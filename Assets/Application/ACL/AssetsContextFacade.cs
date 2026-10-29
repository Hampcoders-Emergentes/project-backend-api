using Hampcoders.Electrolink.API.Assets.Domain.Model.Commands;
using Hampcoders.Electrolink.API.Assets.Domain.Model.Queries;
using Hampcoders.Electrolink.API.Assets.Domain.Services;
using Hampcoders.Electrolink.API.Assets.Interfaces.ACL;
using Hampcoders.Electrolink.API.Assets.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Assets.Domain.Repositories;
using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;
using TechnicianId = Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects.TechnicianId;

namespace Hampcoders.Electrolink.API.Assets.Application.ACL;

public class AssetsContextFacade(
    ITechnicianInventoryCommandService inventoryCommandService,
    ITechnicianInventoryQueryService   inventoryQueryService,
    IPropertyCommandService            propertyCommandService,
    IPropertyQueryService              propertyQueryService,
    IComponentTypeQueryService       componentTypeQueryService,
    IComponentTypeRepository           componentTypeRepository,
    IPropertyPortfolioQueryService     portfolioQueryService,
    IPropertyPortfolioCommandService   portfolioCommandService,
    IIoTDeviceCommandService           deviceCommandService,
    IIoTDeviceQueryService             deviceQueryService,
    IIoTDeviceRepository               deviceRepository) : IAssetsContextFacade
{
    // ── Inventory ─────────────────────────────────────────

    public async Task<bool> ExistsInventoryForTechnicianAsync(string technicianId)
    {
        var inventory = await inventoryQueryService.Handle(new GetInventoryByTechnicianIdQuery(TechnicianId.From(technicianId)));
        return inventory is not null;
    }

    public async Task<string> CreateTechnicianInventoryAsync(string technicianId)
    {
        var result = await inventoryCommandService.Handle(
            new CreateTechnicianInventoryCommand(TechnicianId.From(technicianId)));
        return result?.Id.Value ?? string.Empty;
    }

    public async Task<bool> HasTechnicianEnoughStockAsync(
        string technicianId, string componentTypeId, int requiredQuantity)
    {
        var technicianInventory = await inventoryQueryService.Handle(
            new GetInventoryByTechnicianIdQuery(TechnicianId.From(technicianId)));
        if (technicianInventory is null) return false;

        var stock = technicianInventory.Inventory.StockItems.FirstOrDefault(s =>
            s.ComponentId.Value == componentTypeId);
        return stock is not null && stock.AvailableForReservation >= requiredQuantity;
    }

    public async Task<bool> CheckAllComponentsInStockAsync(string technicianId, IReadOnlyList<(string ComponentTypeId, int quantity)> requirements)
    {
        var technicianInventory = await inventoryQueryService.Handle(
            new GetInventoryByTechnicianIdQuery(TechnicianId.From(technicianId)));
        if (technicianInventory is null) return false;

        return requirements.All(req =>
        {
            var stock = technicianInventory.Inventory.StockItems.FirstOrDefault(
                s => s.ComponentTypeId.Value == req.ComponentTypeId);
            return stock is not null && stock.AvailableForReservation >= req.quantity;
        });
    }

    public async Task<bool> ComponentTypeExistsAndIsActiveAsync(string componentTypeId)
        => await componentTypeRepository.ExistsActiveByIdAsync(componentTypeId);

    public async Task<string?> GetComponentTypeNameAsync(string componentTypeId)
        => await componentTypeQueryService.Handle(new GetComponentTypeNameByIdQuery(ComponentTypeId.From(componentTypeId)));

    public async Task<bool> AdjustTechnicianStockAsync(string technicianId, IReadOnlyList<(string ComponentId, int quantityAdjustment)> adjustments)
    {
        var adjustmentsList = adjustments
            .Select(a => new ComponentAdjustment(ComponentId.From(a.ComponentId), a.quantityAdjustment))
            .ToList();

        var result = await inventoryCommandService.Handle(
            new AdjustTechnicianInventoryCommand(TechnicianId.From(technicianId), adjustmentsList));
        return result is not null;
    }

    public async Task<bool> ReserveComponentsForServiceAsync(string technicianId, string serviceId, IReadOnlyList<(string ComponentId, int quantity)> components)
    {
        var componentsList = components
            .Select(c => new ComponentAdjustment(ComponentId.From(c.ComponentId), c.quantity))
            .ToList();

        var result = await inventoryCommandService.Handle(
            new ReserveComponentsForServiceCommand(TechnicianId.From(technicianId), AssignmentId.From(serviceId), componentsList));
        return result is not null;
    }

    public async Task<bool> HasSufficientStockAsync(
        string technicianId,
        IReadOnlyList<(string ComponentId, int Quantity)> requirements)
    {
        var inv = await inventoryQueryService.Handle(new GetInventoryByTechnicianIdQuery(TechnicianId.From(technicianId)));
        if (inv is null) return false;

        return requirements.All(req =>
        {
            var stock = inv.Inventory.StockItems.FirstOrDefault(s => s.ComponentId.Value == req.ComponentId);
            return stock is not null && stock.AvailableForReservation >= req.Quantity;
        });
    }

    public async Task<bool> ReserveComponentsAsync(
        string technicianId,
        string serviceId,
        IReadOnlyList<(string ComponentId, int Quantity)> components)
    {
        var adjustments = components
            .Select(c => new ComponentAdjustment(ComponentId.From(c.ComponentId), c.Quantity))
            .ToList();

        var result = await inventoryCommandService.Handle(
            new ReserveComponentsForServiceCommand(TechnicianId.From(technicianId), AssignmentId.From(serviceId), adjustments));

        return result is not null;
    }

    public async Task<bool> ConsumeReservedComponentsAsync(string technicianId, string serviceId)
    {
        var result = await inventoryCommandService.Handle(
            new ConsumeComponentsForServiceCommand(TechnicianId.From(technicianId), AssignmentId.From(serviceId)));
        return result is not null;
    }

    public async Task<bool> ReleaseComponentReservationAsync(string technicianId, string serviceId, string reason)
    {
        var result = await inventoryCommandService.Handle(
            new ReleaseReservationCommand(TechnicianId.From(technicianId), AssignmentId.From(serviceId), reason));
        return result;
    }

    public async Task ReleaseReservationAsync(string technicianId, string serviceId, string reason)
        => await inventoryCommandService.Handle(new ReleaseReservationCommand(TechnicianId.From(technicianId), AssignmentId.From(serviceId), reason));

    // ── Properties ────────────────────────────────────────

    public async Task<(double Latitude, double Longitude)?> GetPropertyGeolocationAsync(string propertyId, string ownerId)
    {
        var prop = await propertyQueryService.Handle(new GetPropertyByIdQuery(PropertyId.From(propertyId), ClientIdentity.FromOwnerId(ownerId)));
        if (prop is null) return null;
        return (prop.Geolocation.Latitude, prop.Geolocation.Longitude);
    }

    public async Task<string> GetPropertyAddressAsync(string propertyId)
    {
        var addr = await propertyQueryService.Handle(new GetPropertyAddressQuery(PropertyId.From(propertyId)));
        if (addr is null) return string.Empty;
        return $"{addr.Street} {addr.Number}, {addr.City}".Trim();
    }

    public async Task<bool> PropertyBelongsToOwnerAsync(string propertyId, string ownerId)
    {
        var prop = await propertyQueryService.Handle(new GetPropertyByIdQuery(PropertyId.From(propertyId), ClientIdentity.FromOwnerId(ownerId)));
        return prop is not null;
    }

    // ── Portfolio ─────────────────────────────────────────

    public async Task<bool> PortfolioExistsAsync(string ownerId)
    {
        var portfolio = await portfolioQueryService.Handle(new GetPortfolioByOwnerIdQuery(ClientIdentity.FromOwnerId(ownerId)));
        return portfolio is not null;
    }

    public async Task<string> CreatePortfolioAsync(string ownerId)
    {
        var portfolio = await portfolioCommandService.Handle(new CreatePropertyPortfolioCommand(ClientIdentity.FromOwnerId(ownerId)));
        return portfolio?.Id.Value ?? string.Empty;
    }

    public async Task<bool> HomeownerHasPropertiesAsync(string ownerId)
    {
        var portfolio = await portfolioQueryService.Handle(new GetPortfolioByOwnerIdQuery(ClientIdentity.FromOwnerId(ownerId)));
        return portfolio is not null && portfolio.Entries.Any();
    }

    public async Task<bool> RecordMaintenanceForPropertyAsync(string propertyId, string serviceId, string technicianId, string workSummary,
        DateTime completedAt)
    {
        var result = await propertyCommandService.Handle(new RecordMaintenanceForPropertyCommand(
            PropertyId.From(propertyId), AssignmentId.From(serviceId), TechnicianId.From(technicianId), workSummary, completedAt));
        return result is not null;
    }

    // ── IoT 🆕 ─────────────────────────────────────────────

    public Task<int> GetActiveDeviceCountForBillingAsync(string ownerId)
        => deviceQueryService.Handle(new GetActiveDeviceCountByOwnerIdQuery(ownerId));

    public async Task<bool> PropertyHasInstalledDeviceAsync(string propertyId)
    {
        var devices = await deviceQueryService.Handle(new GetIoTDevicesByPropertyIdQuery(propertyId));
        return devices.Any(d => d.Status == EDeviceStatus.Installed);
    }

    public async Task<string?> GetDeviceIdBySerialNumberAsync(string serialNumber)
    {
        var device = await deviceQueryService.Handle(new GetIoTDeviceBySerialNumberQuery(serialNumber));
        return device?.Id.Value;
    }
}