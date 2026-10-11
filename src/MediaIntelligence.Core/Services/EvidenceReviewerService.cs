using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using MediaIntelligence.Core.Models;

namespace MediaIntelligence.Core.Services;

public sealed record EvidenceReviewResult(
    bool IsVerified,
    IReadOnlyList<string> UnsupportedClaims);

public sealed partial class EvidenceReviewerService
{
    private readonly IEvidenceReviewDiagnostics _diagnostics;

    public EvidenceReviewerService(
        IEvidenceReviewDiagnostics? diagnostics = null)
    {
        _diagnostics = diagnostics ?? NullEvidenceReviewDiagnostics.Instance;
    }

    public EvidenceReviewResult Review(
        string response,
        IEnumerable<string> analyticsToolResults)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(analyticsToolResults);

        var toolResults = analyticsToolResults.ToArray();
        var snapshots = toolResults
            .SelectMany(ReadCampaignSnapshots)
            .ToArray();
        var comparisons = CampaignComparisonCalculator.Calculate(snapshots);
        var evidence = toolResults
            .SelectMany(ReadNumericEvidence)
            .Concat(comparisons.SelectMany(ReadComparisonEvidence))
            .ToArray();

        var titles = evidence
            .Select(value => value.Title)
            .Where(title => title is not null)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Cast<string>()
            .ToArray();

        var numericClaims = NumericClaimRegex()
            .Matches(response)
            .Where(match => !IsWithinIsoDate(
                response,
                match.Groups["value"].Index))
            .Where(match => !IsListOrdinal(response, match))
            .Select(match => CreateNumericClaim(response, match, titles))
            .Select(claim => new ReviewedNumericClaim(
                claim,
                IsClaimSupported(claim, evidence)))
            .ToArray();
        var unsupportedNumericClaims = numericClaims
            .Where(claim => !claim.IsSupported)
            .Select(claim => claim.Claim)
            .Select(claim => claim.Text)
            .ToArray();

        var unsupportedClaims = SensitiveIdentifierRegex()
            .Matches(response)
            .Select(match => match.Value)
            .Concat(unsupportedNumericClaims)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var result = new EvidenceReviewResult(
            unsupportedClaims.Length == 0,
            unsupportedClaims);
        if (!result.IsVerified)
        {
            _diagnostics.WriteRejectedDraft(
                response,
                unsupportedClaims,
                numericClaims.Select(claim => new EvidenceClaimDiagnostic(
                    claim.Claim.Text,
                    claim.Claim.Title,
                    claim.Claim.Metric,
                    claim.Claim.ComparedWithTitle,
                    claim.Claim.Direction,
                    claim.IsSupported)).ToArray());
        }

        return result;
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

