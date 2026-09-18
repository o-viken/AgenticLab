# Load test

`flow-loadtest.mjs` exercises the current Interactive Server Flow page with concurrent browser contexts.
It is intentionally outside the solution and does not require Azure credentials to compile. Install
Playwright in a temporary location before running it:

```sh
mkdir -p /tmp/the-series-loadtest
npm install --prefix /tmp/the-series-loadtest --no-save --package-lock=false playwright
NODE_PATH=/tmp/the-series-loadtest/node_modules node tools/flow-loadtest.mjs
```

Start the application first, for example with `dotnet run --project src/TheSeries.AppHost`, and set
`THESERIES_URL` to the externally reachable Web URL. The defaults are 10 concurrent users and 3 rounds:

Users run concurrently; rounds within each user's page run sequentially. The harness waits for Blazor
interactivity before filling the form and matches each submitted message to its new exchange. A failed
round is reported with its user/round and stops that user's remaining rounds. Against a real AI service,
this sends real model requests and can incur cost; use a fake API for an initial harness smoke test.

```sh
THESERIES_URL=http://127.0.0.1:5140 \
THESERIES_USERS=25 \
THESERIES_ROUNDS=5 \
NODE_PATH=/tmp/the-series-loadtest/node_modules \
node tools/flow-loadtest.mjs
```

The JSON output reports `firstExchangeMs` (local exchange appearance, not the first backend event),
completion time, successful sample count, Chromium JS heap when available, and page/run errors.
The browser heap estimate may be rounded or shared across pages and is not total browser memory.
Missing servers and initialization failures return a nonzero exit code with an error message.
Record Web and AiService process working set, managed heap, allocation rate,
`TheSeries.Web.Flow` metrics, `TheSeries.AiService.Flow` metrics and latency from Aspire/OpenTelemetry
at the same time. Repeat the profile for idle tabs, active runs, paused/manual runs, long conversations,
New conversation and closed tabs. Run the profile before and after any CSR migration; it is not a CI test
and it does not claim to measure server managed heap by itself.