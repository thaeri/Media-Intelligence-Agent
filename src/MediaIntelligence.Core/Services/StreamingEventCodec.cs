using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MediaIntelligence.Core.Models;

namespace MediaIntelligence.Core.Services;

public sealed record ValidatedStreamEvent(
    Guid EventId,
    DateTime EventTimeUtc,
    ViewingEvent? ViewingEvent,
    AdImpression? AdImpression,
    string PayloadSha256);

public static class StreamingEventCodec
{
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public static ValidatedStreamEvent Decode(string topic, string value)
    {
        if (Encoding.UTF8.GetByteCount(value) > 64 * 1024)
            throw new StreamValidationException("payload_too_large");

        try
        {
            return topic switch
            {
                StreamingTopics.ViewingEvents => DecodeViewing(value),
                StreamingTopics.AdImpressions => DecodeAd(value),
                _ => throw new StreamValidationException("unsupported_topic")
            };
        }
        catch (JsonException)
        {
            throw new StreamValidationException("invalid_json");
        }
    }

    public static string Serialize<T>(EventEnvelope<T> value) =>
        JsonSerializer.Serialize(value, JsonOptions);

    public static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();

    private static ValidatedStreamEvent DecodeViewing(string value)
    {
        var envelope = JsonSerializer.Deserialize<EventEnvelope<ViewingEventV1>>(
            value, JsonOptions) ?? throw new StreamValidationException("empty_payload");
        ValidateEnvelope(envelope.SchemaVersion, envelope.EventId,
            envelope.EventTimeUtc, envelope.Data?.Id ?? Guid.Empty);
        var data = envelope.Data;
        if (data is null || data.ContentId == Guid.Empty ||
            string.IsNullOrWhiteSpace(data.Region) || data.Region.Length > 100 ||
            string.IsNullOrWhiteSpace(data.DeviceType) || data.DeviceType.Length > 100 ||
            data.WatchDurationSeconds < 0 || data.DeviceId?.Length > 200)
            throw new StreamValidationException("invalid_viewing_event");

        return new(envelope.EventId, envelope.EventTimeUtc,
            new ViewingEvent(data.Id, data.ContentId, data.Region, data.DeviceType,
                envelope.EventTimeUtc, data.WatchDurationSeconds, data.Completed,
                data.AdShown, data.DeviceId is null ? null : new DeviceId(data.DeviceId)),
            null, Hash(value));
    }

    private static ValidatedStreamEvent DecodeAd(string value)
    {
        var envelope = JsonSerializer.Deserialize<EventEnvelope<AdImpressionV1>>(
            value, JsonOptions) ?? throw new StreamValidationException("empty_payload");
        ValidateEnvelope(envelope.SchemaVersion, envelope.EventId,
            envelope.EventTimeUtc, envelope.Data?.Id ?? Guid.Empty);
        var data = envelope.Data;
        if (data is null || data.CampaignId == Guid.Empty || data.ContentId == Guid.Empty ||
            data.DeviceId?.Length > 200)
            throw new StreamValidationException("invalid_ad_impression");

        return new(envelope.EventId, envelope.EventTimeUtc, null,
            new AdImpression(data.Id, data.CampaignId, data.ContentId,
                envelope.EventTimeUtc,
                data.DeviceId is null ? null : new DeviceId(data.DeviceId)), Hash(value));
    }

    private static void ValidateEnvelope(
        string version, Guid eventId, DateTime eventTimeUtc, Guid dataId)
    {
        if (!string.Equals(version, StreamingTopics.SchemaVersion, StringComparison.Ordinal))
            throw new StreamValidationException("unsupported_schema");
        if (eventId == Guid.Empty || eventId != dataId)
            throw new StreamValidationException("invalid_event_id");
        if (eventTimeUtc.Kind != DateTimeKind.Utc)
            throw new StreamValidationException("event_time_not_utc");
    }
}

public sealed class StreamValidationException : Exception
{
    public StreamValidationException(string failureCode) : base(failureCode) =>
        FailureCode = failureCode;
    public string FailureCode { get; }
}
