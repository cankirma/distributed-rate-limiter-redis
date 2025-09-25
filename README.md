# Distributed Rate Limiter (Redis-backed)

This repository contains a .NET-based, Redis-backed distributed rate limiting solution. It includes reusable core algorithms, an ASP.NET Core API surface, infrastructure bindings, automated tests, and a BenchmarkDotNet project for performance profiling.

## Project layout

- `src/RateLimiter.Core` – Core abstractions, token bucket and leaky bucket implementations, sliding window metrics.
- `src/RateLimiter.Api` – Minimal API exposing rate limited endpoints with middleware and endpoint filters.
- `src/RateLimiter.Infrastructure` – Infrastructure services and Redis integration wiring.
- `benchmarks/RateLimiter.Benchmarks` – BenchmarkDotNet suite covering algorithm hot paths.
- `tests/*` – Unit and integration tests.

## Prerequisites

- .NET SDK 9.0.303 or later
- Redis instance (for infrastructure/integration scenarios)

## Build & test

```bash
dotnet build
dotnet test
```

## Running the API

```bash
dotnet run --project src/RateLimiter.Api/RateLimiter.Api.csproj
```

## Docker

Build the API container image:

```bash
docker build -t ratelimiter-api .
```

Run the container (ensure a Redis instance is reachable; adjust the connection string as needed):

```bash
docker run --rm -p 8080:8080 \
  -e RateLimiter__Redis__ConnectionString=redis:6379 \
  ratelimiter-api
```

The container listens on port 8080 and exposes `/healthz`, `/metrics`, and the sample endpoints under `/api`.

## Benchmarks

Execute the BenchmarkDotNet suite to compare algorithm performance:

```bash
dotnet run -c Release --project benchmarks/RateLimiter.Benchmarks/RateLimiter.Benchmarks.csproj
```

Latest results (Apple M4 Pro, .NET 9.0.7):

| Method | Mean | StdDev |
| --- | ---: | ---: |
| Token bucket – allowed request | 3.70 ns | 0.04 ns |
| Token bucket – denied request | 3.34 ns | 0.03 ns |
| Leaky bucket – allowed request | 2.74 ns | 0.03 ns |
| Leaky bucket – denied request | 3.91 ns | 0.01 ns |
| Sliding window sample update | 48.88 ns | 0.65 ns |

Benchmark artifacts are written to `BenchmarkDotNet.Artifacts/`, including CSV, HTML, and GitHub-flavored markdown reports for deeper analysis.

