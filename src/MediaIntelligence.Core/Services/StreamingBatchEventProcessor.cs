using MediaIntelligence.Core.Data;
using MediaIntelligence.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace MediaIntelligence.Core.Services;

public sealed record StreamBatchItem(string Topic, ValidatedStreamEvent Message);
public sealed record StreamBatchItemResult(StreamProcessOutcome? Outcome, string? FailureCode);

public sealed class StreamingBatchEventProcessor
{
    private readonly MediaIntelligenceDbContext _dbContext;

    public StreamingBatchEventProcessor(MediaIntelligenceDbContext dbContext) =>
        _dbContext = dbContext;

    public async Task<IReadOnlyList<StreamBatchItemResult>> ProcessAsync(
        IReadOnlyList<StreamBatchItem> batch,
        CancellationToken cancellationToken)
    {
        if (batch.Count == 0) return [];
        await using var transaction = await _dbContext.Database
            .BeginTransactionAsync(cancellationToken);

        var eventIds = batch.Select(item => item.Message.EventId).Distinct().ToArray();
        var topics = batch.Select(item => item.Topic).Distinct().ToArray();
        var priorByKey = (await _dbContext.StreamIngestions
            .Where(item => topics.Contains(item.Topic) && eventIds.Contains(item.EventId))
            .ToListAsync(cancellationToken)).ToDictionary(item => (item.Topic, item.EventId));

        var contentIds = batch.Where(item => item.Message.ViewingEvent is not null)
            .Select(item => item.Message.ViewingEvent!.ContentId).Distinct().ToArray();
        var knownContentIds = (await _dbContext.MediaContents
            .Where(item => contentIds.Contains(item.Id)).Select(item => item.Id)
            .ToListAsync(cancellationToken)).ToHashSet();
        var campaignIds = batch.Where(item => item.Message.AdImpression is not null)
            .Select(item => item.Message.AdImpression!.CampaignId).Distinct().ToArray();
        var knownCampaigns = (await _dbContext.Campaigns
            .Where(item => campaignIds.Contains(item.Id))
            .Select(item => new { item.Id, item.ContentId }).ToListAsync(cancellationToken))
            .Select(item => (item.Id, item.ContentId)).ToHashSet();

        var viewingIds = batch.Where(item => item.Message.ViewingEvent is not null)
            .Select(item => item.Message.ViewingEvent!.Id).Distinct().ToArray();
        var usedViewingIds = (await _dbContext.ViewingEvents
            .Where(item => viewingIds.Contains(item.Id)).Select(item => item.Id)
            .ToListAsync(cancellationToken)).ToHashSet();
        var impressionIds = batch.Where(item => item.Message.AdImpression is not null)
            .Select(item => item.Message.AdImpression!.Id).Distinct().ToArray();
        var usedImpressionIds = (await _dbContext.AdImpressions
            .Where(item => impressionIds.Contains(item.Id)).Select(item => item.Id)
            .ToListAsync(cancellationToken)).ToHashSet();

        var seen = new Dictionary<(string Topic, Guid EventId), string>();
        var results = new StreamBatchItemResult[batch.Count];
        var processedAtUtc = DateTime.UtcNow;
        for (var index = 0; index < batch.Count; index++)
        {
            var item = batch[index];
            var message = item.Message;
            var key = (item.Topic, message.EventId);
            if (priorByKey.TryGetValue(key, out var prior))
            {
                results[index] = SameHash(prior.PayloadSha256, message.PayloadSha256);
                continue;
            }
            if (seen.TryGetValue(key, out var priorHash))
            {
                results[index] = SameHash(priorHash, message.PayloadSha256);
                continue;
            }

            if (message.ViewingEvent is { } viewing)
            {
                if (!knownContentIds.Contains(viewing.ContentId))
                {
                    results[index] = new(null, "unknown_content");
                    continue;
                }
                if (!usedViewingIds.Add(viewing.Id))
                {
                    results[index] = new(null, "event_id_already_exists");
                    continue;
                }
                _dbContext.ViewingEvents.Add(new ViewingEventEntity
                {
                    Id = viewing.Id, ContentId = viewing.ContentId,
                    Region = viewing.Region, DeviceType = viewing.DeviceType,
                    ViewedAtUtc = viewing.ViewedAt,
                    WatchDurationSeconds = viewing.WatchDurationSeconds,
                    Completed = viewing.Completed, AdShown = viewing.AdShown,
                    DeviceId = viewing.DeviceId?.Value
                });
            }
            else if (message.AdImpression is { } impression)
            {
                if (!knownCampaigns.Contains((impression.CampaignId, impression.ContentId)))
                {
                    results[index] = new(null, "unknown_campaign_content");
                    continue;
                }
                if (!usedImpressionIds.Add(impression.Id))
                {
                    results[index] = new(null, "event_id_already_exists");
                    continue;
                }
                _dbContext.AdImpressions.Add(new AdImpressionEntity
                {
                    Id = impression.Id, CampaignId = impression.CampaignId,
                    ContentId = impression.ContentId,
                    OccurredAtUtc = impression.OccurredAtUtc,
                    DeviceId = impression.DeviceId?.Value
                });
            }
            else
            {
                results[index] = new(null, "unsupported_event");
                continue;
            }

            seen.Add(key, message.PayloadSha256);
            _dbContext.StreamIngestions.Add(new StreamIngestionEntity
            {
                Topic = item.Topic, EventId = message.EventId,
                PayloadSha256 = message.PayloadSha256,
                EventTimeUtc = message.EventTimeUtc, ProcessedAtUtc = processedAtUtc
            });
            results[index] = new(StreamProcessOutcome.Inserted, null);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return results;
    }

    private static StreamBatchItemResult SameHash(string left, string right) =>
        string.Equals(left, right, StringComparison.Ordinal)
            ? new(StreamProcessOutcome.Duplicate, null)
            : new(null, "event_id_payload_conflict");
}
