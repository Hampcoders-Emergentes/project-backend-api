using Hampcoders.Electrolink.API.Profiles.Application.Internal.OutboundServices;
using Hampcoders.Electrolink.API.Profiles.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.Profiles.Domain.Model.Commands;
using Hampcoders.Electrolink.API.Profiles.Domain.Model.Exceptions;
using Hampcoders.Electrolink.API.Profiles.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Profiles.Domain.Repositories;
using Hampcoders.Electrolink.API.Profiles.Domain.Services;
using Hampcoders.Electrolink.API.Shared.Domain.Model.Entities;
using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Shared.Domain.Repositories;
using Hampcoders.Electrolink.API.Shared.Infrastructure;
using Hampcoders.Electrolink.API.Shared.Infrastructure.Interfaces;
using MediatR;

namespace Hampcoders.Electrolink.API.Profiles.Application.Internal.CommandServices;

public class ProfileCommandService(
  IProfileRepository profileRepository,
  IFileStorageService fileStorageService,
  IOrphanedFileRepository orphanedFileRepository,
  ExternalIamService externalIamService,
  IUnitOfWork unitOfWork,
  IMediator mediator,
  IProfileUniquenessChecker uniquenessChecker,
  ILogger<ProfileCommandService> logger)
  : IProfileCommandService
{

  public async Task<Profile?> Handle(CreateProfileCommand command)
  { 
      var userId = UserId.From(command.UserId);
      
      if (await profileRepository.ExistsByUserIdAsync(userId))
      {
          logger.LogWarning("[Profiles BC] Perfil ya existe para {UserId}. Evento duplicado ignorado.", command.UserId);
          return await profileRepository.FindByUserIdAsync(userId);
      }
      
      var profile = Profile.Create(userId);
      await profileRepository.AddAsync(profile);
      await unitOfWork.CompleteAsync();
      
      return profile;
  }

  public async Task<bool> Handle(UpdateTechnicianSpecialtiesCommand command)
  {
      var profile = await profileRepository.FindByTechnicianIdAsync(
          TechnicianId.From(command.TechnicianId));

      if (profile?.Technician is null)
          throw new ArgumentException($"Technician with ID {command.TechnicianId} not found.");

      var specialties = command.Specialties
          .Select(s => Enum.TryParse<ESpecialty>(s, true, out var parsed) ? parsed : (ESpecialty?)null)
          .Where(s => s.HasValue)
          .Select(s => s!.Value)
          .ToList();

      profile.Technician.UpdateSpecialties(specialties);
      profileRepository.Update(profile);
      await unitOfWork.CompleteAsync();
      return true;
  }

  public async Task<Profile> Handle(CompleteProfileAsTechnicianCommand command)
  {
      var profile = await profileRepository.FindByUserIdAsync(
          UserId.From(command.UserId));

      if (profile is null)
          throw new ArgumentException("Profile not found.");

      var personalData = PersonalData.Create(
          command.FirstName,
          command.LastName,
          PhoneNumber.From(command.PhoneNumber),
          Dni.From(command.Dni),
          DateOfBirth.From(command.DateOfBirth),
          Address.Create(command.Street, command.Number, command.District, command.City, command.Country,
              command.PostalCode));

      var technicianData = TechnicianData.Create(
          command.Specialties,
          command.ExperienceYears,
          command.AboutMe,
          ServiceArea.FromPointAndRadius(
              command.CenterLatitude,
              command.CenterLongitude,
              command.RadiusKm)); 

  await profile.CompleteAsTechnician(personalData, technicianData, uniquenessChecker);

      profileRepository.Update(profile);
      await unitOfWork.CompleteAsync();
      await PublishAndClearEventsAsync(profile);
      return profile;
  }

  public async Task<Profile> Handle(CompleteProfileAsHomeownerCommand command)
  {
      var profile = await profileRepository.FindByUserIdAsync(
          UserId.From(command.UserId));

      if (profile is null)
          throw new ArgumentException("Profile not found.");

      var personalData = PersonalData.Create(
          command.FirstName,
          command.LastName,
          PhoneNumber.From(command.PhoneNumber),
          Dni.From(command.Dni),
          DateOfBirth.From(command.DateOfBirth),
          Address.Create(command.Street, command.Number, command.District, command.City, command.Country, command.PostalCode)
      );

      var homeownerData = HomeownerData.Create(
          command.PreferredContactTime,
          command.CommunicationPreferences,
          command.EmergencyContact
      );

      await profile.CompleteAsHomeowner(personalData, homeownerData, uniquenessChecker);

      profileRepository.Update(profile);
      await unitOfWork.CompleteAsync();
      await PublishAndClearEventsAsync(profile);
      return profile;
  }

  public async Task<Profile> Handle(CompleteProfileAsCompanyCommand command)
  {
      var profile = await profileRepository.FindByUserIdAsync(
          UserId.From(command.UserId));

      if (profile is null)
          throw new ArgumentException("Profile not found.");

      var billingAddress = Address.Create(
          command.BillingStreet,
          command.BillingNumber,
          command.BillingDistrict,
          command.BillingCity,
          command.BillingCountry,
          command.BillingPostalCode);

      var companyData = CompanyData.Create(
          command.CompanyName,
          TaxId.From(command.TaxId),
          billingAddress,
          command.Industry,
          command.CompanySize,
          command.Website);

      PersonalData? personalData = null;
      if (command.FirstName is not null && command.LastName is not null)
      {
          personalData = PersonalData.Create(
              command.FirstName,
              command.LastName,
              PhoneNumber.From(command.PhoneNumber!),
              Dni.From(command.Dni!),
              DateOfBirth.From(command.DateOfBirth!),
              Address.Create(
                  command.Street ?? string.Empty,
                  command.Number ?? string.Empty,
                  command.District ?? string.Empty,
                  command.City ?? string.Empty,
                  command.Country ?? string.Empty,
                  command.PostalCode ?? string.Empty));
      }

      await profile.CompleteAsCompany(companyData, uniquenessChecker, personalData);

      profileRepository.Update(profile);
      await unitOfWork.CompleteAsync();
      await PublishAndClearEventsAsync(profile);
      return profile;
  }

  public async Task<Profile> Handle(UpdateProfilePersonalDataCommand command)
  {
      var profile = await profileRepository.FindByIdAsync(
          ProfileId.From(command.ProfileId)) ?? throw new ArgumentException("Profile not found.");
      
      EnsureOwnership(profile, command.UserId);

      Address? address = null;
      
      if (command.Street is not null || command.District is not null ||
          command.City   is not null || command.Country  is not null ||
          command.PostalCode is not null)
      {
          var current = profile.PersonalData!.Address;
          address = Address.Create(
              command.Street ?? current.Street, 
              command.Number ?? current.Number,
              command.District ?? current.District,
              command.City ?? current.City,
              command.Country ?? current.Country,
              command.PostalCode ?? current.PostalCode);
      }
      
      var phone = command.PhoneNumber is not null
          ? PhoneNumber.From(command.PhoneNumber)
          : null;

      profile.UpdatePersonalData(command.FirstName, command.LastName, phone, address);

      profileRepository.Update(profile);
      await unitOfWork.CompleteAsync();
      await PublishAndClearEventsAsync(profile);
      return profile;
  }

  public async Task<Profile> Handle(UpdateTechnicianDataCommand command)
  {
      var profile = await profileRepository.FindByIdAsync(
          ProfileId.From(command.ProfileId)) ?? throw new ArgumentException("Profile not found.");
      
      EnsureOwnership(profile, command.UserId);

      profile.UpdateTechnicianData(
          command.Specialties,
          command.ExperienceYears, 
          command.AboutMe);
      
      profileRepository.Update(profile);
      await unitOfWork.CompleteAsync();
      await PublishAndClearEventsAsync(profile);
      return profile;
  }

  public async Task<Profile> Handle(UpdateCommunicationPreferencesCommand command)
  {
      var profile = await profileRepository.FindByIdAsync(
          ProfileId.From(command.ProfileId)) ?? throw new ArgumentException("Profile not found.");
      
      EnsureOwnership(profile, command.UserId);

      var current = profile.Homeowner!.CommunicationPreferences;
      var preferences = CommunicationPreferences.Create(
          command.SmsNotifications   ?? current.SmsNotifications,
          command.EmailNotifications ?? current.EmailNotifications,
          command.PushNotifications  ?? current.PushNotifications,
          command.PreferredContactTime ?? profile.Homeowner!.PreferredContactTime);

      var emergencyContact = command.EmergencyContact;
      
      profile.UpdateHomeOwnerData(
          command.PreferredContactTime,
          preferences,
          emergencyContact);
      
      profileRepository.Update(profile);
      await unitOfWork.CompleteAsync();
      await PublishAndClearEventsAsync(profile);
      return profile;
  }

  public async Task<Profile> Handle(UploadProfilePictureCommand command)
  {
      var profile = await profileRepository.FindByIdAsync(ProfileId.From(command.ProfileId)) 
          ?? throw new ArgumentException("Profile not found.");

      EnsureOwnership(profile, command.UserId);

      using var stream = command.FileStream;
      var fileName = command.FileName;
      
      var result = await fileStorageService.UploadProfilePhotoAsync(profile.UserId.Value, stream, fileName);
      
      var photo = ProfilePhoto.Create(result.PublicUrl, result.ProviderId);
      profile.UpdateProfilePhoto(photo);

      profileRepository.Update(profile);
      await unitOfWork.CompleteAsync();
      await PublishAndClearEventsAsync(profile);

      return profile;
  }

  public async Task<SignedUploadData> Handle(GetProfilePhotoUploadUrlCommand command)
  {
      var profile = await profileRepository.FindByIdAsync(
          ProfileId.From(command.ProfileId)) ?? throw new ArgumentException("Profile not found.");

      EnsureOwnership(profile, command.UserId);

      return await fileStorageService.GetSignedUploadUrlForProfileAsync(profile.UserId.Value);
  }

  public async Task<Profile> Handle(UpdateProfilePhotoCommand command)
  {
      var profile = await profileRepository.FindByIdAsync(
          ProfileId.From(command.ProfileId)) ?? throw new ArgumentException("Profile not found.");

      EnsureOwnership(profile, command.UserId);

      try
      {
          if (profile.Photo is not null)
          {
              await fileStorageService.DeleteProfilePhotoAsync(profile.UserId.Value, profile.Photo.ProviderId);
          }

          var photo = ProfilePhoto.Create(command.PublicUrl, command.ProviderId);
          profile.UpdateProfilePhoto(photo);

          profileRepository.Update(profile);
          await unitOfWork.CompleteAsync();
          await PublishAndClearEventsAsync(profile);

          return profile;
      }
      catch
      {
          await orphanedFileRepository.AddAsync(
              OrphanedFileDeletion.Create(command.ProviderId, $"electrolink/profiles/{profile.UserId.Value}/avatar", "Profile photo registration failed"));
          await unitOfWork.CompleteAsync();
          throw;
      }
  }

  public async Task Handle(RemoveProfilePhotoCommand command)
  {
      var profile = await profileRepository.FindByIdAsync(
          ProfileId.From(command.ProfileId)) ?? throw new ArgumentException("Profile not found.");

      EnsureOwnership(profile, command.UserId);

      var oldProviderId = profile.RemoveProfilePhoto();

      if (oldProviderId is not null)
      {
          try
          {
              await fileStorageService.DeleteProfilePhotoAsync(profile.UserId.Value, oldProviderId);
          }
          catch
          {
              await orphanedFileRepository.AddAsync(
                  OrphanedFileDeletion.Create(oldProviderId, $"electrolink/profiles/{profile.UserId.Value}/avatar", "Profile photo deletion failed"));
          }
      }

      profileRepository.Update(profile);
      await unitOfWork.CompleteAsync();
      await PublishAndClearEventsAsync(profile);
  }

  public async Task Handle(DeactivateProfileCommand command)
  {
      var profile = await profileRepository.FindByIdAsync(
          ProfileId.From(command.ProfileId)) ?? throw new ArgumentException("Profile not found.");
      
      EnsureOwnership(profile, command.UserId);
      
      profile.Deactivate();
      profileRepository.Update(profile);
      await unitOfWork.CompleteAsync();
      await PublishAndClearEventsAsync(profile);
  }

  public async Task Handle(ReactivateProfileCommand command)
  {
      var profile = await profileRepository.FindByIdAsync(
          ProfileId.From(command.ProfileId)) ?? throw new ArgumentException("Profile not found.");

      EnsureOwnership(profile, command.UserId);

      profile.Reactivate();

      profileRepository.Update(profile);
      await unitOfWork.CompleteAsync();
      await PublishAndClearEventsAsync(profile);
  }
  
  public async Task Handle(GrantIoTCertificationCommand command)
  {
      var technicianId = TechnicianId.From(command.TechnicianId);
      var profile = await profileRepository.FindByTechnicianIdAsync(technicianId)
          ?? throw new ArgumentException($"Technician {command.TechnicianId} not found.");

      if (profile.Technician is null)
          throw new ArgumentException($"Technician {command.TechnicianId} has no Technician entity.");

      var certification = CertificationData.Create(
          command.CertificationName,
          command.IssuerOrganization,
          command.DateObtained,
          command.ExpirationDate,
          command.CredentialId,
          command.CredentialUrl);

      profile.GrantIoTCertification(certification);
      profileRepository.Update(profile);
      await unitOfWork.CompleteAsync();
      await PublishAndClearEventsAsync(profile);

      logger.LogInformation("[Profiles] IoT Certification granted to Technician {TechnicianId}", command.TechnicianId);
  }

  public async Task Handle(RevokeIoTCertificationCommand command)
  {
      var technicianId = TechnicianId.From(command.TechnicianId);
      var profile = await profileRepository.FindByTechnicianIdAsync(technicianId)
          ?? throw new ArgumentException($"Technician {command.TechnicianId} not found.");

      if (profile.Technician is null)
          throw new ArgumentException($"Technician {command.TechnicianId} has no Technician entity.");

      profile.RevokeIoTCertification();
      profileRepository.Update(profile);
      await unitOfWork.CompleteAsync();
      await PublishAndClearEventsAsync(profile);

      logger.LogInformation("[Profiles] IoT Certification revoked from Technician {TechnicianId}", command.TechnicianId);
  }

  private async Task PublishAndClearEventsAsync(Profile profile)
  {
      var events = profile.DomainEvents.ToArray();
      profile.ClearDomainEvents();
      foreach (var domainEvent in events)
          await mediator.Publish(domainEvent, CancellationToken.None);
  }
  
  private static void EnsureOwnership(Profile profile, string userId)
  {
      if (profile.UserId != UserId.From(userId))
          throw new UnauthorizedProfileAccessException();
  }
}
