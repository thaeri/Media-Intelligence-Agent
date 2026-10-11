using MediaIntelligence.Core.Services;

namespace MediaIntelligence.Tests;

public sealed class EvidenceReviewerServiceTests
{
    private readonly EvidenceReviewerService _reviewer = new();

    [Fact]
    public void Review_AcceptsClaimsThatMatchToolEvidence()
    {
        var result = _reviewer.Review(
            "The completion rate is 66.7% across 100 viewing sessions.",
            [AnalyticsResult(completionRate: 66.6666667, sessions: 100)]);

        Assert.True(
            result.IsVerified,
            string.Join(", ", result.UnsupportedClaims));
        Assert.Empty(result.UnsupportedClaims);
    }

    [Fact]
    public void Review_RejectsUnsupportedNumericalClaims()
    {
        var result = _reviewer.Review(
            "The completion rate is 75% across 120 sessions.",
            [AnalyticsResult(completionRate: 66.6666667, sessions: 100)]);

        Assert.False(result.IsVerified);
        Assert.Equal(["75%", "120 sessions"], result.UnsupportedClaims);
    }

    [Fact]
    public void Review_RejectsNumbersWhenNoToolEvidenceExists()
    {
        var result = _reviewer.Review(
            "There were 100 sessions.",
            []);

        Assert.False(result.IsVerified);
        Assert.Equal(["100 sessions"], result.UnsupportedClaims);
    }

    [Fact]
    public void Review_IgnoresResponsesWithoutNumericalClaims()
    {
        var result = _reviewer.Review(
            "The dataset is synthetic and more evidence is required.",
            []);

        Assert.True(result.IsVerified);
    }

    [Fact]
    public void CreateVerifiedResponse_BlocksTheUnverifiedDraft()
    {
        var response = _reviewer.CreateVerifiedResponse(
            "Completion increased to 90%.",
            [AnalyticsResult(completionRate: 60, sessions: 100)]);

        Assert.DoesNotContain("increased to 90%", response);
        Assert.Contains("unsupported numerical claims (90%)", response);
    }

    [Fact]
    public void Review_UsesAllComparisonToolResults()
    {
        const string comparisonResult = """
            {
              "DataSource": "Synthetic viewing events",
              "Results": [
                { "ViewingSessions": 100, "CompletionRate": 63, "AdExposureRate": 81 },
                { "ViewingSessions": 100, "CompletionRate": 71, "AdExposureRate": 79 }
              ]
            }
            """;

        var result = _reviewer.Review(
            "The highest completion rate is 71%.",
            [comparisonResult]);

        Assert.True(result.IsVerified);
    }

    [Fact]
    public void Review_RejectsMetricAttributedToTheWrongTitle()
    {
        var result = _reviewer.Review(
            "City Stories has a 70% completion rate.",
            [CrossTitleComparisonResult]);

        Assert.False(result.IsVerified);
        Assert.Equal(["70%"], result.UnsupportedClaims);
    }

    [Fact]
    public void Review_AcceptsCorrectlyAttributedComparisonMetrics()
    {
        var result = _reviewer.Review(
            "City Stories has a 60% completion rate, while Desert Heroes has 70%.",
            [CrossTitleComparisonResult]);

        Assert.True(
            result.IsVerified,
            string.Join(", ", result.UnsupportedClaims));
    }

    [Fact]
    public void Review_IgnoresMalformedToolOutput()
    {
        var result = _reviewer.Review("The rate is 50%.", ["not-json"]);

        Assert.False(result.IsVerified);
    }

    [Fact]
    public void Review_RejectsAudienceMetricBorrowedFromAnotherMetric()
    {
        var result = _reviewer.Review(
            "City Stories has reach of 1.5.",
            [AudienceIdentityEvidence]);

        Assert.False(result.IsVerified);
        Assert.Equal(["1.5"], result.UnsupportedClaims);
    }

    [Fact]
    public void Review_RejectsAudienceMetricBorrowedFromAnotherTitle()
    {
        var result = _reviewer.Review(
            "City Stories has reach of 1.",
            [AudienceIdentityEvidence]);

        Assert.False(result.IsVerified);
        Assert.Equal(["1"], result.UnsupportedClaims);
    }

    [Fact]
    public void Review_AcceptsCorrectAudienceMetricAttribution()
    {
        var result = _reviewer.Review(
            "City Stories has reach of 2 and frequency of 1.5.",
            [AudienceIdentityEvidence]);

        Assert.True(
            result.IsVerified,
            string.Join(", ", result.UnsupportedClaims));
    }

