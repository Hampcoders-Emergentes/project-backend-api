using Hampcoders.Electrolink.API.Assets.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.Assets.Domain.Model.Queries;
using Hampcoders.Electrolink.API.Profiles.Domain.Model.Queries;
using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;

namespace Hampcoders.Electrolink.API.Assets.Domain.Services;

public interface IPropertyQueryService
{
    Task<Property?> Handle(GetPropertyByIdQuery query);
    Task<Property?> Handle(GetPropertyByHomeownerIdAndIdQuery query);
    /// <returns>The requested page and the total number of matching properties.</returns>
    Task<(IEnumerable<Property> Items, int TotalCount)> Handle(GetAllPropertiesByOwnerIdQuery query);
    Task<Address?> Handle(GetPropertyAddressQuery query);
}