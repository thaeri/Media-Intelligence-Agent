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
}
