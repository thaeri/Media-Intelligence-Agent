using System.Text.Json;
using MediaIntelligence.Core.Services;

namespace MediaIntelligence.Tests;

public sealed class EvidenceReviewDiagnosticsTests
{
    [Fact]
    public void Reviewer_WritesDiagnosticsOnlyForRejectedDrafts()
    {
        var sink = new CapturingDiagnostics();
        var reviewer = new EvidenceReviewerService(sink);
        const string evidence =
            "{\"Title\":\"City Stories\",\"CompletionRate\":60}";

        reviewer.Review("City Stories completion rate is 60%.", [evidence]);
        Assert.Empty(sink.Entries);

        reviewer.Review("City Stories completion rate is 70%.", [evidence]);
        var entry = Assert.Single(sink.Entries);
        Assert.Equal(["70%"], entry.UnsupportedClaims);
        var claim = Assert.Single(entry.Claims);
        Assert.False(claim.Supported);
        Assert.Equal("City Stories", claim.Campaign);
        Assert.Equal("completion_rate", claim.Metric);
    }

    [Fact]
    public void LocalDiagnostics_RedactsAndDeletesExpiredFiles()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"media-intelligence-diagnostics-{Guid.NewGuid():N}");
        var now = new DateTimeOffset(2026, 10, 11, 12, 0, 0, TimeSpan.Zero);

        try
        {
            Directory.CreateDirectory(directory);
            var expired = Path.Combine(
                directory,
                "evidence-rejection-expired.json");
            File.WriteAllText(expired, "expired");
            File.SetLastWriteTimeUtc(expired, now.AddHours(-2).UtcDateTime);
            var diagnostics = new LocalEvidenceReviewDiagnostics(
                directory,
                TimeSpan.FromHours(1),
                () => now);

            diagnostics.WriteRejectedDraft(
                "Device device-secret-1 token=top-secret " +
                "123e4567-e89b-12d3-a456-426614174000 claimed 70%.",
                ["device-secret-1", "70%"],
                [new EvidenceClaimDiagnostic(
                    "token=top-secret 70%",
                    "City Stories",
                    "coverage",
                    null,
                    null,
                    false)]);

            Assert.False(File.Exists(expired));
            var file = Assert.Single(Directory.GetFiles(directory));
            var json = File.ReadAllText(file);
            Assert.DoesNotContain("device-secret-1", json);
            Assert.DoesNotContain("top-secret", json);
            Assert.DoesNotContain(
                "123e4567-e89b-12d3-a456-426614174000",
                json);
            using var document = JsonDocument.Parse(json);
            Assert.Equal(
                now.AddHours(1),
                document.RootElement
                    .GetProperty("RetainUntilUtc")
                    .GetDateTimeOffset());
        }
        finally
        {
            if (Directory.Exists(directory) &&
                Path.GetFullPath(directory).StartsWith(
                    Path.GetFullPath(Path.GetTempPath()),
                    StringComparison.OrdinalIgnoreCase))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void DiagnosticsOptions_AreDisabledByDefault()
    {
        var options = new EvidenceReviewDiagnosticsOptions();

        Assert.False(options.Enabled);
        Assert.Null(options.Directory);
        Assert.InRange(options.RetentionMinutes, 1, 24 * 60);
    }

    private sealed class CapturingDiagnostics : IEvidenceReviewDiagnostics
    {
        public List<DiagnosticEntry> Entries { get; } = [];

        public void WriteRejectedDraft(
            string draft,
            IReadOnlyList<string> unsupportedClaims,
            IReadOnlyList<EvidenceClaimDiagnostic> claims) =>
            Entries.Add(new DiagnosticEntry(
                draft,
                unsupportedClaims,
                claims));
    }

    private sealed record DiagnosticEntry(
        string Draft,
        IReadOnlyList<string> UnsupportedClaims,
        IReadOnlyList<EvidenceClaimDiagnostic> Claims);
}
