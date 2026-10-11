using MediaIntelligence.Core.Services;

namespace MediaIntelligence.Tests;

public sealed class CampaignEvidenceRobustnessTests
{
    private const string CityEvidence = """
        {"CampaignName":"City Stories Launch","Metrics":{"TotalImpressions":10,"EligibleImpressions":5,"Reach":2,"Frequency":2.5,"EligibleExposureCoverageRate":50}}
        """;

    private const string DesertEvidence = """
        {"CampaignName":"Desert Heroes Encore","Metrics":{"TotalImpressions":10,"EligibleImpressions":6,"Reach":2,"Frequency":3.0,"EligibleExposureCoverageRate":60}}
        """;

    private readonly EvidenceReviewerService reviewer = new();

    [Fact]
    public void Review_AcceptsMultipleIndividualToolResultsAndMarkdownFormats()
    {
        const string response = """
            | Campaign | Eligible impressions | Reach | Frequency | Eligible coverage |
            |---|---:|---:|---:|---:|
            | City Stories Launch | 5 | 2 | 2.5 | 50% |
            | Desert Heroes Encore | 6 | 2 | 3.0 | 60% |

            Findings:
            1. Desert Heroes Encore has 1 more eligible impression than City Stories Launch.
            2. Desert Heroes Encore has the same reach as City Stories Launch, a difference of 0.
            3. Desert Heroes Encore has 0.5 higher frequency than City Stories Launch.
            4. Desert Heroes Encore has 10 percentage points higher coverage than City Stories Launch.
            """;

        var result = reviewer.Review(response, [CityEvidence, DesertEvidence]);

        Assert.True(result.IsVerified, string.Join(", ", result.UnsupportedClaims));
    }

    [Fact]
    public void Review_AcceptsReversedDirectionWhenCampaignAttributionIsCorrect()
    {
        const string response =
            "City Stories Launch has 1 fewer eligible impression, 0.5 lower frequency, and 10 percentage points lower coverage than Desert Heroes Encore.";

        var result = reviewer.Review(response, [CityEvidence, DesertEvidence]);

        Assert.True(result.IsVerified, string.Join(", ", result.UnsupportedClaims));
    }

    [Fact]
    public void Review_UsesClaimLocalDirectionWhenOneSentenceContainsEqualReach()
    {
        const string response =
            "Compared with City Stories Launch, Desert Heroes Encore has " +
            "1 more eligible impression, the same reach with a difference " +
            "of 0, 0.5 higher average frequency, and 10 percentage points " +
            "higher eligible coverage.";

        var result = reviewer.Review(response, [CityEvidence, DesertEvidence]);

        Assert.True(result.IsVerified, string.Join(", ", result.UnsupportedClaims));
    }

    [Fact]
    public void Review_AcceptsAttributedSignedDifferencesFromIndividualTools()
    {
        const string response = """
            ## Desert Heroes Encore vs. City Stories Launch
            - Eligible impression difference: +1
            - Reach difference: 0
            - Average frequency difference: +0.5
            - Eligible coverage difference: +10 percentage points
            """;

        var result = reviewer.Review(response, [CityEvidence, DesertEvidence]);

        Assert.True(result.IsVerified, string.Join(", ", result.UnsupportedClaims));
    }

    [Theory]
    [InlineData("City Stories Launch has 1 more eligible impression than Desert Heroes Encore.")]
    [InlineData("Desert Heroes Encore has 2 more eligible impressions than City Stories Launch.")]
    [InlineData("Desert Heroes Encore has 10 percentage points lower coverage than City Stories Launch.")]
    [InlineData("Desert Heroes Encore has 4.0 frequency.")]
    [InlineData("Compared with City Stories Launch, Desert Heroes Encore has +2 eligible impressions.")]
    [InlineData("Compared with City Stories Launch, Desert Heroes Encore has -0.5 frequency.")]
    [InlineData("Compared with City Stories Launch, Desert Heroes Encore has +10 frequency.")]
    public void Review_RejectsWrongAttributionDirectionOrUnsupportedNumbers(string response)
    {
        var result = reviewer.Review(response, [CityEvidence, DesertEvidence]);

        Assert.False(result.IsVerified);
        Assert.NotEmpty(result.UnsupportedClaims);
    }

    [Fact]
    public void Review_IgnoresOnlyOrderedListMarkers()
    {
        const string valid = """
            1. City Stories Launch frequency is 2.5.
            2) Desert Heroes Encore frequency is 3.0.
            """;
        const string invalid = "City Stories Launch frequency is 2.5. Standalone result: 1.";

        Assert.True(reviewer.Review(valid, [CityEvidence, DesertEvidence]).IsVerified);
        Assert.False(reviewer.Review(invalid, [CityEvidence, DesertEvidence]).IsVerified);
    }

    [Fact]
    public void Review_DoesNotWhitelistObservedSmokeTestNumbers()
    {
        var diagnostics = new ClaimCaptureDiagnostics();
        var diagnosticReviewer = new EvidenceReviewerService(diagnostics);
        const string response =
            "The unexplained values are 1, 0.5, and 10 percentage points.";

        var result = diagnosticReviewer.Review(
            response,
            [CityEvidence, DesertEvidence]);

        Assert.False(result.IsVerified);
        Assert.Contains("1", result.UnsupportedClaims);
        Assert.Contains("0.5", result.UnsupportedClaims);
        Assert.True(
            result.UnsupportedClaims.Contains("10 percentage points"),
            System.Text.Json.JsonSerializer.Serialize(diagnostics.Claims));
    }

    [Fact]
    public void Review_RecomputesDifferencesInsteadOfTrustingToolComparisonFields()
    {
        const string tamperedComparisonEvidence = """
            {
              "Results": [
                {"CampaignName":"City Stories Launch","Metrics":{"EligibleImpressions":5,"Reach":2,"Frequency":2.5,"EligibleExposureCoverageRate":50}},
                {"CampaignName":"Desert Heroes Encore","Metrics":{"EligibleImpressions":6,"Reach":2,"Frequency":3.0,"EligibleExposureCoverageRate":60}}
              ],
              "Comparisons": [
                {"CampaignName":"Desert Heroes Encore","ComparedWithCampaignName":"City Stories Launch","EligibleImpressionsDifference":999,"EligibleImpressionsDirection":"Higher"}
              ]
            }
            """;
        const string response =
            "Desert Heroes Encore has 999 more eligible impressions than " +
            "City Stories Launch.";

        var result = reviewer.Review(response, [tamperedComparisonEvidence]);

        Assert.False(result.IsVerified);
        Assert.Contains("999", result.UnsupportedClaims);
    }

    [Theory]
    [InlineData("Device device-city-a had an impression.")]
    [InlineData("Audience audience-city-1 was reached.")]
    public void Review_StillRejectsRawIdentityIdentifiers(string response)
    {
        var result = reviewer.Review(response, [CityEvidence, DesertEvidence]);

        Assert.False(result.IsVerified);
    }

    private sealed class ClaimCaptureDiagnostics : IEvidenceReviewDiagnostics
    {
        public IReadOnlyList<EvidenceClaimDiagnostic> Claims { get; private set; } = [];

        public void WriteRejectedDraft(
            string draft,
            IReadOnlyList<string> unsupportedClaims,
            IReadOnlyList<EvidenceClaimDiagnostic> claims) =>
            Claims = claims;
    }
}
