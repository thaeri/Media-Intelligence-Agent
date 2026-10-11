using Microsoft.EntityFrameworkCore;

namespace MediaIntelligence.Core.Data;

public static class PostgreSqlDatabaseInitializer
{
    public static async Task MigrateAndSeedAsync(
        MediaIntelligenceDbContext dbContext,
        CancellationToken cancellationToken = default)
    {
        await dbContext.Database.MigrateAsync(cancellationToken);
        await SeedAsync(dbContext, cancellationToken);
    }

    public static async Task SeedAsync(
        MediaIntelligenceDbContext dbContext,
        CancellationToken cancellationToken = default)
    {
        var source = new SyntheticMediaRepository();

        foreach (var item in source.GetContents())
        {
            var entity = await dbContext.MediaContents.FindAsync(
                [item.Id], cancellationToken);
            if (entity is null)
            {
                dbContext.MediaContents.Add(new MediaContentEntity
                {
                    Id = item.Id,
                    Title = item.Title,
                    Genre = item.Genre,
                    ContentType = item.ContentType,
                    Region = item.Region,
                    ReleaseDate = item.ReleaseDate,
                    Tags = item.Tags.ToArray()
                });
            }
            else
            {
                entity.Title = item.Title;
                entity.Genre = item.Genre;
                entity.ContentType = item.ContentType;
                entity.Region = item.Region;
                entity.ReleaseDate = item.ReleaseDate;
                entity.Tags = item.Tags.ToArray();
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        foreach (var content in source.GetContents())
        {
            foreach (var item in source.GetViewingEvents(content.Id))
            {
                if (!await dbContext.ViewingEvents.AnyAsync(
                        entity => entity.Id == item.Id,
                        cancellationToken))
                {
                    dbContext.ViewingEvents.Add(new ViewingEventEntity
                    {
                        Id = item.Id,
                        ContentId = item.ContentId,
                        Region = item.Region,
                        DeviceType = item.DeviceType,
                        ViewedAtUtc = EnsureUtc(item.ViewedAt),
                        WatchDurationSeconds = item.WatchDurationSeconds,
                        Completed = item.Completed,
                        AdShown = item.AdShown,
                        DeviceId = item.DeviceId?.Value
                    });
                }
            }
        }

        var associations = source.GetDeviceAudienceAssociations();
        var consents = source.GetAudienceConsents();
        var audienceIds = associations.Select(item => item.AudienceId.Value)
            .Concat(consents.Select(item => item.AudienceId.Value))
            .Distinct(StringComparer.Ordinal);
        foreach (var audienceId in audienceIds)
        {
            if (!await dbContext.Audiences.AnyAsync(
                    item => item.Id == audienceId,
                    cancellationToken))
            {
                dbContext.Audiences.Add(new AudienceEntity { Id = audienceId });
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        foreach (var item in associations)
        {
            var deviceId = item.DeviceId.Value;
            var audienceId = item.AudienceId.Value;
            if (!await dbContext.DeviceAudienceAssociations.AnyAsync(
                    entity => entity.DeviceId == deviceId &&
                              entity.AudienceId == audienceId,
                    cancellationToken))
            {
                dbContext.DeviceAudienceAssociations.Add(
                    new DeviceAudienceAssociationEntity
                    {
                        DeviceId = deviceId,
                        AudienceId = audienceId
                    });
            }
        }

        foreach (var item in consents)
        {
            var audienceId = item.AudienceId.Value;
            var entity = await dbContext.AudienceConsents.FindAsync(
                [audienceId], cancellationToken);
            if (entity is null)
            {
                dbContext.AudienceConsents.Add(new AudienceConsentEntity
                {
                    AudienceId = audienceId,
                    Status = (int)item.Status
                });
            }
            else
            {
                entity.Status = (int)item.Status;
            }
        }

        foreach (var item in source.GetCampaigns())
        {
            var entity = await dbContext.Campaigns.FindAsync(
                [item.Id], cancellationToken);
            if (entity is null)
            {
                dbContext.Campaigns.Add(new CampaignEntity
                {
                    Id = item.Id,
                    Name = item.Name,
                    ContentId = item.ContentId,
                    StartsAtUtc = item.StartsAtUtc,
                    EndsAtUtc = item.EndsAtUtc
                });
            }
            else
            {
                entity.Name = item.Name;
                entity.ContentId = item.ContentId;
                entity.StartsAtUtc = item.StartsAtUtc;
                entity.EndsAtUtc = item.EndsAtUtc;
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        foreach (var campaign in source.GetCampaigns())
        {
            foreach (var item in source.GetAdImpressions(campaign.Id))
            {
                if (!await dbContext.AdImpressions.AnyAsync(
                        entity => entity.Id == item.Id,
                        cancellationToken))
                {
                    dbContext.AdImpressions.Add(new AdImpressionEntity
                    {
                        Id = item.Id,
                        CampaignId = item.CampaignId,
                        ContentId = item.ContentId,
                        OccurredAtUtc = item.OccurredAtUtc,
                        DeviceId = item.DeviceId?.Value
                    });
                }
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static DateTime EnsureUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc
            ? value
            : DateTime.SpecifyKind(value, DateTimeKind.Utc);
}
