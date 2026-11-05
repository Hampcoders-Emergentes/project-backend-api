namespace Hampcoders.Electrolink.API.Shared.Infrastructure;

public static class FileStorageFolders
{
    public static string ProfilePhoto(string userId) => $"electrolink/profiles/{userId}/avatar";

    // Provider ids can reach deletion from client input, so a file is only deletable inside the folder of the resource that owns it.
    public static bool Contains(string ownerFolder, string providerId)
    {
        if (string.IsNullOrWhiteSpace(ownerFolder) || string.IsNullOrWhiteSpace(providerId))
            return false;
        if (providerId.Contains("..", StringComparison.Ordinal) || providerId.Contains('\\'))
            return false;
        return providerId.StartsWith(ownerFolder.TrimEnd('/') + "/", StringComparison.Ordinal);
    }
}
