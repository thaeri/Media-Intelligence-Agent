using System.Security.Cryptography;
using System.Text;
using MediaIntelligence.Core.Models;

namespace MediaIntelligence.Core.Services;

public static class MediaEmbeddingDocument
{
    public const int ContentVersion = 1;

    public static string CreateText(MediaContent content) =>
        string.Join(' ', new[]
        {
            content.Title,
            content.Genre,
            content.ContentType,
            content.Region,
            string.Join(' ', content.Tags.Order(StringComparer.OrdinalIgnoreCase))
        });

    public static string CreateHash(MediaContent content)
    {
        var canonical = CreateText(content).ToLowerInvariant();
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))
            .ToLowerInvariant();
    }
}
