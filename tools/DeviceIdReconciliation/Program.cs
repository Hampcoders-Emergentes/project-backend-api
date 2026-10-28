using System.Text.Json;
using System.Security.Cryptography;
using System.Text.Json.Serialization;

namespace Electrolink.DeviceIdReconciliation;

public static class ReconciliationCli
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        WriteIndented = true
    };

    public static int Main(string[] args) => Run(args, Console.Out, Console.Error);

    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        if (args.Length != 2 || args[0] != "--input")
        {
            error.WriteLine("Usage: DeviceIdReconciliation --input <local-snapshot.json>");
            return 2;
        }
        try
        {
            using var stream = File.OpenRead(args[1]);
            if (stream.Length > 16 * 1024 * 1024) throw new InvalidDataException();
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            if (buffer.Length > 16 * 1024 * 1024) throw new InvalidDataException();
            var bytes = buffer.ToArray();
            using var document = JsonDocument.Parse(bytes);
            RejectDuplicateProperties(document.RootElement);
            var snapshot = document.RootElement.Deserialize<Snapshot>(Json) ?? throw new InvalidDataException();
            var report = Reconciler.Reconcile(snapshot) with { InputSha256 = Convert.ToHexString(SHA256.HashData(bytes)) };
            output.WriteLine(JsonSerializer.Serialize(report, Json));
            return 0;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            // Never echo file contents, paths or parser exception text: input must be sanitized.
            error.WriteLine("Invalid or unreadable snapshot; no report produced.");
            return 2;
        }
    }

    private static void RejectDuplicateProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new InvalidDataException();
                RejectDuplicateProperties(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray()) RejectDuplicateProperties(item);
    }
}
