namespace MediaIntelligence.Core.Models
{
    public sealed record MediaContent(
        Guid Id,
        string Title,
        string Genre,
        string ContentType,
        string Region,
        DateTime ReleaseDate,
        IReadOnlyList<string> Tags
    );
}