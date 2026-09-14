# Permixa documentation

Canonical human-readable knowledge base for the **Permixa** reusable ASP.NET Core IAM framework.

## Start here

1. **[AGENT-HANDOFF.md](AGENT-HANDOFF.md)** — first file for every AI agent / new developer (operational snapshot).
2. **[CURRENT-STATE.md](CURRENT-STATE.md)** — living status, baseline, migrations, next work.
3. **[PROJECT-OVERVIEW.md](PROJECT-OVERVIEW.md)** — purpose and feature status map.
4. **[ARCHITECTURE.md](ARCHITECTURE.md)** — layers and dependency direction.
5. **[SECURITY-MODEL.md](SECURITY-MODEL.md)** — security invariants and logging policy.
6. **[decisions/README.md](decisions/README.md)** — ADR index.
7. Relevant subsystem under **[architecture/](architecture/)**.
8. **[DEVELOPMENT-GUIDELINES.md](DEVELOPMENT-GUIDELINES.md)** and **[DATA-ACCESS-GUIDELINES.md](DATA-ACCESS-GUIDELINES.md)** before coding.

Also: [TESTING-STRATEGY.md](TESTING-STRATEGY.md), [DEFERRED-SCOPE.md](DEFERRED-SCOPE.md), [CHANGELOG-PHASES.md](CHANGELOG-PHASES.md), [phases/](phases/).

## What is Permixa?

Reusable Identity & Access Management libraries for ASP.NET Core hosts: Identity-backed users/roles, JWT + refresh sessions, RBAC permissions with user overrides, role hierarchy, verification, email delivery, MFA v1, and IAM audit.

## Where to find…

| Need | Document |
|------|----------|
| Agent quick start | [AGENT-HANDOFF.md](AGENT-HANDOFF.md) |
| Current status / test baseline | [CURRENT-STATE.md](CURRENT-STATE.md) |
| Architecture decisions | [decisions/](decisions/) |
| Security constraints | [SECURITY-MODEL.md](SECURITY-MODEL.md) |
| Phase history | [CHANGELOG-PHASES.md](CHANGELOG-PHASES.md), [phases/](phases/) |
| Deferred features | [DEFERRED-SCOPE.md](DEFERRED-SCOPE.md) |
| Subsystems (auth, MFA, audit, …) | [architecture/](architecture/) |

## Future agent bootstrap

```text
1. Read AGENT-HANDOFF.md.
2. Read CURRENT-STATE.md.
3. Inspect git status and recent commits.
4. Read the relevant subsystem docs.
5. Read applicable ADRs.
6. Inspect current code/tests before changing behavior.
7. Never assume documentation is newer than code; verify material claims.
8. Stop for approval on unresolved architecture/security/schema/public API decisions.
9. After implementation, update documentation before reporting completion.
```

## Documentation ownership

Documentation is part of implementation. Non-trivial behavior/architecture/security/API/schema/phase changes must update the relevant docs and keep `AGENT-HANDOFF.md` / `CURRENT-STATE.md` current. See `.cursor/rules/project-documentation.mdc`.
