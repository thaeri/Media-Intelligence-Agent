using System.Text.RegularExpressions;
using MediaIntelligence.Core.Interfaces;

namespace MediaIntelligence.Core.Services;

public sealed partial class DeterministicMediaEmbeddingProvider
    : IMediaEmbeddingProvider
{
    public const int VectorDimension = 64;
    public string Model => "deterministic-token-hash-v1";
    public int Dimension => VectorDimension;

    public float[] Embed(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        var values = new float[Dimension];
        foreach (Match match in TokenPattern().Matches(text.ToLowerInvariant()))
        {
            var hash = 2166136261u;
            foreach (var character in match.Value)
            {
                hash ^= character;
                hash *= 16777619u;
            }

            values[hash % (uint)Dimension] += 1;
        }

        var norm = Math.Sqrt(values.Sum(value => value * value));
        if (norm > 0)
        {
            for (var index = 0; index < values.Length; index++)
            {
                values[index] = (float)(values[index] / norm);
            }
        }

        return values;
    }

    [GeneratedRegex("[\\p{L}\\p{N}]+", RegexOptions.CultureInvariant)]
    private static partial Regex TokenPattern();
}
