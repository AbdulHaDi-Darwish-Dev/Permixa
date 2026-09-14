# Phase I-0 — Reusable ASP.NET Core Rate Limiting

## Goal

Add an opt-in, type-safe Rate Limiting configuration API in `Permixa.AspNetCore` over ASP.NET Core’s native Rate Limiting — usable for IAM and non-IAM endpoints.

## Outcome

Implemented:

- `AddPermixaRateLimiting`
- `AddFixedWindow` / `AddSlidingWindow` / `AddTokenBucket` / `AddConcurrency`
- Startup validation, duplicate-name rejection
- Built-in partitions: RemoteIp / Global / AuthenticatedUserId
- HTTP 429 + optional Retry-After + minimal ProblemDetails (`RateLimiting.TooManyRequests`)
- AspNetCore tests for algorithms, validation, opt-in, partitions, 429 safety
- Docs + ADR-0013

Not implemented (by design):

- Built-in IAM policy catalog
- Distributed Redis/SQL rate limiting
- Global host limiter
- Custom partition delegates
- NuGet packaging

## Verified baseline after phase

```text
Domain           36
Application     224
Infrastructure  187
AspNetCore       52
Integration      51
Total           550 / 550

Build: 0 errors, 0 warnings
Skipped: 0
```

Recorded in `CURRENT-STATE.md` / `AGENT-HANDOFF.md`.
