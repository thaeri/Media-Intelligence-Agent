namespace MediaIntelligence.Core.Models
{
    public sealed record ViewingEvent(
        Guid Id,
        Guid ContentId,
        string Region,
        string DeviceType,
        DateTime ViewedAt,
        int WatchDurationSeconds,
        bool Completed,
        bool AdShown
    );
}