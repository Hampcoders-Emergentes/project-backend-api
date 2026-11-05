namespace Hampcoders.Electrolink.API.Shared.Infrastructure.ExternalProviders;

public class NullFileStorageProvider : IFileStorageProvider
{
    public Task<FileUploadResult> UploadAsync(
        Stream fileStream, string fileName, string folder,
        IReadOnlyDictionary<string, string>? tags = null)
    {
        var providerId = $"null_file_{Guid.NewGuid():N}";
        var ext = Path.GetExtension(fileName).TrimStart('.');
        return Task.FromResult(new FileUploadResult(
            $"https://null-storage.local/{folder}/{fileName}",
            providerId,
            string.IsNullOrEmpty(ext) ? "unknown" : ext,
            fileStream.Length));
    }

    public Task<bool> DeleteAsync(string providerId, string ownerFolder)
    {
        return Task.FromResult(FileStorageFolders.Contains(ownerFolder, providerId));
    }

    public Task<SignedUploadData> GetSignedUploadUrlAsync(
        string folder, IReadOnlyList<string>? allowedFormats = null, long? maxBytes = null)
    {
        return Task.FromResult(new SignedUploadData(
            "https://null-storage.local/upload",
            "null_signature",
            DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            "null_api_key"));
    }

    public string TransformUrl(string providerId, ImageTransformations transformations)
    {
        return $"https://null-storage.local/{providerId}?w={transformations.Width}&h={transformations.Height}";
    }
}
