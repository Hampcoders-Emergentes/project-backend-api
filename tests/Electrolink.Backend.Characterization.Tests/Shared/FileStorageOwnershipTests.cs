using Electrolink.Backend.Characterization.Tests.Assets;
using Hampcoders.Electrolink.API.Shared.Infrastructure;
using Hampcoders.Electrolink.API.Shared.Infrastructure.Services;
using Xunit;

namespace Electrolink.Backend.Characterization.Tests.Shared;

public sealed class FileStorageOwnershipTests
{
    private const string Folder = "electrolink/profiles/us-1/avatar";

    [Theory]
    [InlineData("electrolink/profiles/us-1/avatar/photo", true)]
    [InlineData("electrolink/profiles/us-1/avatar/nested/photo", true)]
    [InlineData("electrolink/profiles/us-2/avatar/photo", false)]
    [InlineData("electrolink/profiles/us-1/avatar", false)]
    [InlineData("electrolink/profiles/us-1/avatar-other/photo", false)]
    [InlineData("electrolink/profiles/us-1/avatar/../../us-2/avatar/photo", false)]
    [InlineData("ELECTROLINK/profiles/us-1/avatar/photo", false)]
    [InlineData("", false)]
    public void Only_files_inside_the_owner_folder_are_deletable(string providerId, bool expected)
        => Assert.Equal(expected, FileStorageFolders.Contains(Folder, providerId));

    [Fact]
    public async Task Profile_photo_deletion_is_scoped_to_the_profile_owner()
    {
        string? scopedFolder = null;
        var provider = AssignmentHttpFixture.Proxy<IFileStorageProvider>((method, args) =>
        {
            scopedFolder = (string)args![1]!;
            return Task.FromResult(false);
        });

        await new FileStorageService(provider).DeleteProfilePhotoAsync("us-1", "electrolink/profiles/us-2/avatar/photo");

        Assert.Equal(Folder, scopedFolder);
    }
}
