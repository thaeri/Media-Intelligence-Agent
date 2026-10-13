[CmdletBinding()]
param(
    [Parameter(Mandatory)] [ValidatePattern('^[a-z0-9-]+$')] [string] $Scenario,
    [ValidateRange(1, 12)] [int] $Partitions = 3,
    [ValidateRange(1, 12)] [int] $Consumers = 1,
    [ValidateRange(1, 1000)] [int] $ConsumerBatchSize = 1,
    [ValidateRange(1, 5000)] [int] $ConsumerBatchWaitMilliseconds = 25,
    [ValidateRange(0, 999999)] [int] $ViewingEvents = 1000,
    [ValidateRange(0, 999999)] [int] $AdImpressions = 500,
    [ValidateRange(1, 1024)] [int] $ProducerConcurrency = 64,
    [ValidateRange(1, 100000)] [int] $ProducerBatchSize = 1000,
    [ValidateRange(0, 1000)] [int] $ProducerLingerMilliseconds = 5,
    [ValidateRange(0, 999999)] [int] $Seed = 1,
    [string] $PostgresPassword = 'stream-benchmark-local',
    [switch] $IncludePoison,
    [switch] $VerifyRestartRecovery,
    [switch] $KeepEnvironment
)

$ErrorActionPreference = 'Stop'
$project = "mi-stream-$Scenario"
$compose = @('-p', $project, '-f', 'compose.yaml', '-f',
    'compose.benchmark.yaml', '-f', 'compose.streaming.yaml',
    '--profile', 'streaming')
$env:POSTGRES_PASSWORD = $PostgresPassword
$env:STREAM_PARTITIONS = $Partitions.ToString()
$env:STREAM_CONSUMER_BATCH_SIZE = $ConsumerBatchSize.ToString()
$env:STREAM_CONSUMER_BATCH_WAIT_MS = $ConsumerBatchWaitMilliseconds.ToString()
$env:STREAM_VIEWING_COUNT = $ViewingEvents.ToString()
$env:STREAM_AD_COUNT = $AdImpressions.ToString()
$env:STREAM_DUPLICATE_EVERY = '0'
$env:STREAM_INCLUDE_POISON = $IncludePoison.ToString().ToLowerInvariant()
$env:STREAM_PRODUCER_CONCURRENCY = $ProducerConcurrency.ToString()
$env:STREAM_PRODUCER_BATCH_SIZE = $ProducerBatchSize.ToString()
$env:STREAM_PRODUCER_LINGER_MS = $ProducerLingerMilliseconds.ToString()
$env:STREAM_PRODUCER_SEED = $Seed.ToString()
$env:STREAM_USE_CURRENT_TIME = 'true'
$env:API_PORT = '0'

function Invoke-Compose([string[]] $Arguments) {
    & docker compose @compose @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "docker compose failed with exit code $LASTEXITCODE"
    }
}

function Read-LedgerCount {
    $container = "$project-postgres-1"
    $value = & docker exec $container psql -U media_app `
        -d media_intelligence_test -tAc 'SELECT count(*) FROM stream_ingestion'
    if ($LASTEXITCODE -ne 0) { throw 'Could not read stream ledger.' }
    return [int]$value
}

function Wait-Ledger([int] $Expected, [int] $TimeoutSeconds = 300) {
    $watch = [Diagnostics.Stopwatch]::StartNew()
    while ($watch.Elapsed.TotalSeconds -lt $TimeoutSeconds) {
        if ((Read-LedgerCount) -eq $Expected) {
            $watch.Stop()
            return $watch.Elapsed.TotalSeconds
        }
        Start-Sleep -Milliseconds 100
    }
    throw "Timed out waiting for $Expected durable events."
}

function Wait-ZeroLag([int] $TimeoutSeconds = 300) {
    $watch = [Diagnostics.Stopwatch]::StartNew()
    while ($watch.Elapsed.TotalSeconds -lt $TimeoutSeconds) {
        $description = & docker exec "$project-redpanda-1" rpk group describe `
            media-intelligence-v1 -X brokers=redpanda:9092 2>$null | Out-String
        if ($description -match 'TOTAL-LAG\s+0') { return }
        Start-Sleep -Milliseconds 200
    }
    throw 'Timed out waiting for zero consumer lag.'
}

