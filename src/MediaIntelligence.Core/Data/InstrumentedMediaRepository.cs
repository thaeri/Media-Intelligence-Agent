using System.Diagnostics;
using System.Diagnostics.Metrics;
using MediaIntelligence.Core.Interfaces;
using MediaIntelligence.Core.Models;
using MediaIntelligence.Core.Services;

namespace MediaIntelligence.Core.Data;

public sealed class InstrumentedMediaRepository : IMediaRepository
{
    private static readonly Meter Meter = new(AgentTelemetryService.MeterName);
    private static readonly Histogram<double> Duration = Meter.CreateHistogram<double>(
        "media.storage.operation.duration", "s", "Repository operation duration");

    private readonly IMediaRepository _inner;
    private readonly string _provider;

    public InstrumentedMediaRepository(IMediaRepository inner, string provider)
    {
        _inner = inner;
        _provider = provider;
    }

    public IReadOnlyList<MediaContent> GetContents() => Measure(
        nameof(GetContents), _inner.GetContents);

    public MediaContent? GetContent(Guid contentId) => Measure(
        nameof(GetContent), () => _inner.GetContent(contentId));

    public IReadOnlyList<ViewingEvent> GetViewingEvents(Guid contentId) => Measure(
        nameof(GetViewingEvents), () => _inner.GetViewingEvents(contentId));

    public IReadOnlyList<DeviceAudienceAssociation> GetDeviceAudienceAssociations() =>
        Measure(nameof(GetDeviceAudienceAssociations),
            _inner.GetDeviceAudienceAssociations);

    public IReadOnlyList<AudienceConsent> GetAudienceConsents() => Measure(
        nameof(GetAudienceConsents), _inner.GetAudienceConsents);

    public IReadOnlyList<Campaign> GetCampaigns() => Measure(
        nameof(GetCampaigns), _inner.GetCampaigns);

    public Campaign? GetCampaign(Guid campaignId) => Measure(
        nameof(GetCampaign), () => _inner.GetCampaign(campaignId));

    public IReadOnlyList<AdImpression> GetAdImpressions(Guid campaignId) => Measure(
        nameof(GetAdImpressions), () => _inner.GetAdImpressions(campaignId));

    private T Measure<T>(string operation, Func<T> action)
    {
        var startedAt = Stopwatch.GetTimestamp();
        var succeeded = false;
        try
        {
            var result = action();
            succeeded = true;
            return result;
        }
        finally
        {
            Duration.Record(Stopwatch.GetElapsedTime(startedAt).TotalSeconds,
                new TagList
                {
                    { "storage.provider", _provider },
                    { "storage.operation", operation },
                    { "storage.outcome", succeeded ? "completed" : "failed" }
                });
        }
    }
}
