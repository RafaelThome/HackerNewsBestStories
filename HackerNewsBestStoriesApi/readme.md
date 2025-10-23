
# HackerNews Best Stories API

This project implements an ASP.NET Core REST API that returns the first `n` "best stories" from Hacker News, sorted by score (descending), in the following shape:

```json
[
  {
    "title": "A uBlock Origin update was rejected from the Chrome Web Store",
    "uri": "https://github.com/uBlockOrigin/uBlock-issues/issues/745",
    "postedBy": "ismaildonmez",
    "time": "2019-10-12T13:43:01+00:00",
    "score": 1716,
    "commentCount": 572
  },
  ...
]
```

## How to run

Requirements:

* .NET 9 SDK or later (download from https://dotnet.microsoft.com/)

* Git (optional)

Steps:

1. Clone the repository

2. cd into the project folder

3. dotnet restore

4. dotnet run (or dotnet run --project ./HackerNewsBestStoriesApi.csproj)

5. The API runs by default on http://localhost:2600 and https://localhost:2662.

6. Example request:

```bash
GET https://localhost:5001/api/beststories?n=10
```

## Assumptions

Caller passes n as a positive integer; sensible maximum applied (default max = 500).

HN’s "beststories" list ordering is authoritative for "first n" but final output is sorted by score.

## Design highlights to avoid overloading Hacker News

1. Caching:

   _ Best-story IDs cached briefly (configurable, default 1s).

   _ Individual story details cached longer (configurable, default 10m).

   _ Cache stores Task<...> results so concurrent requests don't cause duplicate fetches (prevents thundering herd).

2. Concurrency control:

   _ Limit concurrent HTTP calls to HN using Polly.RateLimiter + SemaphoreSlim + Parallel.ForEachAsync with a configurable MaxDegreeOfParallelism (default = 10 per core).

   _ Limit HTTP calls to itself per client (IP) using Microsoft.AspNetCore.RateLimiting

3. HttpClientFactory used to manage connections efficiently.

4. Retry and Circuit Breaker patterns used for resilience

## Enhancements I would make given more time

Support page and pageSize for large n values.

Pre-warm or background-refresh top N stories periodically to improve latency.

Use a background service to refresh best stories periodically.

Add automated tests and CI/CD pipeline and containerization (Dockerfile).

Add logging with structured logs and correlation IDs.

Use a distributed cache (Redis) for multi-instance deployments.

Add metrics (Prometheus) and health checks.
