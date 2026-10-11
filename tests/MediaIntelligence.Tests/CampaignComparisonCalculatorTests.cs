using MediaIntelligence.Core.Models;
using MediaIntelligence.Core.Services;

namespace MediaIntelligence.Tests;

public sealed class CampaignComparisonCalculatorTests
{
    [Fact]
    public void Calculate_ProducesBothAttributedDirectionsAndDeterministicDifferences()
    {
        CampaignMetricSnapshot[] metrics =
        [
            new("City Stories Launch", 5, 2, 2.5, 50),
            new("Desert Heroes Encore", 6, 2, 3.0, 60)
        ];

        var comparisons = CampaignComparisonCalculator.Calculate(metrics);

        var desertToCity = Assert.Single(
            comparisons,
            comparison => comparison.CampaignName == "Desert Heroes Encore");
        Assert.Equal("City Stories Launch", desertToCity.ComparedWithCampaignName);
        Assert.Equal(1, desertToCity.EligibleImpressionsDifference);
        Assert.Equal(CampaignComparisonCalculator.Higher, desertToCity.EligibleImpressionsDirection);
        Assert.Equal(0, desertToCity.ReachDifference);
        Assert.Equal(CampaignComparisonCalculator.Equal, desertToCity.ReachDirection);
        Assert.Equal(0.5, desertToCity.FrequencyDifference);
        Assert.Equal(CampaignComparisonCalculator.Higher, desertToCity.FrequencyDirection);
        Assert.Equal(10, desertToCity.CoveragePercentagePointDifference);
        Assert.Equal(CampaignComparisonCalculator.Higher, desertToCity.CoverageDirection);

        var cityToDesert = Assert.Single(
            comparisons,
            comparison => comparison.CampaignName == "City Stories Launch");
        Assert.Equal(CampaignComparisonCalculator.Lower, cityToDesert.EligibleImpressionsDirection);
        Assert.Equal(CampaignComparisonCalculator.Equal, cityToDesert.ReachDirection);
        Assert.Equal(CampaignComparisonCalculator.Lower, cityToDesert.FrequencyDirection);
        Assert.Equal(CampaignComparisonCalculator.Lower, cityToDesert.CoverageDirection);
    }
}
