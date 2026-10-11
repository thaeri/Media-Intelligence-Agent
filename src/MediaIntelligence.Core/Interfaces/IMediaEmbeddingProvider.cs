namespace MediaIntelligence.Core.Interfaces;

public interface IMediaEmbeddingProvider
{
    string Model { get; }
    int Dimension { get; }
    float[] Embed(string text);
}
