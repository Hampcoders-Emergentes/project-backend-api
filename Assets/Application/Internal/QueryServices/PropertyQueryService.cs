using Hampcoders.Electrolink.API.Assets.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.Assets.Domain.Model.Queries;
using Hampcoders.Electrolink.API.Assets.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Assets.Domain.Repositories;
using Hampcoders.Electrolink.API.Assets.Domain.Services;
using Hampcoders.Electrolink.API.Profiles.Domain.Model.Queries;
using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;

namespace Hampcoders.Electrolink.API.Assets.Application.Internal.QueryServices;

public class PropertyQueryService(IPropertyRepository propertyRepository) : IPropertyQueryService
{
    // Filters by the query's owner (M3.22); previously the owner was ignored.
    public async Task<Property?> Handle(GetPropertyByIdQuery query)
        => await propertyRepository.FindByIdAndOwnerAsync(query.PropertyId, query.Owner);

    /// <summary>
    /// Maneja la consulta para obtener una propiedad específica por su ID,
    /// asegurando que pertenezca al propietario correcto.
    /// </summary>
    public async Task<Property?> Handle(GetPropertyByHomeownerIdAndIdQuery query)
    {
        // Esta línea ahora funcionará porque el 'using' le dice al compilador
        // dónde encontrar 'PropertyId' y 'OwnerId'.
        return await propertyRepository.FindByIdAndOwnerAsync(query.PropertyId, query.Owner);
    }

    /// <summary>
    /// Maneja la consulta para obtener todas las propiedades de un propietario,
    /// aplicando los filtros opcionales de búsqueda.
    /// </summary>
    public async Task<IEnumerable<Property>> Handle(GetAllPropertiesByOwnerIdQuery query)
    {
        var (items, _) = await propertyRepository.GetAllFilteredPaginatedAsync(
            query.Owner, query.City, query.Street, query.Page, query.PageSize);
        return items;
    }
    
    public async Task<Address?> Handle(GetPropertyAddressQuery query)
    {
        var property = await propertyRepository.FindByIdAsync(query.PropertyId);
        return property?.Address;
    }
}