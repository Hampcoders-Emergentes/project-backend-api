namespace Hampcoders.Electrolink.API.Shared.Infrastructure.Interfaces;

public interface IFileStorageService
{
    Task<FileUploadResult> UploadProfilePhotoAsync(string userId, Stream fileStream, string fileName);
    Task<SignedUploadData> GetSignedUploadUrlForProfileAsync(string userId);
    Task<SignedUploadData> GetSignedUploadUrlForPropertyPhotoAsync(string propertyId);
    Task<SignedUploadData> GetSignedUploadUrlForWorkPhotoAsync(string executionId, string photoType);
    Task<bool> DeleteProfilePhotoAsync(string userId, string providerId);
    string GetThumbnailUrl(string providerId);
}