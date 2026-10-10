using MediaIntelligence.Core.Services;

namespace MediaIntelligence.Tests;

public sealed class AgentOfflineEvaluationTests
{
    private readonly EvidenceReviewerService _reviewer = new();

    [Fact]
    public void SingleTitleAnalytics_IsGrounded()
    {
        var result = _reviewer.Review(
            "City Stories has a 60% completion rate across 100 sessions.",
            [SingleTitleEvidence]);

        Assert.True(result.IsVerified);
    }

    [Fact]
    public void CrossTitleComparison_IsGrounded()
    {
        var result = _reviewer.Review(
            "Desert Heroes leads at 70%, while City Stories is at 60%.",
            [ComparisonEvidence]);

        Assert.True(result.IsVerified);
    }

    [Fact]
    public void UnsupportedNumericalClaim_IsRejected()
    {
        var result = _reviewer.Review(
            "City Stories has a 95% completion rate.",
            [SingleTitleEvidence]);

        Assert.False(result.IsVerified);
    }

    [Fact]
    public void WrongTitleAttribution_IsRejected()
    {
        var result = _reviewer.Review(
            "City Stories has a 70% completion rate.",
            [ComparisonEvidence]);

        Assert.False(result.IsVerified);
    }

    [Fact]
    public void MalformedToolEvidence_IsRejected()
    {
        var result = _reviewer.Review(
            "City Stories has a 60% completion rate.",
            ["{ malformed evidence"]);

        Assert.False(result.IsVerified);
    }

    private const string SingleTitleEvidence = """
        {
          "Title": "City Stories",
          "ViewingSessions": 100,
          "CompletionRate": 60,
          "AdExposureRate": 80
        }
        """;

    private const string ComparisonEvidence = """
        {
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
