using Microsoft.Extensions.Options;

namespace MediaIntelligence.Core.Services;

public sealed class StreamingOptions
{
    public const string SectionName = "Streaming";
    public bool Enabled { get; set; }
    public string BootstrapServers { get; set; } = "localhost:19092";
    public string ConsumerGroup { get; set; } = "media-intelligence-v1";
    public int MaxRetries { get; set; } = 3;
    public int RetryDelayMilliseconds { get; set; } = 250;
    public int ProducerViewingCount { get; set; } = 1000;
    public int ProducerAdCount { get; set; } = 500;
    public int ProducerRatePerSecond { get; set; }
    public int ProducerBurstSize { get; set; } = 100;
    public int ProducerDuplicateEvery { get; set; }
    public bool ProducerIncludePoison { get; set; }
    public int ProducerConcurrency { get; set; } = 1;
    public int ProducerBatchSize { get; set; } = 1;
    public int ProducerLingerMilliseconds { get; set; }
    public int ProducerSeed { get; set; }
    public bool ProducerUseCurrentTime { get; set; }
}

public sealed class StreamingOptionsValidator : IValidateOptions<StreamingOptions>
{
    public ValidateOptionsResult Validate(string? name, StreamingOptions options)
    {
        var failures = new List<string>();
        if (string.IsNullOrWhiteSpace(options.BootstrapServers))
            failures.Add("Streaming:BootstrapServers is required.");
        if (string.IsNullOrWhiteSpace(options.ConsumerGroup))
            failures.Add("Streaming:ConsumerGroup is required.");
        if (options.MaxRetries is < 1 or > 10)
            failures.Add("Streaming:MaxRetries must be between 1 and 10.");
        if (options.RetryDelayMilliseconds is < 10 or > 30_000)
            failures.Add("Streaming:RetryDelayMilliseconds must be between 10 and 30000.");
        if (options.ProducerViewingCount < 0 || options.ProducerAdCount < 0 ||
            options.ProducerRatePerSecond < 0 || options.ProducerDuplicateEvery < 0)
            failures.Add("Streaming producer counts and rates cannot be negative.");
        if (options.ProducerBurstSize < 1)
            failures.Add("Streaming:ProducerBurstSize must be at least 1.");
        if (options.ProducerConcurrency is < 1 or > 1024)
            failures.Add("Streaming:ProducerConcurrency must be between 1 and 1024.");
        if (options.ProducerBatchSize is < 1 or > 100_000)
            failures.Add("Streaming:ProducerBatchSize must be between 1 and 100000.");
        if (options.ProducerLingerMilliseconds is < 0 or > 1000)
            failures.Add("Streaming:ProducerLingerMilliseconds must be between 0 and 1000.");
        if (options.ProducerSeed is < 0 or > 999_999)
            failures.Add("Streaming:ProducerSeed must be between 0 and 999999.");
        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
