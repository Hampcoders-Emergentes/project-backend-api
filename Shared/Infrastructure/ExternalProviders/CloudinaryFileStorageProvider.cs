using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using Microsoft.Extensions.Options;

namespace Hampcoders.Electrolink.API.Shared.Infrastructure.ExternalProviders;

public class CloudinaryFileStorageProvider : IFileStorageProvider
{
    private readonly Cloudinary _cloudinary;
    private readonly CloudinarySettings _settings;

    private static readonly HashSet<string> ImageExtensions =
        [".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp", ".tiff", ".svg"];

    public CloudinaryFileStorageProvider(IOptions<CloudinarySettings> settings)
    {
        _settings = settings.Value;
        var account = new Account(_settings.CloudName, _settings.ApiKey, _settings.ApiSecret);
        _cloudinary = new Cloudinary(account);
    }

    public async Task<FileUploadResult> UploadAsync(
        Stream fileStream, string fileName, string folder,
        IReadOnlyDictionary<string, string>? tags = null)
    {
        try
        {
            var ext = Path.GetExtension(fileName).ToLowerInvariant();
            var isImage = ImageExtensions.Contains(ext);
            var publicId = $"{folder}/{Path.GetFileNameWithoutExtension(fileName)}";

            if (isImage)
            {
                var uploadParams = new ImageUploadParams
                {
                    File = new FileDescription(fileName, fileStream),
                    PublicId = publicId,
                    Folder = "",
                    Tags = tags != null ? string.Join(",", tags.Select(t => t.Value)) : null,
                    Overwrite = true
                };
                var result = await _cloudinary.UploadAsync(uploadParams);
                return MapResult(result);
            }
            else
            {
                var uploadParams = new RawUploadParams
                {
                    File = new FileDescription(fileName, fileStream),
                    PublicId = publicId,
                    Folder = "",
                    Tags = tags != null ? string.Join(",", tags.Select(t => t.Value)) : null,
                    Overwrite = true
                };
                var result = await _cloudinary.UploadAsync(uploadParams);
                return MapRawResult(result);
            }
        }
        catch (Exception ex) when (ex is not FileStorageProviderException)
        {
            throw new FileStorageProviderException("Cloudinary", "Upload failed", ex);
        }
    }

    public async Task<bool> DeleteAsync(string providerId, string ownerFolder)
    {
        if (!FileStorageFolders.Contains(ownerFolder, providerId))
            return false;

        try
        {
            var deletionParams = new DeletionParams(providerId) { ResourceType = ResourceType.Image };
            var result = await _cloudinary.DestroyAsync(deletionParams);
            if (result.Result != "ok")
            {
                deletionParams = new DeletionParams(providerId) { ResourceType = ResourceType.Raw };
                result = await _cloudinary.DestroyAsync(deletionParams);
            }
            return result.Result == "ok";
        }
        catch (Exception ex) when (ex is not FileStorageProviderException)
        {
            throw new FileStorageProviderException("Cloudinary", "Delete failed", ex);
        }
    }

    public Task<SignedUploadData> GetSignedUploadUrlAsync(
        string folder, IReadOnlyList<string>? allowedFormats = null, long? maxBytes = null)
    {
        try
        {
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var parameters = new SortedDictionary<string, object>
            {
                ["timestamp"] = timestamp,
                ["folder"] = folder
            };
            if (allowedFormats is { Count: > 0 })
                parameters["allowed_formats"] = string.Join(",", allowedFormats);
            if (maxBytes.HasValue)
                parameters["max_bytes"] = maxBytes.Value;

            var signature = _cloudinary.Api.SignParameters(parameters);
            var uploadUrl = $"https://api.cloudinary.com/v1_1/{_settings.CloudName}/auto/upload";

            return Task.FromResult(new SignedUploadData(uploadUrl, signature, timestamp, _settings.ApiKey));
        }
        catch (Exception ex) when (ex is not FileStorageProviderException)
        {
            throw new FileStorageProviderException("Cloudinary", "Failed to generate signed upload URL", ex);
        }
    }

    public string TransformUrl(string providerId, ImageTransformations transformations)
    {
        try
        {
            var transformation = new Transformation();
            if (transformations.Width.HasValue) transformation.Width(transformations.Width.Value);
            if (transformations.Height.HasValue) transformation.Height(transformations.Height.Value);
            if (transformations.Crop.HasValue) transformation.Crop(transformations.Crop.Value.ToString().ToLower());
            if (transformations.Quality.HasValue) transformation.Quality(transformations.Quality.Value);
            if (!string.IsNullOrWhiteSpace(transformations.Format))
                transformation.FetchFormat(transformations.Format);

            return _cloudinary.Api.UrlImgUp
                .Transform(transformation)
                .BuildUrl(providerId);
        }
        catch (Exception ex) when (ex is not FileStorageProviderException)
        {
            throw new FileStorageProviderException("Cloudinary", "Failed to build transform URL", ex);
        }
    }

    private static FileUploadResult MapResult(ImageUploadResult result) => new(
        result.SecureUrl?.AbsoluteUri ?? result.Url?.AbsoluteUri ?? string.Empty,
        result.PublicId,
        result.Format ?? "unknown",
        result.Bytes
    );

    private static FileUploadResult MapRawResult(RawUploadResult result) => new(
        result.SecureUrl?.AbsoluteUri ?? result.Url?.AbsoluteUri ?? string.Empty,
        result.PublicId,
        Path.GetExtension(result.PublicId)?.TrimStart('.') ?? "unknown",
        result.Bytes
    );
}
