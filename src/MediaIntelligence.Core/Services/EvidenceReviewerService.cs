using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MediaIntelligence.Core.Services;

public sealed record EvidenceReviewResult(
    bool IsVerified,
    IReadOnlyList<string> UnsupportedClaims);

public sealed partial class EvidenceReviewerService
{
    public EvidenceReviewResult Review(
        string response,
        IEnumerable<string> analyticsToolResults)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(analyticsToolResults);

        var evidence = analyticsToolResults
            .SelectMany(ReadNumericEvidence)
            .ToArray();

        var titles = evidence
            .Select(value => value.Title)
            .Where(title => title is not null)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Cast<string>()
            .ToArray();

        var unsupportedClaims = NumericClaimRegex()
            .Matches(response)
            .Select(match => new NumericClaim(
                match.Value.Trim(),
                double.Parse(
                    match.Groups["value"].Value,
                    CultureInfo.InvariantCulture),
                CountDecimalPlaces(match.Groups["value"].Value),
                FindAttributedTitle(response, match.Index, titles),
                FindAttributedMetric(response, match.Index)))
            .Where(claim => !evidence.Any(value => Matches(claim, value)))
            .Select(claim => claim.Text)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return new EvidenceReviewResult(
            unsupportedClaims.Length == 0,
            unsupportedClaims);
    }

    public string CreateVerifiedResponse(
        string response,
        IEnumerable<string> analyticsToolResults)
    {
        var review = Review(response, analyticsToolResults);

        return CreateVerifiedResponse(response, review);
    }

    public string CreateVerifiedResponse(
        string response,
        EvidenceReviewResult review)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(review);

        if (review.IsVerified)
        {
            return response;
        }

        return $"Evidence verification failed: the draft contained unsupported " +
            $"numerical claims ({string.Join(", ", review.UnsupportedClaims)}). " +
            "No unverified numerical answer was returned.";
    }

    private static IEnumerable<EvidenceValue> ReadNumericEvidence(string json)
    {
        JsonDocument document;

        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            yield break;
        }

        using (document)
        {
            foreach (var value in ReadNumbers(
                document.RootElement,
                null,
                null))
            {
                yield return value;
            }
        }
    }

    private static IEnumerable<EvidenceValue> ReadNumbers(
        JsonElement element,
        string? inheritedTitle,
        string? propertyName)
    {
        var title = inheritedTitle;

        if (element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty("Title", out var titleElement) &&
            titleElement.ValueKind == JsonValueKind.String)
        {
            title = titleElement.GetString();
        }

        if (element.ValueKind == JsonValueKind.Number &&
            element.TryGetDouble(out var number))
        {
            yield return new EvidenceValue(
                number,
                title,
                NormalizeMetric(propertyName));
            yield break;
        }

        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                foreach (var value in ReadNumbers(
                    property.Value,
                    title,
                    property.Name))
                {
                    yield return value;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                foreach (var value in ReadNumbers(item, title, propertyName))
                {
                    yield return value;
                }
            }
        }
    }

    private static bool Matches(NumericClaim claim, EvidenceValue evidence)
    {
        if (claim.Title is not null &&
            !string.Equals(
                claim.Title,
                evidence.Title,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (claim.Metric is not null &&
            !string.Equals(
                claim.Metric,
                evidence.Metric,
                StringComparison.Ordinal))
        {
            return false;
        }

        var tolerance = claim.DecimalPlaces == 0
            ? 0.5
            : 0.5 * Math.Pow(10, -claim.DecimalPlaces);

        return Math.Abs(claim.Value - evidence.Value) <= tolerance;
    }

    private static string? FindAttributedTitle(
        string response,
        int claimIndex,
        IReadOnlyList<string> titles)
    {
        var sentenceStart = response.LastIndexOfAny(
            ['.', '!', '?', '\n'],
            Math.Max(0, claimIndex - 1));
        sentenceStart = sentenceStart < 0 ? 0 : sentenceStart + 1;
        var sentenceEnd = response.IndexOfAny(
            ['.', '!', '?', '\n'],
            claimIndex);
        sentenceEnd = sentenceEnd < 0 ? response.Length : sentenceEnd;

        string? closestTitle = null;
        var closestTitleIndex = -1;

        foreach (var title in titles)
        {
            var titleIndex = response.LastIndexOf(
                title,
                claimIndex,
                claimIndex - sentenceStart + 1,
                StringComparison.OrdinalIgnoreCase);

            if (titleIndex >= sentenceStart && titleIndex > closestTitleIndex)
            {
                closestTitle = title;
                closestTitleIndex = titleIndex;
            }
        }

        if (closestTitle is not null)
        {
            return closestTitle;
        }

        var closestFollowingIndex = int.MaxValue;
        foreach (var title in titles)
        {
            var titleIndex = response.IndexOf(
                title,
                claimIndex,
                StringComparison.OrdinalIgnoreCase);

            if (titleIndex >= 0 &&
                titleIndex < sentenceEnd &&
                titleIndex < closestFollowingIndex)
            {
                closestTitle = title;
                closestFollowingIndex = titleIndex;
            }
        }

        return closestTitle;
    }

    private static string? FindAttributedMetric(
        string response,
        int claimIndex)
    {
        var sentenceStart = response.LastIndexOfAny(
            ['.', '!', '?', '\n'],
            Math.Max(0, claimIndex - 1));
        sentenceStart = sentenceStart < 0 ? 0 : sentenceStart + 1;
        var sentenceEnd = response.IndexOfAny(
            ['.', '!', '?', '\n'],
            claimIndex);
        sentenceEnd = sentenceEnd < 0 ? response.Length : sentenceEnd;
        var sentence = response[sentenceStart..sentenceEnd];
        var relativeClaimIndex = claimIndex - sentenceStart;
        string? closestMetric = null;
        var closestDistance = int.MaxValue;

        foreach (var (phrase, metric) in MetricPhrases)
        {
            var searchIndex = 0;
            while (searchIndex < sentence.Length)
            {
                var phraseIndex = sentence.IndexOf(
                    phrase,
                    searchIndex,
                    StringComparison.OrdinalIgnoreCase);
                if (phraseIndex < 0)
                {
                    break;
                }

                var distance = Math.Abs(relativeClaimIndex - phraseIndex);
                if (distance < closestDistance)
                {
                    closestMetric = metric;
                    closestDistance = distance;
                }

                searchIndex = phraseIndex + phrase.Length;
            }
        }

        return closestMetric;
    }

    private static string? NormalizeMetric(string? propertyName)
    {
        if (propertyName is null)
        {
            return null;
        }

        return propertyName.ToLowerInvariant() switch
        {
            "completionrate" => "completion_rate",
            "adexposurerate" => "ad_exposure_rate",
            "viewingsessions" => "viewing_sessions",
            "exposureevents" => "exposure_events",
            "eligibleimpressions" => "eligible_impressions",
            "reach" => "reach",
            "frequency" => "frequency",
            "eligibleexposurecoveragerate" => "coverage",
            "missingdeviceidexclusions" => "missing_device",
            "unknowndeviceexclusions" => "unknown_device",
            "conflictexclusions" => "conflict",
            "consentdeniedexclusions" => "consent_denied",
            "consentmissingexclusions" => "consent_missing",
            _ => null
        };
    }

    private static int CountDecimalPlaces(string number)
    {
        var decimalPoint = number.IndexOf('.');
        return decimalPoint < 0 ? 0 : number.Length - decimalPoint - 1;
    }

    private sealed record NumericClaim(
        string Text,
        double Value,
        int DecimalPlaces,
        string? Title,
        string? Metric);

    private sealed record EvidenceValue(
        double Value,
        string? Title,
        string? Metric);

    private static readonly (string Phrase, string Metric)[] MetricPhrases =
    [
        ("eligible exposure coverage", "coverage"),
        ("coverage rate", "coverage"),
        ("completion rate", "completion_rate"),
        ("ad exposure rate", "ad_exposure_rate"),
        ("viewing sessions", "viewing_sessions"),
        ("sessions", "viewing_sessions"),
        ("exposure events", "exposure_events"),
        ("eligible impressions", "eligible_impressions"),
        ("impressions", "eligible_impressions"),
        ("missing device", "missing_device"),
        ("unknown device", "unknown_device"),
        ("conflict", "conflict"),
        ("consent denied", "consent_denied"),
        ("missing consent", "consent_missing"),
        ("reach", "reach"),
        ("frequency", "frequency"),
        ("coverage", "coverage")
    ];

    [GeneratedRegex(
        @"(?<![\p{L}\p{N}_])(?<value>\d+(?:\.\d+)?)(?:\s*(?:%|percent(?:age)?(?:\s+points?)?|viewing\s+sessions?|sessions?))?",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NumericClaimRegex();
}
