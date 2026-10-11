using System.Text.Json;
using System.Text.RegularExpressions;

namespace MediaIntelligence.Core.Services;

public sealed record EvidenceClaimDiagnostic(
    string Text,
    string? Campaign,
    string? Metric,
    string? ComparedWithCampaign,
    string? Direction,
    bool Supported);

public interface IEvidenceReviewDiagnostics
{
    void WriteRejectedDraft(
        string draft,
        IReadOnlyList<string> unsupportedClaims,
        IReadOnlyList<EvidenceClaimDiagnostic> claims);
}

public sealed class NullEvidenceReviewDiagnostics : IEvidenceReviewDiagnostics
{
    public static NullEvidenceReviewDiagnostics Instance { get; } = new();

    private NullEvidenceReviewDiagnostics()
    {
    }

    public void WriteRejectedDraft(
        string draft,
        IReadOnlyList<string> unsupportedClaims,
        IReadOnlyList<EvidenceClaimDiagnostic> claims)
    {
    }
}

public sealed partial class LocalEvidenceReviewDiagnostics : IEvidenceReviewDiagnostics
{
    private const string FilePrefix = "evidence-rejection-";
    private readonly string _directory;
    private readonly TimeSpan _retention;
    private readonly Func<DateTimeOffset> _utcNow;

    public LocalEvidenceReviewDiagnostics(
        string directory,
        TimeSpan retention,
        Func<DateTimeOffset>? utcNow = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        if (!Path.IsPathFullyQualified(directory))
        {
            throw new ArgumentException(
                "The diagnostics directory must be an absolute path.",
                nameof(directory));
        }

        if (retention <= TimeSpan.Zero || retention > TimeSpan.FromHours(24))
        {
            throw new ArgumentOutOfRangeException(
                nameof(retention),
                "Diagnostic retention must be greater than zero and no more than 24 hours.");
        }

        _directory = Path.GetFullPath(directory);
        _retention = retention;
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
    }

    public void WriteRejectedDraft(
        string draft,
        IReadOnlyList<string> unsupportedClaims,
        IReadOnlyList<EvidenceClaimDiagnostic> claims)
    {
        try
        {
            EnsureRestrictedDirectory();
            DeleteExpiredFiles();

            var now = _utcNow();
            var path = Path.Combine(
                _directory,
                $"{FilePrefix}{now:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}.json");
            var payload = JsonSerializer.Serialize(new
            {
                CapturedAtUtc = now,
                RetainUntilUtc = now.Add(_retention),
                Draft = Redact(draft),
                UnsupportedClaims = unsupportedClaims.Select(Redact).ToArray(),
                Claims = claims.Select(claim => new
                {
                    Text = Redact(claim.Text),
                    Campaign = RedactNullable(claim.Campaign),
                    Metric = RedactNullable(claim.Metric),
                    ComparedWithCampaign = RedactNullable(
                        claim.ComparedWithCampaign),
                    Direction = RedactNullable(claim.Direction),
                    claim.Supported
                }).ToArray()
            });

            var options = new FileStreamOptions
            {
                Access = FileAccess.Write,
                Mode = FileMode.CreateNew,
                Share = FileShare.None,
                Options = FileOptions.WriteThrough
            };
            if (!OperatingSystem.IsWindows())
            {
                options.UnixCreateMode = UnixFileMode.UserRead |
                    UnixFileMode.UserWrite;
            }

            using var stream = new FileStream(path, options);
            using var writer = new StreamWriter(stream);
            writer.Write(payload);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            // Diagnostics are best-effort and must never affect agent behavior.
        }
    }

    private void EnsureRestrictedDirectory()
    {
        if (OperatingSystem.IsWindows())
        {
            // LocalApplicationData inherits the current user's protected profile ACL.
            Directory.CreateDirectory(_directory);
            return;
        }

        Directory.CreateDirectory(
            _directory,
            UnixFileMode.UserRead |
            UnixFileMode.UserWrite |
            UnixFileMode.UserExecute);
        File.SetUnixFileMode(
            _directory,
            UnixFileMode.UserRead |
            UnixFileMode.UserWrite |
            UnixFileMode.UserExecute);
    }

    private void DeleteExpiredFiles()
    {
        var cutoff = _utcNow().Subtract(_retention);
        foreach (var path in Directory.EnumerateFiles(
                     _directory,
                     $"{FilePrefix}*.json"))
        {
            if (File.GetLastWriteTimeUtc(path) < cutoff.UtcDateTime)
            {
                File.Delete(path);
            }
        }
    }

    private static string Redact(string value) =>
        CredentialRegex().Replace(
            GuidRegex().Replace(
                IdentityRegex().Replace(value, "[REDACTED_IDENTITY]"),
                "[REDACTED_GUID]"),
            "$1[REDACTED_SECRET]");

    private static string? RedactNullable(string? value) =>
        value is null ? null : Redact(value);

    [GeneratedRegex(
        @"\b(?:device|audience)[-_][\p{L}\p{N}_-]+\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex IdentityRegex();

    [GeneratedRegex(
        @"\b[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex GuidRegex();

    [GeneratedRegex(
        @"(?i)\b(api[_-]?key|authorization|bearer|client[_-]?secret|password|token)\b\s*[:=]?\s*[^\s,;]+",
        RegexOptions.CultureInvariant)]
    private static partial Regex CredentialRegex();
}

public sealed class EvidenceReviewDiagnosticsOptions
{
    public const string SectionName = "EvidenceReviewDiagnostics";

    public bool Enabled { get; set; }
    public string? Directory { get; set; }
    public int RetentionMinutes { get; set; } = 60;
}
