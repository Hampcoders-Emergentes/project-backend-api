namespace Hampcoders.Electrolink.API.Shared.Infrastructure;

public record FileUploadResult(string PublicUrl, string ProviderId, string Format, long SizeBytes);
public record SignedUploadData(string Url, string Signature, long Timestamp, string ApiKey);

public enum CropMode { Fill, Fit, Limit, Pad, Scale, Thumb }

public record ImageTransformations
{
    public int? Width { get; init; }
    public int? Height { get; init; }
    public CropMode? Crop { get; init; }
    public int? Quality { get; init; }
    public string? Format { get; init; }

    private ImageTransformations() { }

    public static ImageTransformations Create(
        int? width = null, int? height = null,
        CropMode? crop = null, int? quality = null,
        string? format = null) => new()
    {
        Width = width, Height = height,
        Crop = crop, Quality = quality, Format = format
    };
}

public interface IFileStorageProvider
{
    Task<FileUploadResult> UploadAsync(
        Stream fileStream,
        string fileName,
        string folder,
        IReadOnlyDictionary<string, string>? tags = null);

    // Returns false, without calling the provider, when the file is outside ownerFolder.
    Task<bool> DeleteAsync(string providerId, string ownerFolder);

    Task<SignedUploadData> GetSignedUploadUrlAsync(
        string folder,
        IReadOnlyList<string>? allowedFormats = null,
        long? maxBytes = null);

    string TransformUrl(string providerId, ImageTransformations transformations);
}
