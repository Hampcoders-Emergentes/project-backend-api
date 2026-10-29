using Hampcoders.Electrolink.API.Assets.Interfaces.ACL;
using Hampcoders.Electrolink.API.Planning.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;

namespace Hampcoders.Electrolink.API.Planning.Application.Internal.OutboundServices;

/// <summary>
/// Anti-Corruption Layer para comunicación con Assets Bounded Context
/// </summary>
public class ExternalAssetsService(IAssetsContextFacade assetsContextFacade)
{
    public async Task<(double Latitude, double Longitude)?> GetActiveOwnedPropertyGeolocationAsync(string propertyId, ClientIdentity owner)
        => await assetsContextFacade.GetActiveOwnedPropertyGeolocationAsync(propertyId, owner);

    public async Task<(double latitude, double longitude)?> GetPropertyGeolocationAsync(string propertyId, string ownerId)
        => await assetsContextFacade.GetPropertyGeolocationAsync(propertyId, ownerId);
    
    public async Task<string?> GetComponentTypeNameAsync(string componentTypeId)
        => await assetsContextFacade.GetComponentTypeNameAsync(componentTypeId);
    
    public async Task<bool> TechnicianHasStockForRecipeAsync(string technicianId,IReadOnlyList<(string componentTypeId, int quantity)> requirements)
        => await assetsContextFacade.CheckAllComponentsInStockAsync(technicianId, requirements);

    public async Task<bool> ReserveComponentsAsync(string technicianId,string serviceId,IReadOnlyList<(string componentTypeId, int quantity)> components)
        => await assetsContextFacade.ReserveComponentsForServiceAsync(technicianId, serviceId, components);

    public async Task<bool> ReleaseReservationAsync(string technicianId, string serviceId, string reason)
        => await assetsContextFacade.ReleaseComponentReservationAsync(technicianId, serviceId, reason);
    public async Task<bool> HomeownerHasPropertiesAsync(string homeownerId)
        => await assetsContextFacade.HomeownerHasPropertiesAsync(homeownerId);

    public async Task<bool> CheckComponentStockAsync(string technicianId, IReadOnlyList<(string componentTypeId, int quantity)> componentRequirements)
        => await assetsContextFacade.CheckAllComponentsInStockAsync(technicianId, componentRequirements);
    
    public async Task<bool> ComponentTypeExistsAndIsActiveAsync(string componentTypeId)
        => await assetsContextFacade.ComponentTypeExistsAndIsActiveAsync(componentTypeId);

}
