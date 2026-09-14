# Development guidelines

## Permanent decision rule

If a meaningful architecture, security, product behavior, persistence/schema, concurrency, public API, or compatibility decision is **not** already approved/documented:

1. **STOP**
2. Explain the issue
3. List realistic options with pros/cons
4. Give a recommendation
5. Wait for explicit approval

Do not silently expand scope.

## Documentation is part of done

Before saying “implementation complete”:

- [ ] Does [AGENT-HANDOFF.md](AGENT-HANDOFF.md) need updating?
- [ ] Does [CURRENT-STATE.md](CURRENT-STATE.md) need updating?
- [ ] Did architecture change? Update [ARCHITECTURE.md](ARCHITECTURE.md) / subsystem docs
- [ ] Did security behavior change? Update [SECURITY-MODEL.md](SECURITY-MODEL.md)
- [ ] Did public API change? Update docs + consider an ADR
- [ ] Did database schema change? Document migration; never rewrite old migrations
- [ ] Did configuration change?
- [ ] Did a deferred feature become implemented? Update [DEFERRED-SCOPE.md](DEFERRED-SCOPE.md)
- [ ] Did tests/baseline change? Update counts **only after verification**
- [ ] Was a significant decision approved? Add/update ADR under [decisions/](decisions/)
- [ ] Phase milestone? Update [CHANGELOG-PHASES.md](CHANGELOG-PHASES.md) / [phases/](phases/)

Trivial formatting/typo-only changes with no behavioral significance do not require doc updates.

## Historical honesty

- Do not invent WHY a decision was made.
- If only WHAT is known: state the implementation fact and mark rationale as not documented.
- Distinguish: verified historical decision | current implementation fact | open | deferred | future recommendation.

## Code quality expectations

- Match existing naming and layering.
- Prefer focused diffs; no drive-by refactors.
- Follow [DATA-ACCESS-GUIDELINES.md](DATA-ACCESS-GUIDELINES.md).
- Never commit secrets.
- Prefer set-based persistence; inspect for N+1.

## Testing

- Prefer project-by-project test runs if Docker/Testcontainers contention appears.
- Integration and Infrastructure suites need containers when those tests are not skipped by fixtures.

## Machine-enforced rules

- `.cursor/rules/database-access.mdc`
- `.cursor/rules/project-documentation.mdc`
