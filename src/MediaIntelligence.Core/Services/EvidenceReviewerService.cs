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
                FindAttributedTitle(response, match.Index, titles)))
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
            foreach (var value in ReadNumbers(document.RootElement, null))
            {
                yield return value;
            }
        }
    }

    private static IEnumerable<EvidenceValue> ReadNumbers(
        JsonElement element,
        string? inheritedTitle)
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
            yield return new EvidenceValue(number, title);
            yield break;
        }

        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                foreach (var value in ReadNumbers(property.Value, title))
                {
                    yield return value;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                foreach (var value in ReadNumbers(item, title))
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

    private static int CountDecimalPlaces(string number)
    {
        var decimalPoint = number.IndexOf('.');
        return decimalPoint < 0 ? 0 : number.Length - decimalPoint - 1;
    }

    private sealed record NumericClaim(
        string Text,
        double Value,
        int DecimalPlaces,
        string? Title);

    private sealed record EvidenceValue(double Value, string? Title);

    [GeneratedRegex(
        @"(?<![\p{L}\p{N}_])(?<value>\d+(?:\.\d+)?)(?:\s*(?:%|percent(?:age)?(?:\s+points?)?|viewing\s+sessions?|sessions?))?",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NumericClaimRegex();
}
