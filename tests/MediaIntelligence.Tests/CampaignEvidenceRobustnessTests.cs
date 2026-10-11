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

    [Theory]
    [InlineData("City Stories Launch has 1 more eligible impression than Desert Heroes Encore.")]
    [InlineData("Desert Heroes Encore has 2 more eligible impressions than City Stories Launch.")]
    [InlineData("Desert Heroes Encore has 10 percentage points lower coverage than City Stories Launch.")]
    [InlineData("Desert Heroes Encore has 4.0 frequency.")]
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

    [Theory]
    [InlineData("Device device-city-a had an impression.")]
    [InlineData("Audience audience-city-1 was reached.")]
    public void Review_StillRejectsRawIdentityIdentifiers(string response)
    {
        var result = reviewer.Review(response, [CityEvidence, DesertEvidence]);

        Assert.False(result.IsVerified);
    }
}
