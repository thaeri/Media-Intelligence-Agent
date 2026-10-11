using MediaIntelligence.Core.Interfaces;
using MediaIntelligence.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace MediaIntelligence.Core.Data;

public sealed class PostgreSqlMediaRepository : IMediaRepository
{
    private readonly MediaIntelligenceDbContext _dbContext;

    public PostgreSqlMediaRepository(MediaIntelligenceDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public IReadOnlyList<MediaContent> GetContents() =>
        _dbContext.MediaContents
            .AsNoTracking()
            .OrderBy(item => item.Id)
            .AsEnumerable()
            .Select(ToDomain)
            .ToArray();

    public MediaContent? GetContent(Guid contentId)
    {
        var entity = _dbContext.MediaContents
            .AsNoTracking()
            .SingleOrDefault(item => item.Id == contentId);
        return entity is null ? null : ToDomain(entity);
    }

    public IReadOnlyList<ViewingEvent> GetViewingEvents(Guid contentId) =>
        _dbContext.ViewingEvents
            .AsNoTracking()
            .Where(item => item.ContentId == contentId)
            .OrderBy(item => item.ViewedAtUtc)
            .ThenBy(item => item.Id)
            .AsEnumerable()
            .Select(item => new ViewingEvent(
                item.Id,
                item.ContentId,
                item.Region,
                item.DeviceType,
                item.ViewedAtUtc,
                item.WatchDurationSeconds,
                item.Completed,
                item.AdShown,
                item.DeviceId == null ? null : new DeviceId(item.DeviceId)))
            .ToArray();

    public IReadOnlyList<DeviceAudienceAssociation>
        GetDeviceAudienceAssociations() =>
        _dbContext.DeviceAudienceAssociations
            .AsNoTracking()
            .OrderBy(item => item.DeviceId)
            .ThenBy(item => item.AudienceId)
            .AsEnumerable()
            .Select(item => new DeviceAudienceAssociation(
                new DeviceId(item.DeviceId),
                new AudienceId(item.AudienceId)))
            .ToArray();

    public IReadOnlyList<AudienceConsent> GetAudienceConsents() =>
        _dbContext.AudienceConsents
            .AsNoTracking()
            .OrderBy(item => item.AudienceId)
            .AsEnumerable()
            .Select(item => new AudienceConsent(
                new AudienceId(item.AudienceId),
                (AudienceConsentStatus)item.Status))
            .ToArray();

    public IReadOnlyList<Campaign> GetCampaigns() =>
        _dbContext.Campaigns
            .AsNoTracking()
            .OrderBy(item => item.Id)
            .AsEnumerable()
            .Select(item => new Campaign(
                item.Id,
                item.Name,
                item.ContentId,
                item.StartsAtUtc,
                item.EndsAtUtc))
            .ToArray();

    public Campaign? GetCampaign(Guid campaignId)
    {
        var entity = _dbContext.Campaigns
            .AsNoTracking()
            .SingleOrDefault(item => item.Id == campaignId);
        return entity is null
            ? null
            : new Campaign(
                entity.Id,
                entity.Name,
                entity.ContentId,
                entity.StartsAtUtc,
                entity.EndsAtUtc);
    }

    public IReadOnlyList<AdImpression> GetAdImpressions(Guid campaignId) =>
        _dbContext.AdImpressions
            .AsNoTracking()
            .Where(item => item.CampaignId == campaignId)
            .OrderBy(item => item.OccurredAtUtc)
            .ThenBy(item => item.Id)
            .AsEnumerable()
            .Select(item => new AdImpression(
                item.Id,
                item.CampaignId,
                item.ContentId,
                item.OccurredAtUtc,
                item.DeviceId == null ? null : new DeviceId(item.DeviceId)))
            .ToArray();

    private static MediaContent ToDomain(MediaContentEntity item) =>
        new(
            item.Id,
            item.Title,
            item.Genre,
            item.ContentType,
            item.Region,
            item.ReleaseDate,
            item.Tags);
}
