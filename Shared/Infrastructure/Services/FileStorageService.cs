using Hampcoders.Electrolink.API.Shared.Infrastructure;
using Hampcoders.Electrolink.API.Shared.Infrastructure.Interfaces;

namespace Hampcoders.Electrolink.API.Shared.Infrastructure.Services;

public class FileStorageService(IFileStorageProvider fileStorageProvider) : IFileStorageService
{
    private const string ProfilePhotoFolder = "electrolink/profiles";
    private const string PropertyPhotoFolder = "electrolink/assets/properties";
    private const string WorkPhotoFolder = "electrolink/operation";
    private const long ProfilePhotoMaxSizeBytes = 5L * 1024 * 1024;
    private const long PropertyPhotoMaxSizeBytes = 10L * 1024 * 1024;
    private const long WorkPhotoMaxSizeBytes = 10L * 1024 * 1024;

    private static readonly IReadOnlyList<string> AllowedFormats = new List<string> { "jpg", "jpeg", "png", "webp" };

    public async Task<FileUploadResult> UploadProfilePhotoAsync(string userId, Stream fileStream, string fileName)
    {
        var folder = $"{ProfilePhotoFolder}/{userId}/avatar";
        return await fileStorageProvider.UploadAsync(fileStream, fileName, folder);
    }

    public async Task<SignedUploadData> GetSignedUploadUrlForProfileAsync(string userId)
    {
        var folder = $"{ProfilePhotoFolder}/{userId}/avatar";
        return await fileStorageProvider.GetSignedUploadUrlAsync(folder, AllowedFormats, ProfilePhotoMaxSizeBytes);
    }

    public async Task<SignedUploadData> GetSignedUploadUrlForPropertyPhotoAsync(string propertyId)
    {
        var timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
        var folder = $"{PropertyPhotoFolder}/{propertyId}/{timestamp}";
        return await fileStorageProvider.GetSignedUploadUrlAsync(folder, AllowedFormats, PropertyPhotoMaxSizeBytes);
    }

    public async Task<SignedUploadData> GetSignedUploadUrlForWorkPhotoAsync(string executionId, string photoType)
    {
        var timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
        var folder = $"{WorkPhotoFolder}/{executionId}/{photoType}/{timestamp}";
        return await fileStorageProvider.GetSignedUploadUrlAsync(folder, AllowedFormats, WorkPhotoMaxSizeBytes);
    }

    public async Task<bool> DeleteProfilePhotoAsync(string userId, string providerId)
    {
        return await fileStorageProvider.DeleteAsync(providerId, FileStorageFolders.ProfilePhoto(userId));
    }

    public string GetThumbnailUrl(string providerId)
    {
        return fileStorageProvider.TransformUrl(providerId, ImageTransformations.Create(width: 400, height: 300, crop: CropMode.Fill, quality: 80));
    }
}