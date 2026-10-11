import http from 'k6/http';
import { check } from 'k6';
import { Counter, Rate, Trend } from 'k6/metrics';

const baseUrl = __ENV.BASE_URL || 'http://api-memory:8080';
const mode = __ENV.MODE || 'api';
const storage = __ENV.STORAGE || 'inmemory';
const cityContent = '22222222-2222-2222-2222-222222222222';
const cityCampaign = '44444444-4444-4444-4444-444444444444';
const failures = new Rate('benchmark_errors');
const measuredIterations = new Counter('measured_iterations');
const mediaLatency = new Trend('media_latency', true);
const analyticsLatency = new Trend('analytics_latency', true);
const campaignLatency = new Trend('campaign_latency', true);
const apiWorkflowLatency = new Trend('api_workflow_latency', true);
const agentLatency = new Trend('agent_latency', true);

export const options = {
  scenarios: {
    warmup: {
      executor: 'constant-vus', exec: 'warmup', vus: Number(__ENV.VUS || 4),
      duration: __ENV.WARMUP || '5s', gracefulStop: '0s',
    },
    measured: {
      executor: 'constant-vus', exec: 'measured', vus: Number(__ENV.VUS || 4),
      startTime: __ENV.WARMUP || '5s', duration: __ENV.DURATION || '15s',
      gracefulStop: '5s',
    },
  },
  thresholds: { benchmark_errors: ['rate<0.01'] },
  summaryTrendStats: ['avg', 'min', 'med', 'p(90)', 'p(95)', 'p(99)', 'max'],
};

function requestApi(record) {
  let workflowDuration = 0;
  const calls = [
    ['media', `${baseUrl}/api/media`, mediaLatency],
    ['analytics', `${baseUrl}/api/media/${cityContent}/analytics`, analyticsLatency],
    ['campaign', `${baseUrl}/api/benchmark/campaigns/${cityCampaign}/analytics`, campaignLatency],
  ];
  for (const [name, url, metric] of calls) {
    const response = http.get(url, { tags: { endpoint: name, storage } });
    const ok = check(response, { [`${name} status 200`]: r => r.status === 200 });
    failures.add(!ok, { endpoint: name, storage });
    workflowDuration += response.timings.duration;
    if (record) metric.add(response.timings.duration, { storage });
  }
  if (record) apiWorkflowLatency.add(workflowDuration, { storage });
}

function requestAgent(record) {
  const marker = mode === 'agent-retry' ? '[retry]' :
    mode === 'agent-failure' ? '[failure]' : '';
  const response = http.post(`${baseUrl}/api/agent/ask`, JSON.stringify({
    question: `${marker} Compare campaign eligible impressions.`,
  }), { headers: { 'Content-Type': 'application/json' }, tags: { endpoint: mode, storage } });
  const expectedFailure = mode === 'agent-failure';
  const ok = check(response, {
    'agent status 200': r => r.status === 200,
    'agent fail-closed behavior': r => expectedFailure
      ? r.body.includes('Structured response verification failed')
      : !r.body.includes('Structured response verification failed') && r.body.includes('eligible impressions'),
  });
  failures.add(!ok, { endpoint: mode, storage });
  if (record) agentLatency.add(response.timings.duration, { storage, scenario: mode });
}

export function warmup() { mode === 'api' ? requestApi(false) : requestAgent(false); }
export function measured() {
  mode === 'api' ? requestApi(true) : requestAgent(true);
  measuredIterations.add(1, { storage, scenario: mode });
}

export function handleSummary(data) {
  return {
    [`/benchmarks/results/${storage}-${mode}.json`]: JSON.stringify(data, null, 2),
    stdout: `completed ${storage}/${mode}\n`,
  };
}
