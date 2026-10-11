using MediaIntelligence.Core.Models;

namespace MediaIntelligence.Core.Services;

public static class CampaignComparisonCalculator
{
    public const string Higher = "Higher";
    public const string Lower = "Lower";
    public const string Equal = "Equal";

    public static IReadOnlyList<CampaignMetricComparison> Calculate(
        IEnumerable<CampaignMetricSnapshot> campaigns)
    {
        ArgumentNullException.ThrowIfNull(campaigns);
        var snapshots = campaigns
            .GroupBy(item => item.CampaignName, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();
        var comparisons = new List<CampaignMetricComparison>();

        foreach (var campaign in snapshots)
        {
            foreach (var other in snapshots)
            {
                if (string.Equals(
                    campaign.CampaignName,
                    other.CampaignName,
                    StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                comparisons.Add(new CampaignMetricComparison(
                    campaign.CampaignName,
                    other.CampaignName,
                    Math.Abs(campaign.EligibleImpressions - other.EligibleImpressions),
                    Direction(
                        campaign.EligibleImpressions,
                        other.EligibleImpressions),
                    Math.Abs(campaign.Reach - other.Reach),
                    Direction(campaign.Reach, other.Reach),
                    Math.Abs(campaign.Frequency - other.Frequency),
                    Direction(campaign.Frequency, other.Frequency),
                    Math.Abs(
                        campaign.EligibleExposureCoverageRate -
                        other.EligibleExposureCoverageRate),
                    Direction(
                        campaign.EligibleExposureCoverageRate,
                        other.EligibleExposureCoverageRate)));
            }
        }

        return comparisons;
    }

    private static string Direction(double value, double other) =>
        value > other
            ? Higher
            : value < other
                ? Lower
                : Equal;
}
