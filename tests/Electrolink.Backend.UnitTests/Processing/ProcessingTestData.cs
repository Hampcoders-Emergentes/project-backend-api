using Hampcoders.Electrolink.API.Processing.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.Processing.Domain.Model.Entities;
using Hampcoders.Electrolink.API.Processing.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;

namespace Electrolink.Backend.UnitTests.Processing;

internal static class ProcessingTestData
{
    public const string Device = "dev-1";
    public const string Homeowner = "ho-1";
    public static readonly ClientIdentity Owner = ClientIdentity.FromHomeowner(Homeowner);
    public static readonly PropertyId Property = PropertyId.From("prop-1");

    public static DeviceReadingStream Stream(ClientIdentity? owner = null, string device = Device)
    {
        var stream = DeviceReadingStream.Create(DeviceId.From(device), Property, owner ?? Owner);
        stream.ClearDomainEvents();
        return stream;
    }

    public static Reading NormalReading(DeviceReadingStream stream, string id, DateTime? at = null) =>
        Reading.Create(ReadingId.From(id), stream.StreamId, at ?? DateTime.UtcNow, 220, 5, 0.95f, 60, EReadingSource.EdgeDevice);

    /// <summary>Stream holding one ingested reading that the anomaly rules flag as the given condition.</summary>
    public static DeviceReadingStream StreamWithReading(string readingId, float voltage = 220, float current = 5, float powerFactor = 0.95f)
    {
        var stream = Stream();
        stream.IngestReading(Reading.Create(ReadingId.From(readingId), stream.StreamId, DateTime.UtcNow, voltage, current, powerFactor, 60,
            EReadingSource.EdgeDevice));
        stream.ClearDomainEvents();
        return stream;
    }

    public static AnomalyRecord Anomaly(EAnomalyType type = EAnomalyType.Overcurrent, EAnomalySeverity severity = EAnomalySeverity.High)
    {
        var anomaly = AnomalyRecord.Detect(DeviceId.From(Device), Property, Owner, type, severity, ReadingId.From("rd-0"));
        anomaly.ClearDomainEvents();
        return anomaly;
    }

    public static RelayControlCommand SentRelayCommand()
    {
        var command = RelayControlCommand.IssueBySystem(DeviceId.From(Device), Property, ERelayTarget.Open);
        command.MarkAsSent();
        command.ClearDomainEvents();
        return command;
    }
}
