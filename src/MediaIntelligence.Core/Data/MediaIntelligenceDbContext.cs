using Microsoft.EntityFrameworkCore;
using Pgvector;

namespace MediaIntelligence.Core.Data;

public sealed class MediaIntelligenceDbContext : DbContext
{
    public MediaIntelligenceDbContext(
        DbContextOptions<MediaIntelligenceDbContext> options)
        : base(options)
    {
    }

    internal DbSet<MediaContentEntity> MediaContents => Set<MediaContentEntity>();
    internal DbSet<ViewingEventEntity> ViewingEvents => Set<ViewingEventEntity>();
    internal DbSet<AudienceEntity> Audiences => Set<AudienceEntity>();
    internal DbSet<DeviceAudienceAssociationEntity> DeviceAudienceAssociations =>
        Set<DeviceAudienceAssociationEntity>();
    internal DbSet<AudienceConsentEntity> AudienceConsents => Set<AudienceConsentEntity>();
    internal DbSet<CampaignEntity> Campaigns => Set<CampaignEntity>();
    internal DbSet<AdImpressionEntity> AdImpressions => Set<AdImpressionEntity>();
    internal DbSet<MediaEmbeddingEntity> MediaEmbeddings => Set<MediaEmbeddingEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("vector");

        modelBuilder.Entity<MediaContentEntity>(entity =>
        {
            entity.ToTable("media_content");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Title).HasMaxLength(200).IsRequired();
            entity.Property(item => item.Genre).HasMaxLength(100).IsRequired();
            entity.Property(item => item.ContentType).HasMaxLength(100).IsRequired();
            entity.Property(item => item.Region).HasMaxLength(100).IsRequired();
            entity.Property(item => item.ReleaseDate).HasColumnType("date");
            entity.Property(item => item.Tags).HasColumnType("text[]").IsRequired();
            entity.HasIndex(item => item.Title).IsUnique();
        });

        modelBuilder.Entity<ViewingEventEntity>(entity =>
        {
            entity.ToTable("viewing_event");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Region).HasMaxLength(100).IsRequired();
            entity.Property(item => item.DeviceType).HasMaxLength(100).IsRequired();
            entity.Property(item => item.ViewedAtUtc).HasColumnType("timestamp with time zone");
            entity.Property(item => item.DeviceId).HasMaxLength(200);
            entity.HasOne<MediaContentEntity>()
                .WithMany()
                .HasForeignKey(item => item.ContentId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(item => new { item.ContentId, item.ViewedAtUtc });
            entity.ToTable(table => table.HasCheckConstraint(
                "CK_viewing_event_watch_duration",
                "\"WatchDurationSeconds\" >= 0"));
        });

        modelBuilder.Entity<AudienceEntity>(entity =>
        {
            entity.ToTable("audience");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Id).HasMaxLength(200);
        });

        modelBuilder.Entity<DeviceAudienceAssociationEntity>(entity =>
        {
            entity.ToTable("device_audience_association");
            entity.HasKey(item => new { item.DeviceId, item.AudienceId });
            entity.Property(item => item.DeviceId).HasMaxLength(200);
            entity.Property(item => item.AudienceId).HasMaxLength(200);
            entity.HasOne<AudienceEntity>()
                .WithMany()
                .HasForeignKey(item => item.AudienceId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(item => item.AudienceId);
        });

        modelBuilder.Entity<AudienceConsentEntity>(entity =>
        {
            entity.ToTable("audience_consent");
            entity.HasKey(item => item.AudienceId);
            entity.Property(item => item.AudienceId).HasMaxLength(200);
            entity.HasOne<AudienceEntity>()
                .WithOne()
                .HasForeignKey<AudienceConsentEntity>(item => item.AudienceId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.ToTable(table => table.HasCheckConstraint(
                "CK_audience_consent_status",
                "\"Status\" >= 0 AND \"Status\" <= 2"));
        });

        modelBuilder.Entity<CampaignEntity>(entity =>
        {
            entity.ToTable("campaign");
            entity.HasKey(item => item.Id);
            entity.HasAlternateKey(item => new { item.Id, item.ContentId });
            entity.Property(item => item.Name).HasMaxLength(200).IsRequired();
            entity.Property(item => item.StartsAtUtc).HasColumnType("timestamp with time zone");
            entity.Property(item => item.EndsAtUtc).HasColumnType("timestamp with time zone");
            entity.HasOne<MediaContentEntity>()
                .WithMany()
                .HasForeignKey(item => item.ContentId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(item => item.ContentId);
            entity.HasIndex(item => item.Name).IsUnique();
            entity.ToTable(table => table.HasCheckConstraint(
                "CK_campaign_utc_range",
                "\"EndsAtUtc\" > \"StartsAtUtc\""));
        });

        modelBuilder.Entity<AdImpressionEntity>(entity =>
        {
            entity.ToTable("ad_impression");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.OccurredAtUtc).HasColumnType("timestamp with time zone");
            entity.Property(item => item.DeviceId).HasMaxLength(200);
            entity.HasOne<CampaignEntity>()
                .WithMany()
                .HasForeignKey(item => new { item.CampaignId, item.ContentId })
                .HasPrincipalKey(item => new { item.Id, item.ContentId })
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<MediaContentEntity>()
                .WithMany()
                .HasForeignKey(item => item.ContentId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(item => new { item.CampaignId, item.OccurredAtUtc });
            entity.HasIndex(item => item.ContentId);
        });

        modelBuilder.Entity<MediaEmbeddingEntity>(entity =>
        {
            entity.ToTable("media_embedding");
            entity.HasKey(item => new { item.ContentId, item.Model });
            entity.Property(item => item.Model).HasMaxLength(200);
            entity.Property(item => item.ContentHash).HasMaxLength(64);
            entity.Property(item => item.UpdatedAtUtc)
                .HasColumnType("timestamp with time zone");
            entity.Property(item => item.Embedding).HasColumnType("vector");
            entity.HasOne<MediaContentEntity>()
                .WithMany()
                .HasForeignKey(item => item.ContentId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(item => new { item.Model, item.Dimension });
            entity.ToTable(table => table.HasCheckConstraint(
                "CK_media_embedding_dimension",
                "\"Dimension\" > 0"));
            entity.ToTable(table => table.HasCheckConstraint(
                "CK_media_embedding_content_version",
                "\"ContentVersion\" > 0"));
        });
    }
}

internal sealed class MediaContentEntity
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Genre { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public string Region { get; set; } = string.Empty;
    public DateTime ReleaseDate { get; set; }
    public string[] Tags { get; set; } = [];
}

internal sealed class ViewingEventEntity
{
    public Guid Id { get; set; }
    public Guid ContentId { get; set; }
    public string Region { get; set; } = string.Empty;
    public string DeviceType { get; set; } = string.Empty;
    public DateTime ViewedAtUtc { get; set; }
    public int WatchDurationSeconds { get; set; }
    public bool Completed { get; set; }
    public bool AdShown { get; set; }
    public string? DeviceId { get; set; }
}

internal sealed class AudienceEntity
{
    public string Id { get; set; } = string.Empty;
}

internal sealed class DeviceAudienceAssociationEntity
{
    public string DeviceId { get; set; } = string.Empty;
    public string AudienceId { get; set; } = string.Empty;
}

internal sealed class AudienceConsentEntity
{
    public string AudienceId { get; set; } = string.Empty;
    public int Status { get; set; }
}

internal sealed class CampaignEntity
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public Guid ContentId { get; set; }
    public DateTime StartsAtUtc { get; set; }
    public DateTime EndsAtUtc { get; set; }
}

internal sealed class AdImpressionEntity
{
    public Guid Id { get; set; }
    public Guid CampaignId { get; set; }
    public Guid ContentId { get; set; }
    public DateTime OccurredAtUtc { get; set; }
    public string? DeviceId { get; set; }
}

internal sealed class MediaEmbeddingEntity
{
    public Guid ContentId { get; set; }
    public string Model { get; set; } = string.Empty;
    public int Dimension { get; set; }
    public string ContentHash { get; set; } = string.Empty;
    public int ContentVersion { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public Vector Embedding { get; set; } = new(Array.Empty<float>());
}
