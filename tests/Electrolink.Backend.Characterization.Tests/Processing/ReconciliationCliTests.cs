using System.Security.Cryptography;
using System.Text.Json;
using Electrolink.DeviceIdReconciliation;
using Xunit;

namespace Electrolink.Backend.Characterization.Tests.Processing;

public sealed class ReconciliationCliTests : IDisposable
{
    private readonly string directory = Directory.CreateTempSubdirectory("electrolink-m24-test-").FullName;
    private string FilePath => Path.Combine(directory, "snapshot.json");
    private static string ValidJson => """
        {"schemaVersion":1,"snapshotId":"synthetic","environment":"synthetic","assets":[],"observations":[],"mappings":[]}
        """;

    private (int Code, string Output, string Error) Run(string content)
    {
        File.WriteAllText(FilePath, content);
        using var output = new StringWriter();
        using var error = new StringWriter();
        var code = ReconciliationCli.Run(new[] { "--input", FilePath }, output, error);
        return (code, output.ToString(), error.ToString());
    }

    [Fact]
    public void CliProducesDeterministicReportWithoutChangingInputOrCreatingOtherFiles()
    {
        var first = Run(ValidJson);
        var hash = SHA256.HashData(File.ReadAllBytes(FilePath));
        using var output = new StringWriter();
        using var error = new StringWriter();
        Assert.Equal(0, ReconciliationCli.Run(new[] { "--input", FilePath }, output, error));
        Assert.Equal(first.Output, output.ToString());
        Assert.Equal(hash, SHA256.HashData(File.ReadAllBytes(FilePath)));
        Assert.Single(Directory.GetFiles(directory));
        using var json = JsonDocument.Parse(first.Output);
        Assert.False(json.RootElement.GetProperty("grantsAccess").GetBoolean());
        Assert.Equal(Convert.ToHexString(hash), json.RootElement.GetProperty("inputSha256").GetString());
        Assert.False(json.RootElement.TryGetProperty("allowedDeviceIds", out _));
        Assert.Equal(0, first.Code);
    }

    [Fact]
    public void UnknownFieldsIncludingCredentialsAreRejectedWithoutEchoingInput()
    {
        var result = Run(ValidJson.Replace("\"assets\":[]", "\"apiKey\":\"private-fixture-marker\",\"assets\":[]"));
        Assert.Equal(2, result.Code);
        Assert.Empty(result.Output);
        Assert.DoesNotContain("private-fixture-marker", result.Error);
        Assert.DoesNotContain(FilePath, result.Error);
    }

    [Fact]
    public void DuplicateJsonPropertiesAreRejectedInsteadOfLastValueWinning()
    {
        var result = Run(ValidJson.Replace("\"schemaVersion\":1", "\"schemaVersion\":1,\"schemaVersion\":2"));
        Assert.Equal(2, result.Code);
        Assert.Empty(result.Output);
    }

    [Fact]
    public void MalformedJsonDoesNotEchoFileContents()
    {
        var result = Run("{malformed-private-fixture-marker}");
        Assert.Equal(2, result.Code);
        Assert.Empty(result.Output);
        Assert.DoesNotContain("private-fixture-marker", result.Error);
    }

    [Theory]
    [InlineData("version")]
    [InlineData("null-array")]
    [InlineData("missing-evidence")]
    [InlineData("unknown-source")]
    public void InvalidSnapshotIsRejectedWithoutPartialReport(string problem)
    {
        var json = problem switch
        {
            "version" => ValidJson.Replace("\"schemaVersion\":1", "\"schemaVersion\":2"),
            "null-array" => ValidJson.Replace("\"assets\":[]", "\"assets\":null"),
            "missing-evidence" => ValidJson.Replace("\"assets\":[]", "\"assets\":[{\"recordId\":\"a\",\"internalId\":\"dev-a\",\"serialNumber\":\"a\",\"status\":\"Installed\",\"evidenceRef\":\"\"}]"),
            _ => ValidJson.Replace("\"observations\":[]", "\"observations\":[{\"recordId\":\"o\",\"source\":\"gateway\",\"deviceId\":\"dev-a\",\"evidenceRef\":\"e\"}]")
        };
        var result = Run(json);
        Assert.Equal(2, result.Code);
        Assert.Empty(result.Output);
    }

    [Fact]
    public void OutputFileOrConfigurationArgumentsAreNotSupported()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        Assert.Equal(2, ReconciliationCli.Run(new[] { "--input", FilePath, "--output", "appsettings.json" }, output, error));
        Assert.Empty(output.ToString());
        Assert.Empty(Directory.GetFiles(directory));
    }

    [Fact]
    public void MissingFileReportsGenericErrorWithoutPath()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        Assert.Equal(2, ReconciliationCli.Run(new[] { "--input", FilePath }, output, error));
        Assert.Empty(output.ToString());
        Assert.DoesNotContain(FilePath, error.ToString());
    }

    public void Dispose() => Directory.Delete(directory, recursive: true);
}
