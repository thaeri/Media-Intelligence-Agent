using System.Text.Json.Serialization;

namespace MediaIntelligence.Core.Models;

public static class StreamingTopics
{
    public const string ViewingEvents = "viewing-events.v1";
    public const string AdImpressions = "ad-impressions.v1";
    public const string DeadLetter = "media-events.dlq.v1";
    public const string SchemaVersion = "1.0";
}

public sealed record EventEnvelope<T>(
    string SchemaVersion,
    Guid EventId,
    DateTime EventTimeUtc,
    T Data);

public sealed record ViewingEventV1(
    Guid Id,
    Guid ContentId,
    string Region,
    string DeviceType,
    int WatchDurationSeconds,
    bool Completed,
    bool AdShown,
    string? DeviceId);

public sealed record AdImpressionV1(
    Guid Id,
    Guid CampaignId,
    Guid ContentId,
    string? DeviceId);

public sealed record DeadLetterEventV1(
    string SchemaVersion,
    string SourceTopic,
    int SourcePartition,
    long SourceOffset,
    string FailureCode,
    DateTime FailedAtUtc,
    string PayloadSha256,
    string OriginalValue);

public enum StreamProcessOutcome
{
    Inserted,
    Duplicate
}