    [Fact]
    public void Review_RejectsMetricAttributedToWrongCampaign()
    {
        var result = _reviewer.Review(
            "City Stories Launch has 6 eligible impressions.",
            [CampaignComparisonEvidence]);

        Assert.False(result.IsVerified);
        Assert.Equal(["6"], result.UnsupportedClaims);
    }

    [Fact]
    public void Review_RejectsTotalImpressionsBorrowedAsEligibleImpressions()
    {
        var result = _reviewer.Review(
            "City Stories Launch has 10 eligible impressions.",
            [CampaignComparisonEvidence]);

        Assert.False(result.IsVerified);
        Assert.Equal(["10"], result.UnsupportedClaims);
    }

    [Fact]
    public void Review_AcceptsCorrectCampaignComparisonAttribution()
    {
        var result = _reviewer.Review(
            "City Stories Launch has frequency of 2.5, while Desert Heroes " +
            "Encore has frequency of 3.",
            [CampaignComparisonEvidence]);

        Assert.True(
            result.IsVerified,
            string.Join(", ", result.UnsupportedClaims));
    }

    [Fact]
    public void Review_AcceptsRepeatedMetricValueAcrossCampaigns()
    {
        var result = _reviewer.Review(
            "Both campaigns recorded 10 total impressions.",
            [CampaignComparisonEvidence]);

        Assert.True(
            result.IsVerified,
            string.Join(", ", result.UnsupportedClaims));
    }

    [Fact]
    public void Review_IgnoresIsoDatesButNotUnsupportedStandaloneYears()
    {
        var accepted = _reviewer.Review(
            "Window: 2026-01-01T00:00:00Z to 2026-02-01. " +
            "City Stories Launch had reach of 2.",
            [CampaignComparisonEvidence]);
        var rejected = _reviewer.Review(
            "City Stories Launch is projected through 2027 with reach of 2.",
            [CampaignComparisonEvidence]);

        Assert.True(
            accepted.IsVerified,
            string.Join(", ", accepted.UnsupportedClaims));
        Assert.False(rejected.IsVerified);
        Assert.Contains("2027", rejected.UnsupportedClaims);
    }

    [Fact]
    public void Review_RejectsRawPseudonymousIdentifierEvenWhenDigitsMatchEvidence()
    {
        var result = _reviewer.Review(
            "City Stories Launch reach came from device-002.",
            [CampaignComparisonEvidence]);

        Assert.False(result.IsVerified);
        Assert.Contains("device-002", result.UnsupportedClaims);
    }

    [Fact]
    public void Review_RejectsWrongMetricDespiteValueExistingForAnotherMetric()
    {
        var result = _reviewer.Review(
            "City Stories Launch has reach of 5.",
            [CampaignComparisonEvidence]);

        Assert.False(result.IsVerified);
        Assert.Contains("5", result.UnsupportedClaims);
    }

    private static string AnalyticsResult(double completionRate, int sessions) =>
        FormattableString.Invariant($$"""
        {
          "ViewingSessions": {{sessions}},
          "CompletionRate": {{completionRate}},
          "AdExposureRate": 80,
          "DataSource": "Synthetic viewing events"
        }
        """);

    private const string CrossTitleComparisonResult = """
        {
          "DataSource": "Synthetic viewing events",
          "Results": [
            {
              "Title": "City Stories",
              "ViewingSessions": 100,
              "CompletionRate": 60,
              "AdExposureRate": 80
            },
            {
              "Title": "Desert Heroes",
              "ViewingSessions": 100,
              "CompletionRate": 70,
              "AdExposureRate": 85
            }
          ]
        }
        """;

    private const string AudienceIdentityEvidence = """
        {
          "Results": [
            {
              "Title": "City Stories",
              "Reach": 2,
              "Frequency": 1.5,
              "EligibleImpressions": 3
            },
            {
              "Title": "Desert Heroes",
              "Reach": 1,
              "Frequency": 3,
              "EligibleImpressions": 3
            }
          ]
        }
        """;

    private const string CampaignComparisonEvidence = """
        {
          "Results": [
            {
              "CampaignName": "City Stories Launch",
              "Metrics": {
                "TotalImpressions": 10,
                "EligibleImpressions": 5,
                "Reach": 2,
                "Frequency": 2.5,
                "EligibleExposureCoverageRate": 50
              }
            },
            {
              "CampaignName": "Desert Heroes Encore",
              "Metrics": {
                "TotalImpressions": 10,
                "EligibleImpressions": 6,
                "Reach": 2,
                "Frequency": 3,
                "EligibleExposureCoverageRate": 60
              }
            }
          ]
        }
        """;
}
