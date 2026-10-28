using Hampcoders.Electrolink.API.Processing.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.Processing.Domain.Model.Entities;
using Hampcoders.Electrolink.API.Processing.Domain.Model.Events;
using Hampcoders.Electrolink.API.Processing.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;
using Xunit;

namespace Electrolink.Backend.Characterization.Tests.Processing;

public class DeviceReadingStreamTests
{
    private static readonly DateTime CapturedAt = new(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(EReadingSource.EdgeDevice)]
    [InlineData(EReadingSource.EdgeBufferReplay)]
    [InlineData(EReadingSource.EDGE)]
    public void IngestReading_PreservesMeasurementAndPublishesOneEvent(EReadingSource source)
    {
        var stream = CreateStream();
        var reading = CreateReading(stream, "reading-first", source);

        var accepted = stream.IngestReading(reading);

        Assert.True(accepted);
        var stored = Assert.Single(stream.Readings);
        Assert.Same(reading, stored);
        Assert.Equal("reading-first", stored.ReadingId.Value);
        Assert.Equal(stream.StreamId, stored.StreamId);
        Assert.Equal(CapturedAt, stored.Timestamp);
        Assert.Equal(220f, stored.Voltage);
        Assert.Equal(0.15f, stored.Current);
        Assert.Equal(0.95f, stored.PowerFactor);
        Assert.Equal(60f, stored.Frequency);
        Assert.Equal(source, stored.Source);
        Assert.Equal(CapturedAt, stream.LastReceivedAt);

        var published = Assert.IsType<ReadingIngestedEvent>(Assert.Single(stream.DomainEvents));
        Assert.Equal(stream.StreamId.Value, published.StreamId);
        Assert.Equal(stream.DeviceId.Value, published.DeviceId);
        Assert.Equal(stream.PropertyId.Value, published.PropertyId);
        Assert.Equal(stream.Owner.ClientId, published.OwnerId);
        Assert.Equal(stored.ReadingId.Value, published.ReadingId);
        Assert.Equal(CapturedAt, published.ReadingTimestamp);
        Assert.Equal(source.ToString(), published.Source);
    }

    [Fact]
    public void IngestReading_DuplicateIdentityDoesNotReplaceMeasurementOrPublishAgain()
    {
        var stream = CreateStream();
        var original = CreateReading(stream, "reading-duplicate");
        Assert.True(stream.IngestReading(original));
        var originalEvent = Assert.Single(stream.DomainEvents);
        // A retry can deserialize into a different instance with the same identifier.
        var retry = CreateReading(stream, "reading-duplicate", EReadingSource.EdgeBufferReplay);

        var accepted = stream.IngestReading(retry);

        Assert.False(accepted);
        Assert.Same(original, Assert.Single(stream.Readings));
        Assert.Same(originalEvent, Assert.Single(stream.DomainEvents));
        Assert.Equal(CapturedAt, stream.LastReceivedAt);
    }

    [Fact]
    public void IngestReading_DifferentIdentityAddsMeasurementAndItsOwnEvent()
    {
        var stream = CreateStream();
        var first = CreateReading(stream, "reading-first");
        var next = CreateReading(stream, "reading-next");
        Assert.True(stream.IngestReading(first));

        var accepted = stream.IngestReading(next);

        Assert.True(accepted);
        Assert.Collection(stream.Readings,
            reading => Assert.Same(first, reading),
            reading => Assert.Same(next, reading));
        Assert.Collection(stream.DomainEvents,
            ev => Assert.Equal("reading-first", Assert.IsType<ReadingIngestedEvent>(ev).ReadingId),
            ev => Assert.Equal("reading-next", Assert.IsType<ReadingIngestedEvent>(ev).ReadingId));
    }

    [Fact]
    public void IngestReading_PausedStreamPreservesHistoryAndRejectsMeasurement()
    {
        var stream = CreateStream();
        var previous = CreateReading(stream, "reading-before-pause");
        Assert.True(stream.IngestReading(previous));
        stream.Pause();
        var eventsBefore = stream.DomainEvents.ToArray();
        var next = CreateReading(stream, "reading-after-pause");

        var accepted = stream.IngestReading(next);

        Assert.False(accepted);
        Assert.Equal(EStreamStatus.Inactive, stream.StreamStatus);
        Assert.Same(previous, Assert.Single(stream.Readings));
        Assert.Equal(eventsBefore, stream.DomainEvents);
        Assert.Equal(CapturedAt, stream.LastReceivedAt);
    }

    private static DeviceReadingStream CreateStream() => DeviceReadingStream.Create(
        DeviceId.From("dev-characterization"),
        PropertyId.From("prop-characterization"),
        ClientIdentity.FromHomeowner("ho-characterization"));

    private static Reading CreateReading(
        DeviceReadingStream stream,
        string readingId,
        EReadingSource source = EReadingSource.EdgeDevice) => Reading.Create(
            ReadingId.From(readingId), stream.StreamId, CapturedAt,
            voltage: 220f, current: 0.15f, powerFactor: 0.95f, frequency: 60f, source: source);
}
