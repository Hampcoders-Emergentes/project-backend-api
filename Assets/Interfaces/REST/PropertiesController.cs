using Hampcoders.Electrolink.API.Assets.Application.Internal.QueryServices;
using Hampcoders.Electrolink.API.Assets.Domain.Model.Commands;
using Hampcoders.Electrolink.API.Assets.Domain.Model.Exceptions;
using Hampcoders.Electrolink.API.Assets.Domain.Model.Queries;
using Hampcoders.Electrolink.API.Assets.Domain.Services;
using Hampcoders.Electrolink.API.Assets.Interfaces.REST.Resources;
using Hampcoders.Electrolink.API.Assets.Interfaces.REST.Transform;
using Hampcoders.Electrolink.API.IAM.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.Profiles.Interfaces.ACL;
using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Hampcoders.Electrolink.API.Assets.Interfaces.REST;

[ApiController]
[Route("api/v1/homeowners/{ownerId}/[controller]")]
[Route("api/v1/companies/{ownerId}/[controller]")]
[Produces("application/json")]
[SwaggerTag("Properties Controller Endpoints")]
// M3.22: the route owner must be the authenticated actor's own HomeOwner/Company subject.
// Ineligible actor → 403; another owner, a route prefix that does not match the actor's role, or a foreign/missing property → generic 404.
public class PropertiesController(
    IPropertyCommandService commandService,
    IPropertyQueryService   queryService,
    IProfilesContextFacade  profiles) : ControllerBase
{
    private ObjectResult ResourceNotFound() => NotFound(new { message = "Resource not found." });

    private async Task<(ClientIdentity? Owner, ActionResult? Denied)> AuthorizeOwnerAsync(string ownerId)
    {
        if (HttpContext.Items["User"] is not User actor) return (null, Unauthorized());
        var owner = await PropertyOwnerResolver.ResolveAsync(profiles, actor);
        if (owner is null) return (null, StatusCode(StatusCodes.Status403Forbidden));
        var routeType = Request.Path.StartsWithSegments("/api/v1/companies", StringComparison.OrdinalIgnoreCase)
            ? EClientType.Company : EClientType.Homeowner;
        if (owner.ClientType != routeType || owner.ClientId != ownerId) return (null, ResourceNotFound());
        return (owner, null);
    }

    private async Task<bool> OwnsAsync(ClientIdentity owner, string propertyId)
    {
        if (string.IsNullOrWhiteSpace(propertyId) || !propertyId.StartsWith("prop-", StringComparison.Ordinal)) return false;
        return await queryService.Handle(new GetPropertyByIdQuery(PropertyId.From(propertyId), owner)) is not null;
    }

    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<PropertyResource>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<PropertyResource>>> GetAll(
        [FromRoute] string ownerId,
        [FromQuery] string? city, [FromQuery] string? district,
        [FromQuery] string? region, [FromQuery] string? street,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 10)
    {
        var (owner, denied) = await AuthorizeOwnerAsync(ownerId);
        if (denied is not null) return denied;
        var results = await queryService.Handle(
            new GetAllPropertiesByOwnerIdQuery(owner!, city, street, page, pageSize));
        return Ok(results.Select(PropertyResourceFromEntityAssembler.ToResourceFromEntity));
    }

    /// <summary>Crea una propiedad para un homeowner</summary>
    [HttpPost]
    [ProducesResponseType(typeof(PropertyResource), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PropertyResource>> CreateProperty(
        [FromRoute] string ownerId, [FromBody] CreatePropertyResource resource)
    {
        var (owner, denied) = await AuthorizeOwnerAsync(ownerId);
        if (denied is not null) return denied;
        try
        {
            var command  = CreatePropertyCommandFromResourceAssembler.ToCommandFromResource(resource, owner!.ClientId);
            var property = await commandService.Handle(command);
            if (property is null) return BadRequest();
            var response = PropertyResourceFromEntityAssembler.ToResourceFromEntity(property);
            return Ok(response);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>Retorna una propiedad por ID</summary>
    [HttpGet("{propertyId}", Name = nameof(GetPropertyById))]
    [ProducesResponseType(typeof(PropertyResource), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PropertyResource>> GetPropertyById([FromRoute] string ownerId, string propertyId)
    {
        var (owner, denied) = await AuthorizeOwnerAsync(ownerId);
        if (denied is not null) return denied;
        if (!propertyId.StartsWith("prop-", StringComparison.Ordinal)) return ResourceNotFound();
        var property = await queryService.Handle(new GetPropertyByIdQuery(PropertyId.From(propertyId), owner!));
        if (property is null) return ResourceNotFound();
        return Ok(PropertyResourceFromEntityAssembler.ToResourceFromEntity(property));
    }

    /// <summary>Actualiza la ubicación (dirección y/o geolocalización) de una propiedad</summary>
    [HttpPatch("{propertyId}/location")]
    [ProducesResponseType(typeof(PropertyResource), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PropertyResource>> UpdateLocation(
        [FromRoute] string ownerId,
        [FromRoute] string propertyId, [FromBody] UpdateLocationResource resource)
    {
        var (owner, denied) = await AuthorizeOwnerAsync(ownerId);
        if (denied is not null) return denied;
        if (!await OwnsAsync(owner!, propertyId)) return ResourceNotFound();
        try
        {
            var command = UpdatePropertyLocationCommandFromResourceAssembler
                .ToCommandFromResource(resource, propertyId);
            var property = await commandService.Handle(command);
            if (property is null) return NotFound();
            return Ok(PropertyResourceFromEntityAssembler.ToResourceFromEntity(property));
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (AssetNotFoundException) { return ResourceNotFound(); }
    }

    /// <summary>Activa una propiedad</summary>
    [HttpPatch("{propertyId}/activate")]
    [ProducesResponseType(typeof(PropertyResource), StatusCodes.Status200OK)]
    public async Task<ActionResult<PropertyResource>> Activate([FromRoute] string ownerId, [FromRoute] string propertyId)
    {
        var (owner, denied) = await AuthorizeOwnerAsync(ownerId);
        if (denied is not null) return denied;
        if (!await OwnsAsync(owner!, propertyId)) return ResourceNotFound();
        var property = await commandService.Handle(new ActivatePropertyCommand(PropertyId.From(propertyId)));
        return property is null ? NotFound() : Ok(PropertyResourceFromEntityAssembler.ToResourceFromEntity(property));
    }

    /// <summary>Desactiva una propiedad</summary>
    [HttpPatch("{propertyId}/deactivate")]
    [ProducesResponseType(typeof(PropertyResource), StatusCodes.Status200OK)]
    public async Task<ActionResult<PropertyResource>> Deactivate([FromRoute] string ownerId, [FromRoute] string propertyId)
    {
        var (owner, denied) = await AuthorizeOwnerAsync(ownerId);
        if (denied is not null) return denied;
        if (!await OwnsAsync(owner!, propertyId)) return ResourceNotFound();
        var property = await commandService.Handle(new DeactivatePropertyCommand(PropertyId.From(propertyId)));
        return property is null ? NotFound() : Ok(PropertyResourceFromEntityAssembler.ToResourceFromEntity(property));
    }

    /// <summary>Archiva permanentemente una propiedad</summary>
    [HttpDelete("{propertyId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> ArchiveProperty([FromRoute] string ownerId, [FromRoute] string propertyId, [FromQuery] string reason)
    {
        var (owner, denied) = await AuthorizeOwnerAsync(ownerId);
        if (denied is not null) return denied;
        if (!await OwnsAsync(owner!, propertyId)) return ResourceNotFound();
        try
        {
            var command = ArchivePropertyCommandFromResourceAssembler.ToCommandFromResource(propertyId, reason);
            var property = await commandService.Handle(command);
            return property is null ? NotFound() : NoContent();
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (AssetNotFoundException) { return ResourceNotFound(); }
    }

    /// <summary>Obtiene una URL firmada para subir una foto directamente a Cloudinary</summary>
    [HttpGet("{propertyId}/photos/upload-url")]
    [ProducesResponseType(typeof(PropertyPhotoUploadUrlResource), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PropertyPhotoUploadUrlResource>> GetPhotoUploadUrl(
        [FromRoute] string ownerId, [FromRoute] string propertyId)
    {
        var (owner, denied) = await AuthorizeOwnerAsync(ownerId);
        if (denied is not null) return denied;
        if (!await OwnsAsync(owner!, propertyId)) return ResourceNotFound();
        try
        {
            var command = GetPropertyPhotoUploadUrlCommandFromResourceAssembler.ToCommand(owner!.ClientId, propertyId);
            var signedData = await commandService.Handle(command);

            return Ok(new PropertyPhotoUploadUrlResource(
                signedData.Url,
                signedData.Signature,
                signedData.Timestamp,
                signedData.ApiKey));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (AssetNotFoundException) { return ResourceNotFound(); }
    }

    /// <summary>Registra una foto después de que el cliente la subió directamente a Cloudinary</summary>
    [HttpPost("{propertyId}/photos")]
    [ProducesResponseType(typeof(PropertyResource), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PropertyResource>> RegisterPhoto(
        [FromRoute] string ownerId,
        [FromRoute] string propertyId,
        [FromBody] RegisterPropertyPhotoResource resource)
    {
        var (owner, denied) = await AuthorizeOwnerAsync(ownerId);
        if (denied is not null) return denied;
        if (!await OwnsAsync(owner!, propertyId)) return ResourceNotFound();
        try
        {
            var command = RegisterPropertyPhotoCommandFromResourceAssembler.ToCommand(owner!.ClientId, propertyId, resource);
            var property = await commandService.Handle(command);

            if (property is null) return NotFound();
            return Ok(PropertyResourceFromEntityAssembler.ToResourceFromEntity(property));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (AssetNotFoundException) { return ResourceNotFound(); }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>Establece una foto existente como la foto principal de la propiedad</summary>
    [HttpPost("{propertyId}/main-photo")]
    [ProducesResponseType(typeof(PropertyResource), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PropertyResource>> SetMainPhoto(
        [FromRoute] string ownerId,
        [FromRoute] string propertyId,
        [FromBody] SetPropertyMainPhotoResource resource)
    {
        var (owner, denied) = await AuthorizeOwnerAsync(ownerId);
        if (denied is not null) return denied;
        if (!await OwnsAsync(owner!, propertyId)) return ResourceNotFound();
        try
        {
            var command = SetPropertyMainPhotoCommandFromResourceAssembler.ToCommand(owner!.ClientId, propertyId, resource);
            var property = await commandService.Handle(command);

            if (property is null) return NotFound();
            return Ok(PropertyResourceFromEntityAssembler.ToResourceFromEntity(property));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (AssetNotFoundException) { return ResourceNotFound(); }
    }

}