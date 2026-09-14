# ADR-0009 — LoginResult Authenticated vs MfaRequired

- **Status:** Accepted (Phase F Option A)

## Context

MFA-enabled login needs a success path that is not “tokens” and is not a generic validation failure.

## Decision

`LoginUseCase` returns `Result<LoginResult>` with mutually exclusive:

- `Authenticated(AuthenticationResult)`
- `MfaRequired(MfaProof, ExpiresAtUtc)`

`AuthenticationResult` remains “fully authenticated credentials only.” MFA-required is a **success discriminant**, not `Result.Failure`.

## Verified rationale

Explicit Phase F user approval of Option A for login API shape.

## Consequences

Hosts must handle both success shapes. Password step must not issue tokens when MFA is required.

## Alternatives considered

Other shapes were discussed in Phase F decisioning; Option A was approved. Full text of rejected options not reproduced here — do not invent them.

## Source/evidence note

Phase F approval; `LoginResult.cs`; `LoginUseCase.cs`.