try {
    Invoke-Compose @('up', '-d', '--build', 'postgres', 'migrate',
        'redpanda', 'stream-topics', 'api')

    $savedPreference = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    $producerOutput = & docker compose @compose run --rm stream-producer 2>&1 |
        Out-String
    $producerExitCode = $LASTEXITCODE
    $ErrorActionPreference = $savedPreference
    if ($producerExitCode -ne 0) { throw $producerOutput }
    Write-Verbose $producerOutput
    if ($producerOutput -notmatch '\(([0-9.]+) events/s\)') {
        throw 'Producer throughput was not found in output.'
    }
    $producerRate = [double]$Matches[1]

    $drainWatch = [Diagnostics.Stopwatch]::StartNew()
    Invoke-Compose @('up', '-d', '--scale', "stream-consumer=$Consumers",
        'stream-consumer')
    $drainSeconds = Wait-Ledger ($ViewingEvents + $AdImpressions)
    Wait-ZeroLag
    $drainWatch.Stop()
    $databaseCommits = [long](& docker exec "$project-postgres-1" psql `
        -U media_app -d media_intelligence_test -tAc `
        "SELECT count(DISTINCT to_jsonb(stream_ingestion)->>'ProcessedAtUtc') FROM stream_ingestion")
    if ($LASTEXITCODE -ne 0) { throw 'Could not read database transaction count.' }
    $consumerRate = ($ViewingEvents + $AdImpressions) /
        [Math]::Max($drainSeconds, 0.001)

    $sql = "SELECT percentile_cont(ARRAY[0.5,0.95,0.99]) WITHIN GROUP (ORDER BY freshness) FROM (SELECT EXTRACT(EPOCH FROM ((to_jsonb(s)->>'ProcessedAtUtc')::timestamptz - (to_jsonb(s)->>'EventTimeUtc')::timestamptz)) AS freshness FROM stream_ingestion s) measured"
    $percentilesText = & docker exec "$project-postgres-1" psql -U media_app `
        -d media_intelligence_test -tAc $sql
    if ($LASTEXITCODE -ne 0) { throw 'Could not calculate freshness.' }
    $percentiles = $percentilesText.Trim().Trim('{', '}').Split(',') |
        ForEach-Object { [double]$_ }

    $apiContainer = "$project-api-1"
    $mediaJson = & docker exec $apiContainer curl --fail --silent `
        http://localhost:8080/api/media/22222222-2222-2222-2222-222222222222/analytics
    $campaignJson = & docker exec $apiContainer curl --fail --silent `
        http://localhost:8080/api/benchmark/campaigns/44444444-4444-4444-4444-444444444444/analytics
    $media = $mediaJson | ConvertFrom-Json
    $campaign = $campaignJson | ConvertFrom-Json

    $beforeReplayLedger = Read-LedgerCount
    $ErrorActionPreference = 'Continue'
    $replayOutput = & docker compose @compose run --rm stream-producer 2>&1 |
        Out-String
    $replayExitCode = $LASTEXITCODE
    $ErrorActionPreference = $savedPreference
    if ($replayExitCode -ne 0) { throw $replayOutput }
    Wait-ZeroLag
    $afterReplayLedger = Read-LedgerCount
    $replayedMedia = (& docker exec $apiContainer curl --fail --silent `
        http://localhost:8080/api/media/22222222-2222-2222-2222-222222222222/analytics) |
        ConvertFrom-Json
    $replayedCampaign = (& docker exec $apiContainer curl --fail --silent `
        http://localhost:8080/api/benchmark/campaigns/44444444-4444-4444-4444-444444444444/analytics) |
        ConvertFrom-Json
    if ($afterReplayLedger -ne $beforeReplayLedger -or
        $replayedMedia.viewingSessions -ne $media.viewingSessions -or
        $replayedCampaign.metrics.totalImpressions -ne $campaign.metrics.totalImpressions) {
        throw 'Replay changed durable or HTTP-visible totals.'
    }

    # Use in-campaign event time and unknown synthetic device IDs to prove that
    # persisted impressions affect totals without bypassing default-deny consent.
    $consentCheckAds = 12
    $env:STREAM_VIEWING_COUNT = '0'
    $env:STREAM_AD_COUNT = $consentCheckAds.ToString()
    $env:STREAM_PRODUCER_SEED = (($Seed + 250000) % 1000000).ToString()
    $env:STREAM_USE_CURRENT_TIME = 'false'
    $ErrorActionPreference = 'Continue'
    $consentProducerOutput = & docker compose @compose run --rm stream-producer 2>&1 |
        Out-String
    $consentProducerExitCode = $LASTEXITCODE
    $ErrorActionPreference = $savedPreference
    if ($consentProducerExitCode -ne 0) { throw $consentProducerOutput }
    [void](Wait-Ledger ($afterReplayLedger + $consentCheckAds))
    Wait-ZeroLag
    $consentCampaign = (& docker exec $apiContainer curl --fail --silent `
        http://localhost:8080/api/benchmark/campaigns/44444444-4444-4444-4444-444444444444/analytics) |
        ConvertFrom-Json
    if ($consentCampaign.metrics.totalImpressions -ne
            $campaign.metrics.totalImpressions + $consentCheckAds -or
        $consentCampaign.metrics.eligibleImpressions -ne
            $campaign.metrics.eligibleImpressions -or
        $consentCampaign.metrics.exclusions.unknownDevice -ne
            $campaign.metrics.exclusions.unknownDevice + $consentCheckAds) {
        throw 'Consent-ineligible impression verification failed.'
    }

    $restartLag = $null
    $restartRecoverySeconds = $null
    if ($VerifyRestartRecovery) {
        Invoke-Compose @('stop', 'stream-consumer')
        $restartViewing = 30
        $restartAds = 15
        $restartEvents = $restartViewing + $restartAds
        $env:STREAM_VIEWING_COUNT = $restartViewing.ToString()
        $env:STREAM_AD_COUNT = $restartAds.ToString()
        $env:STREAM_PRODUCER_SEED = (($Seed + 500000) % 1000000).ToString()
        $env:STREAM_USE_CURRENT_TIME = 'true'
        $ErrorActionPreference = 'Continue'
        $restartProducerOutput = & docker compose @compose run --rm stream-producer 2>&1 |
            Out-String
        $restartProducerExitCode = $LASTEXITCODE
        $ErrorActionPreference = $savedPreference
        if ($restartProducerExitCode -ne 0) { throw $restartProducerOutput }
        $description = & docker exec "$project-redpanda-1" rpk group describe `
            media-intelligence-v1 -X brokers=redpanda:9092 2>$null | Out-String
        if ($description -notmatch 'TOTAL-LAG\s+(\d+)') {
            throw 'Could not read restart backlog.'
        }
        $restartLag = [int]$Matches[1]
        $restartWatch = [Diagnostics.Stopwatch]::StartNew()
        Invoke-Compose @('up', '-d', '--scale', "stream-consumer=$Consumers",
            'stream-consumer')
        [void](Wait-Ledger ($afterReplayLedger + $consentCheckAds + $restartEvents))
        Wait-ZeroLag
        $restartWatch.Stop()
        $restartRecoverySeconds = [Math]::Round($restartWatch.Elapsed.TotalSeconds, 3)
    }

    [pscustomobject]@{
        scenario = $Scenario
        partitions = $Partitions
        consumers = $Consumers
        consumerBatchSize = $ConsumerBatchSize
        consumerBatchWaitMilliseconds = $ConsumerBatchWaitMilliseconds
        viewingEvents = $ViewingEvents
        adImpressions = $AdImpressions
        producerConcurrency = $ProducerConcurrency
        producerBatchSize = $ProducerBatchSize
        producerLingerMilliseconds = $ProducerLingerMilliseconds
        producerEventsPerSecond = $producerRate
        consumerDrainSeconds = [Math]::Round($drainSeconds, 3)
        consumerEventsPerSecond = [Math]::Round($consumerRate, 1)
        databaseCommitsDuringDrain = $databaseCommits
        databaseCommitsPerSecond = [Math]::Round(
            $databaseCommits / [Math]::Max($drainSeconds, 0.001), 1)
        freshnessP50Milliseconds = [Math]::Round($percentiles[0] * 1000, 1)
        freshnessP95Milliseconds = [Math]::Round($percentiles[1] * 1000, 1)
        freshnessP99Milliseconds = [Math]::Round($percentiles[2] * 1000, 1)
        cityViewingSessions = $media.viewingSessions
        cityCampaignTotalImpressions = $campaign.metrics.totalImpressions
        cityCampaignEligibleImpressions = $campaign.metrics.eligibleImpressions
        cityCampaignReach = $campaign.metrics.reach
        cityCampaignFrequency = $campaign.metrics.frequency
        cityCampaignCoveragePercent = $campaign.metrics.eligibleExposureCoverageRate
        cityCampaignUnknownDeviceExclusions = $campaign.metrics.exclusions.unknownDevice
        replayLedgerBefore = $beforeReplayLedger
        replayLedgerAfter = $afterReplayLedger
        consentCheckTotalImpressions = $consentCampaign.metrics.totalImpressions
        consentCheckEligibleImpressions = $consentCampaign.metrics.eligibleImpressions
        consentCheckUnknownDeviceExclusions = $consentCampaign.metrics.exclusions.unknownDevice
        restartBacklogEvents = $restartLag
        restartRecoverySeconds = $restartRecoverySeconds
    } | ConvertTo-Json
}
finally {
    if (-not $KeepEnvironment) {
        & docker compose @compose down -v --remove-orphans | Out-Null
    }
}
