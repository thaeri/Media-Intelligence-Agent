using MediaIntelligence.Core.Data;
using MediaIntelligence.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace MediaIntelligence.Core.Services;

public sealed class StreamingEventProcessor
{
    private readonly MediaIntelligenceDbContext _dbContext;

    public StreamingEventProcessor(MediaIntelligenceDbContext dbContext) =>
        _dbContext = dbContext;

    public async Task<StreamProcessOutcome> ProcessAsync(
        string topic,
        ValidatedStreamEvent message,
        CancellationToken cancellationToken)
    {
        await using var transaction = await _dbContext.Database
            .BeginTransactionAsync(cancellationToken);
        var prior = await _dbContext.StreamIngestions
            .SingleOrDefaultAsync(item => item.Topic == topic &&
                item.EventId == message.EventId, cancellationToken);
        if (prior is not null)
        {
            if (!string.Equals(prior.PayloadSha256, message.PayloadSha256,
                    StringComparison.Ordinal))
                throw new StreamValidationException("event_id_payload_conflict");
            await transaction.CommitAsync(cancellationToken);
            return StreamProcessOutcome.Duplicate;
        }

        if (message.ViewingEvent is { } viewing)
        {
            if (!await _dbContext.MediaContents.AnyAsync(
                    item => item.Id == viewing.ContentId, cancellationToken))
                throw new StreamValidationException("unknown_content");
            if (await _dbContext.ViewingEvents.AnyAsync(
                    item => item.Id == viewing.Id, cancellationToken))
                throw new StreamValidationException("event_id_already_exists");
            _dbContext.ViewingEvents.Add(new ViewingEventEntity
            {
                Id = viewing.Id,
                ContentId = viewing.ContentId,
                Region = viewing.Region,
                DeviceType = viewing.DeviceType,
                ViewedAtUtc = viewing.ViewedAt,
                WatchDurationSeconds = viewing.WatchDurationSeconds,
                Completed = viewing.Completed,
                AdShown = viewing.AdShown,
                DeviceId = viewing.DeviceId?.Value
            });
        }
        else if (message.AdImpression is { } impression)
        {
            if (!await _dbContext.Campaigns.AnyAsync(
                    item => item.Id == impression.CampaignId &&
                        item.ContentId == impression.ContentId, cancellationToken))
                throw new StreamValidationException("unknown_campaign_content");
            if (await _dbContext.AdImpressions.AnyAsync(
                    item => item.Id == impression.Id, cancellationToken))
                throw new StreamValidationException("event_id_already_exists");
            _dbContext.AdImpressions.Add(new AdImpressionEntity
            {
                Id = impression.Id,
                CampaignId = impression.CampaignId,
                ContentId = impression.ContentId,
                OccurredAtUtc = impression.OccurredAtUtc,
                DeviceId = impression.DeviceId?.Value
            });
        }
        else
        {
            throw new StreamValidationException("unsupported_event");
        }

        _dbContext.StreamIngestions.Add(new StreamIngestionEntity
        {
            Topic = topic,
            EventId = message.EventId,
            PayloadSha256 = message.PayloadSha256,
            EventTimeUtc = message.EventTimeUtc,
            ProcessedAtUtc = DateTime.UtcNow
        });
        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return StreamProcessOutcome.Inserted;
    }
}