    private static IEnumerable<CampaignMetricSnapshot> ReadCampaignSnapshots(
        string json)
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
            foreach (var snapshot in ReadCampaignSnapshots(document.RootElement))
            {
                yield return snapshot;
            }
        }
    }

    private static IEnumerable<CampaignMetricSnapshot> ReadCampaignSnapshots(
        JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty("CampaignName", out var campaignName) &&
            campaignName.ValueKind == JsonValueKind.String &&
            element.TryGetProperty("Metrics", out var metrics) &&
            metrics.ValueKind == JsonValueKind.Object &&
            TryReadNumber(metrics, "EligibleImpressions", out var eligible) &&
            TryReadNumber(metrics, "Reach", out var reach) &&
            TryReadNumber(metrics, "Frequency", out var frequency) &&
            TryReadNumber(
                metrics,
                "EligibleExposureCoverageRate",
                out var coverage))
        {
            yield return new CampaignMetricSnapshot(
                campaignName.GetString()!,
                (int)eligible,
                (int)reach,
                frequency,
                coverage);
        }

        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                foreach (var snapshot in ReadCampaignSnapshots(property.Value))
                {
                    yield return snapshot;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                foreach (var snapshot in ReadCampaignSnapshots(item))
                {
                    yield return snapshot;
                }
            }
        }
    }

    private static bool TryReadNumber(
        JsonElement element,
        string propertyName,
        out double value)
    {
        value = default;
        return element.TryGetProperty(propertyName, out var property) &&
            property.ValueKind == JsonValueKind.Number &&
            property.TryGetDouble(out value);
    }

    private static IEnumerable<EvidenceValue> ReadComparisonEvidence(
        CampaignMetricComparison comparison)
    {
        yield return ComparisonValue(
            comparison.EligibleImpressionsDifference,
            comparison,
            "eligible_impressions_difference",
            comparison.EligibleImpressionsDirection);
        yield return ComparisonValue(
            comparison.ReachDifference,
            comparison,
            "reach_difference",
            comparison.ReachDirection);
        yield return ComparisonValue(
            comparison.FrequencyDifference,
            comparison,
            "frequency_difference",
            comparison.FrequencyDirection);
        yield return ComparisonValue(
            comparison.CoveragePercentagePointDifference,
            comparison,
            "coverage_difference",
            comparison.CoverageDirection);
    }

    private static EvidenceValue ComparisonValue(
        double value,
        CampaignMetricComparison comparison,
        string metric,
        string direction) =>
        new(
            value,
            comparison.CampaignName,
            metric,
            comparison.ComparedWithCampaignName,
            direction);

    private static IEnumerable<EvidenceValue> ReadNumbers(
        JsonElement element,
        string? inheritedTitle,
        string? propertyName)
    {
        var title = inheritedTitle;

        if (element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty("ComparedWithCampaignName", out _))
        {
            yield break;
        }

        if (element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty("Title", out var titleElement) &&
            titleElement.ValueKind == JsonValueKind.String)
        {
            title = titleElement.GetString();
        }

        if (element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty("CampaignName", out var campaignElement) &&
            campaignElement.ValueKind == JsonValueKind.String)
        {
            title = campaignElement.GetString();
        }

        if (element.ValueKind == JsonValueKind.Number &&
            element.TryGetDouble(out var number))
        {
            yield return new EvidenceValue(
                number,
                title,
                NormalizeMetric(propertyName),
                null,
                null);
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
        if (claim.Metric is null)
        {
            return false;
        }

        if (evidence.ComparedWithTitle is not null &&
            (claim.Title is null ||
             claim.Metric is null ||
             claim.ComparedWithTitle is null ||
             claim.Direction is null))
        {
            return false;
        }

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

        if (claim.ComparedWithTitle is not null &&
            !string.Equals(
                claim.ComparedWithTitle,
                evidence.ComparedWithTitle,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (claim.Direction is not null &&
            !string.Equals(
                claim.Direction,
                evidence.Direction,
                StringComparison.Ordinal))
        {
            return false;
        }

        var tolerance = claim.DecimalPlaces == 0
            ? 0.5
            : 0.5 * Math.Pow(10, -claim.DecimalPlaces);

        return Math.Abs(claim.Value - evidence.Value) <= tolerance;
    }

    private static bool IsClaimSupported(
        NumericClaim claim,
        IReadOnlyList<EvidenceValue> evidence)
    {
        if (claim.Metric is not null)
        {
            return evidence.Any(value => Matches(claim, value));
        }

        if (claim.Title is null || claim.ComparedWithTitle is not null)
        {
            return false;
        }

        var matchingMetrics = evidence
            .Where(value => value.ComparedWithTitle is null)
            .Where(value => string.Equals(
                value.Title,
                claim.Title,
                StringComparison.OrdinalIgnoreCase))
            .Where(value => NumericValuesMatch(claim, value))
            .Select(value => value.Metric)
            .Where(metric => metric is not null)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return matchingMetrics.Length == 1;
    }

    private static bool NumericValuesMatch(
        NumericClaim claim,
        EvidenceValue evidence)
    {
        var tolerance = claim.DecimalPlaces == 0
            ? 0.5
            : 0.5 * Math.Pow(10, -claim.DecimalPlaces);
        return Math.Abs(claim.Value - evidence.Value) <= tolerance;
    }

    private static NumericClaim CreateNumericClaim(
        string response,
        Match match,
        IReadOnlyList<string> titles)
    {
        var attribution = FindCampaignAttribution(response, match.Index, titles);
        var direction = attribution.ComparedWithTitle is null
            ? null
            : FindDifferenceDirection(response, match);
        var metric = FindMarkdownTableMetric(response, match.Index) ??
            FindAttributedMetric(response, match.Index);
        var comparedWithTitle = direction is null
            ? null
            : attribution.ComparedWithTitle;
        if (comparedWithTitle is not null)
        {
            metric = FindDifferenceMetric(response, match) ?? ToDifferenceMetric(metric);
        }

        var value = double.Parse(
            match.Groups["value"].Value,
            CultureInfo.InvariantCulture);
        if (comparedWithTitle is null &&
            match.Groups["sign"].Value is "-" or "−")
        {
            value = -value;
        }

        return new NumericClaim(
            match.Value.Trim(),
            value,
            CountDecimalPlaces(match.Groups["value"].Value),
            attribution.Title,
            metric,
            comparedWithTitle,
            direction);
    }

    private static string? FindAttributedTitle(
        string response,
        int claimIndex,
        IReadOnlyList<string> titles)
    {
        string? closestTitle = null;
        var closestTitleIndex = -1;

        foreach (var title in titles)
        {
            var titleIndex = response.LastIndexOf(
                title,
                claimIndex,
                claimIndex + 1,
                StringComparison.OrdinalIgnoreCase);

            if (titleIndex > closestTitleIndex)
            {
                closestTitle = title;
                closestTitleIndex = titleIndex;
            }
        }

        if (closestTitle is not null)
        {
            return closestTitle;
        }

        var clauseEnd = FindClauseEnd(response, claimIndex);
        var closestFollowingIndex = int.MaxValue;
        foreach (var title in titles)
        {
            var titleIndex = response.IndexOf(
                title,
                claimIndex,
                StringComparison.OrdinalIgnoreCase);

            if (titleIndex >= 0 &&
                titleIndex < clauseEnd &&
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
        var clauseStart = FindClauseStart(response, claimIndex);
        var clauseEnd = FindClauseEnd(response, claimIndex);
        var clause = response[clauseStart..clauseEnd];
        var relativeClaimIndex = claimIndex - clauseStart;
        string? precedingMetric = null;
        var precedingDistance = int.MaxValue;
        string? followingMetric = null;
        var followingDistance = int.MaxValue;

        foreach (var (phrase, metric) in MetricPhrases)
        {
            var searchIndex = 0;
            while (searchIndex < clause.Length)
            {
                var phraseIndex = clause.IndexOf(
                    phrase,
                    searchIndex,
                    StringComparison.OrdinalIgnoreCase);
                if (phraseIndex < 0)
                {
                    break;
                }

                var phraseEnd = phraseIndex + phrase.Length;
                if (phraseEnd <= relativeClaimIndex)
                {
                    var distance = relativeClaimIndex - phraseEnd;
                    if (distance < precedingDistance)
                    {
                        precedingMetric = metric;
                        precedingDistance = distance;
                    }
                }
                else if (phraseIndex >= relativeClaimIndex)
                {
                    var distance = phraseIndex - relativeClaimIndex;
                    if (distance < followingDistance)
                    {
                        followingMetric = metric;
                        followingDistance = distance;
                    }
                }

                searchIndex = phraseIndex + phrase.Length;
            }
        }

        if (precedingMetric is null)
        {
            return followingMetric;
        }

        if (followingMetric is null)
        {
            return precedingMetric;
        }

        return precedingDistance <= followingDistance
            ? precedingMetric
            : followingMetric;
    }

    private static int FindClauseStart(string response, int claimIndex)
    {
        var boundary = response.LastIndexOfAny(
            [',', ';', '\n', '|'],
            Math.Max(0, claimIndex - 1));
        return boundary < 0 ? 0 : boundary + 1;
    }

    private static int FindClauseEnd(string response, int claimIndex)
    {
        var boundary = response.IndexOfAny(
            [',', ';', '\n', '|'],
            claimIndex);
        return boundary < 0 ? response.Length : boundary;
    }

    private static bool IsWithinIsoDate(string response, int claimIndex) =>
        IsoDateRegex().Matches(response).Any(match =>
            claimIndex >= match.Index &&
            claimIndex < match.Index + match.Length);

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
            "totalimpressions" => "total_impressions",
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

    private static CampaignAttribution FindCampaignAttribution(
        string response,
        int claimIndex,
        IReadOnlyCollection<string> titles)
    {
        var lineStart = response.LastIndexOf('\n', Math.Max(0, claimIndex - 1)) + 1;
        var lineEnd = response.IndexOf('\n', claimIndex);
        if (lineEnd < 0)
        {
            lineEnd = response.Length;
        }

        var occurrences = FindTitleOccurrences(
            response,
            titles,
            lineStart,
            lineEnd);

        if (occurrences.Length == 0)
        {
            var heading = FindComparisonHeading(
                response,
                titles,
                lineStart);
            if (heading is not null)
            {
                return heading;
            }

            return new CampaignAttribution(
                FindAttributedTitle(response, claimIndex, titles.ToArray()),
                null);
        }

        var beforeClaim = occurrences.Where(item => item.Index <= claimIndex).ToArray();
        TitleOccurrence subject;
        TitleOccurrence? comparedWith = null;
        var line = response[lineStart..lineEnd];

        if (occurrences.Length >= 2 &&
            Regex.IsMatch(
                line[..Math.Max(0, occurrences[0].Index - lineStart)],
                @"\bcompared\s+(?:with|to)\s*$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            comparedWith = occurrences[0];
            subject = occurrences[1];
        }
        else if (occurrences.Length >= 2 &&
                 occurrences[1].Index + occurrences[1].Title.Length <= claimIndex &&
                 Regex.IsMatch(
                     response[(occurrences[0].Index + occurrences[0].Title.Length)..occurrences[1].Index],
                     @"\b(?:leads?|exceeds?|trails?|is\s+(?:ahead|behind)|vs\.?|versus|compared\s+(?:with|to))\b",
                     RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) &&
                 Regex.IsMatch(
                     response[(occurrences[1].Index + occurrences[1].Title.Length)..claimIndex],
                     @"\bby\s*$",
                     RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            subject = occurrences[0];
            comparedWith = occurrences[1];
        }
        else
        {
            subject = beforeClaim.LastOrDefault() ?? occurrences[0];
            comparedWith = occurrences
                .Where(item => !string.Equals(item.Title, subject.Title, StringComparison.OrdinalIgnoreCase))
                .OrderBy(item => Math.Abs(item.Index - claimIndex))
                .FirstOrDefault();
        }

        return new CampaignAttribution(subject.Title, comparedWith?.Title);
    }

    private static TitleOccurrence[] FindTitleOccurrences(
        string response,
        IReadOnlyCollection<string> titles,
        int start,
        int end) =>
        titles
            .SelectMany(title => FindOccurrences(response, title, start, end)
                .Select(index => new TitleOccurrence(title, index)))
            .OrderBy(item => item.Index)
            .ToArray();

    private static CampaignAttribution? FindComparisonHeading(
        string response,
        IReadOnlyCollection<string> titles,
        int currentLineStart)
    {
        var searchEnd = Math.Max(0, currentLineStart - 1);
        for (var lines = 0; lines < 10 && searchEnd > 0; lines++)
        {
            var lineStart = response.LastIndexOf('\n', Math.Max(0, searchEnd - 1)) + 1;
            var line = response[lineStart..searchEnd];
            if (string.IsNullOrWhiteSpace(line))
            {
                return null;
            }

            var occurrences = FindTitleOccurrences(
                response,
                titles,
                lineStart,
                searchEnd);
            if (occurrences.Length >= 2)
            {
                if (Regex.IsMatch(
                    line,
                    @"\b(?:vs\.?|versus|compared\s+(?:with|to))\b",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                {
                    if (Regex.IsMatch(
                        line[..Math.Max(0, occurrences[0].Index - lineStart)],
                        @"\bcompared\s+(?:with|to)\s*$",
                        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                    {
                        return new CampaignAttribution(
                            occurrences[1].Title,
                            occurrences[0].Title);
                    }

                    return new CampaignAttribution(
                        occurrences[0].Title,
                        occurrences[1].Title);
                }
            }

            searchEnd = Math.Max(0, lineStart - 1);
        }

        return null;
    }

    private static IEnumerable<int> FindOccurrences(string text, string value, int start, int end)
    {
        var index = start;
        while (index < end)
        {
            index = text.IndexOf(value, index, end - index, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
            {
                yield break;
            }

            yield return index;
            index += value.Length;
        }
    }

    private static string? FindDifferenceDirection(string response, Match match)
    {
        var sign = match.Groups["sign"].Value;
        if (sign == "+")
        {
            return CampaignComparisonCalculator.Higher;
        }

        if (sign is "-" or "−")
        {
            return CampaignComparisonCalculator.Lower;
        }

        var clauseStart = FindClauseStart(response, match.Index);
        var clauseEnd = FindClauseEnd(response, match.Index);
        var before = response[clauseStart..match.Index];
        var after = response[(match.Index + match.Length)..clauseEnd];

        if (Regex.IsMatch(
            after,
            @"^\s*(?:%|percent(?:age)?(?:\s+points?)?|pp|eligible\s+impressions?|impressions?|audiences?|viewers?|frequency)?\s*(?:more|higher|greater|ahead|above)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) ||
            Regex.IsMatch(
                before,
                @"\b(?:higher|greater|ahead|leads?|exceeds?|above)(?:\s+\w+){0,3}\s+by\s*$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            return CampaignComparisonCalculator.Higher;
        }

        if (Regex.IsMatch(
            after,
            @"^\s*(?:%|percent(?:age)?(?:\s+points?)?|pp|eligible\s+impressions?|impressions?|audiences?|viewers?|frequency)?\s*(?:fewer|lower|less|behind|below)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) ||
            Regex.IsMatch(
                before,
                @"\b(?:lower|less|behind|trails?|below)(?:\s+\w+){0,3}\s+by\s*$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            return CampaignComparisonCalculator.Lower;
        }

        var explicitlyDifference = Regex.IsMatch(
            before,
            @"\bdifference(?:\s+(?:of|is|was))?\s*[:=]?\s*$|\bby\s*$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) ||
            Regex.IsMatch(
                after,
                @"^\s*(?:%|percent(?:age)?(?:\s+points?)?|pp)?\s+difference\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (explicitlyDifference &&
            double.TryParse(
                match.Groups["value"].Value,
                CultureInfo.InvariantCulture,
                out var value) &&
            Math.Abs(value) < double.Epsilon)
        {
            return CampaignComparisonCalculator.Equal;
        }

        return null;
    }

    private static string? ToDifferenceMetric(string? metric) => metric switch
    {
        "eligible_impressions" => "eligible_impressions_difference",
        "reach" => "reach_difference",
        "frequency" => "frequency_difference",
        "coverage" => "coverage_difference",
        "eligible_impressions_difference" or "reach_difference" or "frequency_difference" or "coverage_difference" => metric,
        _ => null
    };

    private static string? FindDifferenceMetric(string response, Match match)
    {
        var lineEnd = response.IndexOf('\n', match.Index);
        if (lineEnd < 0)
        {
            lineEnd = response.Length;
        }

        var afterEnd = response.IndexOfAny([',', ';', '.'], match.Index + match.Length);
        if (afterEnd < 0 || afterEnd > lineEnd)
        {
            afterEnd = lineEnd;
        }

        var after = response[(match.Index + match.Length)..afterEnd];
        if (Regex.IsMatch(after, @"\beligible\s+impressions?\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            return "eligible_impressions_difference";
        }

        if (Regex.IsMatch(after, @"\b(?:reach|audiences?|viewers?)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            return "reach_difference";
        }

        if (Regex.IsMatch(after, @"\bfrequency\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            return "frequency_difference";
        }

        if (Regex.IsMatch(after, @"\b(?:coverage|percentage\s+points?|pp)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            return "coverage_difference";
        }

        var lineStart = response.LastIndexOf('\n', Math.Max(0, match.Index - 1)) + 1;
        var before = response[lineStart..match.Index];
        var candidates = MetricPhrases
            .Where(item => item.Metric is
                "eligible_impressions" or "reach" or "frequency" or "coverage" or
                "eligible_impressions_difference" or "reach_difference" or
                "frequency_difference" or "coverage_difference")
            .Select(item => new
            {
                item.Metric,
                Index = before.LastIndexOf(
                    item.Phrase,
                    StringComparison.OrdinalIgnoreCase)
            })
            .Where(item => item.Index >= 0)
            .OrderByDescending(item => item.Index)
            .FirstOrDefault();
        if (candidates is not null)
        {
            return ToDifferenceMetric(candidates.Metric);
        }

        return null;
    }

    private static string? FindMarkdownTableMetric(string response, int claimIndex)
    {
        var rowStart = response.LastIndexOf('\n', Math.Max(0, claimIndex - 1)) + 1;
        var rowEnd = response.IndexOf('\n', claimIndex);
        if (rowEnd < 0)
        {
            rowEnd = response.Length;
        }

        var row = response[rowStart..rowEnd];
        if (!row.TrimStart().StartsWith('|') || row.Count(character => character == '|') < 2)
        {
            return null;
        }

        var column = response[rowStart..claimIndex].Count(character => character == '|') - 1;
        var separatorStart = -1;
        var separatorEnd = -1;
        var searchEnd = rowStart - 1;
        for (var lines = 0; lines < 20 && searchEnd > 0; lines++)
        {
            var candidateStart = response.LastIndexOf(
                '\n',
                Math.Max(0, searchEnd - 1)) + 1;
            var candidate = response[candidateStart..searchEnd];
            if (Regex.IsMatch(
                candidate,
                @"^\s*\|?(?:\s*:?-{3,}:?\s*\|)+\s*$",
                RegexOptions.CultureInvariant))
            {
                separatorStart = candidateStart;
                separatorEnd = searchEnd;
                break;
            }

            if (string.IsNullOrWhiteSpace(candidate) ||
                !candidate.TrimStart().StartsWith('|'))
            {
                return null;
            }

            searchEnd = Math.Max(0, candidateStart - 1);
        }

        if (separatorStart < 0 || separatorEnd < 0)
        {
            return null;
        }

        var headerEnd = separatorStart - 1;
        var headerStart = response.LastIndexOf('\n', Math.Max(0, headerEnd - 1)) + 1;
        var headers = response[headerStart..headerEnd]
            .Trim()
            .Trim('|')
            .Split('|', StringSplitOptions.TrimEntries);
        if (column < 0 || column >= headers.Length)
        {
            return null;
        }

        var header = headers[column];
        if (header.Contains("eligible", StringComparison.OrdinalIgnoreCase) &&
            header.Contains("impression", StringComparison.OrdinalIgnoreCase))
        {
            return "eligible_impressions";
        }

        if (header.Contains("reach", StringComparison.OrdinalIgnoreCase)) return "reach";
        if (header.Contains("frequency", StringComparison.OrdinalIgnoreCase)) return "frequency";
        if (header.Contains("coverage", StringComparison.OrdinalIgnoreCase)) return "coverage";
        if (header.Contains("total", StringComparison.OrdinalIgnoreCase) &&
            header.Contains("impression", StringComparison.OrdinalIgnoreCase)) return "total_impressions";
        return null;
    }

    private static bool IsListOrdinal(string response, Match match)
    {
        var valueGroup = match.Groups["value"];
        var lineStart = response.LastIndexOf('\n', Math.Max(0, valueGroup.Index - 1)) + 1;
        if (!string.IsNullOrWhiteSpace(response[lineStart..valueGroup.Index]))
        {
            return false;
        }

        var markerIndex = valueGroup.Index + valueGroup.Length;
        if (markerIndex >= response.Length || (response[markerIndex] != '.' && response[markerIndex] != ')'))
        {
            return false;
        }

        var afterMarker = markerIndex + 1;
        return afterMarker >= response.Length || char.IsWhiteSpace(response[afterMarker]);
    }

    private sealed record TitleOccurrence(string Title, int Index);

    private sealed record CampaignAttribution(string? Title, string? ComparedWithTitle);

    private sealed record ReviewedNumericClaim(
        NumericClaim Claim,
        bool IsSupported);

    private sealed record NumericClaim(
        string Text,
        double Value,
        int DecimalPlaces,
        string? Title,
        string? Metric,
        string? ComparedWithTitle,
        string? Direction = null);

    private sealed record EvidenceValue(
        double Value,
        string? Title,
        string? Metric,
        string? ComparedWithTitle,
        string? Direction = null);

    private static readonly (string Phrase, string Metric)[] MetricPhrases =
    [
        ("eligible impression difference", "eligible_impressions_difference"),
        ("eligible impressions difference", "eligible_impressions_difference"),
        ("coverage difference", "coverage_difference"),
        ("frequency difference", "frequency_difference"),
        ("reach difference", "reach_difference"),
        ("percentage point difference", "coverage_difference"),
        ("eligible exposure coverage", "coverage"),
        ("coverage rate", "coverage"),
        ("completion rate", "completion_rate"),
        ("ad exposure rate", "ad_exposure_rate"),
        ("viewing sessions", "viewing_sessions"),
        ("sessions", "viewing_sessions"),
        ("exposure events", "exposure_events"),
        ("total impressions", "total_impressions"),
        ("eligible impressions", "eligible_impressions"),
        ("eligible impression", "eligible_impressions"),
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
        @"(?<![\p{L}\p{N}_])(?<sign>[+\-−])?\s*(?<value>\d+(?:\.\d+)?)(?:\s*(?:%|percent(?:age)?(?:\s+points?)?|viewing\s+sessions?|sessions?))?",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NumericClaimRegex();

    [GeneratedRegex(
        @"\b\d{4}-\d{2}-\d{2}(?:[Tt]\d{2}:\d{2}(?::\d{2}(?:\.\d+)?)?(?:[Zz]|[+-]00:00)?)?\b",
        RegexOptions.CultureInvariant)]
    private static partial Regex IsoDateRegex();

    [GeneratedRegex(
        @"\b(?:device|audience)[-_][\p{L}\p{N}_-]+\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SensitiveIdentifierRegex();
}
