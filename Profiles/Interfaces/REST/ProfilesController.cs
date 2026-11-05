using System.Net.Mime;
using Hampcoders.Electrolink.API.Profiles.Domain.Model.Commands;
using Hampcoders.Electrolink.API.Profiles.Domain.Model.Queries;
using Hampcoders.Electrolink.API.Profiles.Domain.Services;
using Hampcoders.Electrolink.API.Profiles.Infrastructure.Interfaces.ASP.Configuration.Extensions.Filters;
using Hampcoders.Electrolink.API.Profiles.Interfaces.ACL;
using Hampcoders.Electrolink.API.Profiles.Interfaces.REST.Resources;
using Hampcoders.Electrolink.API.Profiles.Interfaces.REST.Transform;
using Hampcoders.Electrolink.API.Shared.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Hampcoders.Electrolink.API.Profiles.Interfaces.REST;

[Route("api/v1/[controller]")]
[Produces(MediaTypeNames.Application.Json)]
[ServiceFilter(typeof(DomainExceptionFilter))]
[SwaggerTag("Profile endpoints")]
public class ProfilesController(
    IProfileCommandService commandService,
    IProfileQueryService queryService,
    IProfilesContextFacade profilesContextFacade) : BaseProfileController(commandService, queryService)
{
    // GET api/v1/profiles/me
    [HttpGet("me")]
    [SwaggerOperation(Summary = "Get my profile", OperationId = "GetMyProfile")]
    [ProducesResponseType(typeof(MyProfileResource), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetMyProfile()
    {
        var profile = await QueryService.Handle(new GetMyProfileQuery(UserId));
        if (profile is null) return NotFound(new { message = "Profile not found." });
        return Ok(MyProfileResourceFromEntityAssembler.ToResourceFromEntity(profile));
    }

    // POST api/v1/profiles/me/complete/technician
    [HttpPost("me/complete/technician")]
    [SwaggerOperation(Summary = "Complete profile as technician", OperationId = "CompleteAsTechnician")]
    [ProducesResponseType(typeof(MyProfileResource), StatusCodes.Status201Created)]
    public async Task<IActionResult> CompleteAsTechnician(
        [FromBody] CompleteProfileAsTechnicianResource resource)
    {
        var command = CompleteProfileAsTechnicianCommandFromResourceAssembler
            .ToCommandFromResource(resource, UserId);
        var profile = await CommandService.Handle(command);
        return StatusCode(StatusCodes.Status201Created,
            MyProfileResourceFromEntityAssembler.ToResourceFromEntity(profile));
    }

    // POST api/v1/profiles/me/complete/homeowner
    [HttpPost("me/complete/homeowner")]
    [SwaggerOperation(Summary = "Complete profile as homeowner", OperationId = "CompleteAsHomeowner")]
    [ProducesResponseType(typeof(MyProfileResource), StatusCodes.Status201Created)]
    public async Task<IActionResult> CompleteAsHomeowner(
        [FromBody] CompleteProfileAsHomeownerResource resource)
    {
        var command = CompleteProfileAsHomeownerCommandFromResourceAssembler
            .ToCommandFromResource(resource, UserId);
        var profile = await CommandService.Handle(command);
        return StatusCode(StatusCodes.Status201Created,
            MyProfileResourceFromEntityAssembler.ToResourceFromEntity(profile));
    }

    // POST api/v1/profiles/me/complete/company
    [HttpPost("me/complete/company")]
    [SwaggerOperation(Summary = "Complete profile as company", OperationId = "CompleteAsCompany")]
    [ProducesResponseType(typeof(MyProfileResource), StatusCodes.Status201Created)]
    public async Task<IActionResult> CompleteAsCompany(
        [FromBody] CompleteProfileAsCompanyResource resource)
    {
        var command = CompleteProfileAsCompanyCommandFromResourceAssembler
            .ToCommandFromResource(resource, UserId);
        var profile = await CommandService.Handle(command);
        return StatusCode(StatusCodes.Status201Created,
            MyProfileResourceFromEntityAssembler.ToResourceFromEntity(profile));
    }

    // GET api/v1/profiles/me/status
    [HttpGet("me/status")]
    [SwaggerOperation(Summary = "Get profile status", OperationId = "GetProfileStatus")]
    [ProducesResponseType(typeof(ProfileStatusResource), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetProfileStatus()
    {
        var readModel = await QueryService.Handle(new GetProfileStatusQuery(UserId));
        if (readModel is null) return NotFound(new { message = "Profile not found." });
        return Ok(ProfileStatusResourceFromReadModelAssembler.ToResourceFromReadModel(readModel));
    }

    // PATCH api/v1/profiles/me/personal-data
    [HttpPatch("me/personal-data")]
    [SwaggerOperation(Summary = "Update personal data", OperationId = "UpdatePersonalData")]
    [ProducesResponseType(typeof(MyProfileResource), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdatePersonalData(
        [FromBody] UpdatePersonalDataResource resource)
    {
        var profileId = await GetProfileIdAsync();
        if (profileId is null) return NotFound(new { message = "Profile not found." });

        var command = UpdatePersonalDataCommandFromResourceAssembler
            .ToCommandFromResource(resource, profileId, UserId);
        var profile = await CommandService.Handle(command);
        return Ok(MyProfileResourceFromEntityAssembler.ToResourceFromEntity(profile));
    }

    // POST api/v1/profiles/me/picture
    [HttpPost("me/picture")]
    [SwaggerOperation(Summary = "Upload profile picture", OperationId = "UploadProfilePicture")]
    [ProducesResponseType(typeof(MyProfileResource), StatusCodes.Status200OK)]
    public async Task<IActionResult> UploadProfilePicture(IFormFile file)
    {
        var profileId = await GetProfileIdAsync();
        if (profileId is null) return NotFound(new { message = "Profile not found." });

        using var stream = file.OpenReadStream();
        var fileName = $"{profileId}-{Guid.NewGuid()}{Path.GetExtension(file.FileName)}";
        var command = new UploadProfilePictureCommand(profileId, UserId, stream, fileName);
        var profile = await CommandService.Handle(command);

        return Ok(MyProfileResourceFromEntityAssembler.ToResourceFromEntity(profile));
    }

    // PATCH api/v1/profiles/me/technician
    [HttpPatch("me/technician")]
    [SwaggerOperation(Summary = "Update technician data", OperationId = "UpdateTechnicianData")]
    [ProducesResponseType(typeof(MyProfileResource), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateTechnicianData(
        [FromBody] UpdateTechnicianDataRequest resource)
    {
        var profileId = await GetProfileIdAsync();
        if (profileId is null) return NotFound(new { message = "Profile not found." });

        var command = UpdateTechnicianDataCommandFromResourceAssembler
            .ToCommandFromResource(resource, profileId, UserId);
        var profile = await CommandService.Handle(command);
        return Ok(MyProfileResourceFromEntityAssembler.ToResourceFromEntity(profile));
    }

    // PATCH api/v1/profiles/me/homeowner
    [HttpPatch("me/homeowner")]
    [SwaggerOperation(Summary = "Update homeowner preferences", OperationId = "UpdateHomeownerPreferences")]
    [ProducesResponseType(typeof(MyProfileResource), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateHomeownerPreferences(
        [FromBody] UpdateHomeownerPreferencesResource resource)
    {
        var profileId = await GetProfileIdAsync();
        if (profileId is null) return NotFound(new { message = "Profile not found." });

        var command = UpdateHomeownerPreferencesCommandFromResourceAssembler
            .ToCommandFromResource(resource, profileId, UserId);
        var profile = await CommandService.Handle(command);
        return Ok(MyProfileResourceFromEntityAssembler.ToResourceFromEntity(profile));
    }

    // POST api/v1/profiles/me/deactivate
    [HttpPost("me/deactivate")]
    [SwaggerOperation(Summary = "Deactivate profile", OperationId = "DeactivateProfile")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeactivateProfile(
        [FromBody] DeactivateProfileResource resource)
    {
        var profileId = await GetProfileIdAsync();
        if (profileId is null) return NotFound(new { message = "Profile not found." });

        await CommandService.Handle(new DeactivateProfileCommand(
            ProfileId: profileId,
            UserId:    UserId,
            Reason:    resource.Reason,
            Notes:     resource.Notes));
        return NoContent();
    }

    // POST api/v1/profiles/me/reactivate
    [HttpPost("me/reactivate")]
    [SwaggerOperation(Summary = "Reactivate profile", OperationId = "ReactivateProfile")]
    [ProducesResponseType(typeof(MyProfileResource), StatusCodes.Status200OK)]
    public async Task<IActionResult> ReactivateProfile()
    {
        var profileId = await GetProfileIdAsync();
        if (profileId is null) return NotFound(new { message = "Profile not found." });

        await CommandService.Handle(new ReactivateProfileCommand(
            ProfileId: profileId,
            UserId:    UserId));

        var profile = await QueryService.Handle(new GetMyProfileQuery(UserId));
        return Ok(MyProfileResourceFromEntityAssembler.ToResourceFromEntity(profile!));
    }

    // POST api/v1/profiles/me/photo/upload-url
    [HttpPost("me/photo/upload-url")]
    [SwaggerOperation(Summary = "Get signed upload URL for profile photo", OperationId = "GetProfilePhotoUploadUrl")]
    [ProducesResponseType(typeof(SignedUploadUrlResource), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetProfilePhotoUploadUrl()
    {
        var profileId = await GetProfileIdAsync();
        if (profileId is null) return NotFound(new { message = "Profile not found." });

        var signedData = await CommandService.Handle(new GetProfilePhotoUploadUrlCommand(profileId, UserId));
        return Ok(new SignedUploadUrlResource(
            signedData.Url,
            signedData.Signature,
            signedData.Timestamp));
    }

    // PUT api/v1/profiles/me/photo
    [HttpPut("me/photo")]
    [SwaggerOperation(Summary = "Update profile photo after direct upload", OperationId = "UpdateProfilePhoto")]
    [ProducesResponseType(typeof(MyProfileResource), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateProfilePhoto(
        [FromBody] UpdateProfilePhotoResource resource)
    {
        var profileId = await GetProfileIdAsync();
        if (profileId is null) return NotFound(new { message = "Profile not found." });

        var command = UpdateProfilePhotoCommandFromResourceAssembler
            .ToCommandFromResource(profileId, UserId, resource);
        var profile = await CommandService.Handle(command);
        return Ok(MyProfileResourceFromEntityAssembler.ToResourceFromEntity(profile));
    }

    // PUT api/v1/profiles/{profileId}/thresholds
    [HttpPut("{profileId}/thresholds")]
    [SwaggerOperation(Summary = "Set consumption thresholds", OperationId = "SetConsumptionThresholds")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SetThresholds(
        [FromRoute] string profileId,
        [FromBody] SetThresholdsResource resource)
    {
        var claims = await profilesContextFacade.GetProfileClaimsAsync(UserId);
        if (claims?.ProfileId != profileId) return NotFound(new { message = "Resource not found." });

        await profilesContextFacade.SetConsumptionThresholdsAsync(profileId, resource.Thresholds);
        return NoContent();
    }

    // DELETE api/v1/profiles/me/photo
    [HttpDelete("me/photo")]
    [SwaggerOperation(Summary = "Remove profile photo", OperationId = "RemoveProfilePhoto")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> RemoveProfilePhoto()
    {
        var profileId = await GetProfileIdAsync();
        if (profileId is null) return NotFound(new { message = "Profile not found." });

        await CommandService.Handle(new RemoveProfilePhotoCommand(profileId, UserId));
        return NoContent();
    }
}
