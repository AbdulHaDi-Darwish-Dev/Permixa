# ADR-0007 — RS256 JWT; no access-token blacklist

- **Status:** Accepted

## Context

Need signed access tokens for APIs with rotatable host-controlled keys.

## Decision

- Issue and validate JWTs with **RS256** and externally configured RSA keys.
- Do **not** implement an access-token blacklist. Rely on short-lived access tokens plus refresh-family revocation.

## Verified rationale

Authentication core phase specified RS256 and external key configuration. No-blacklist direction is an explicit project constraint in authentication/security prompts.

## Consequences

Revoked sessions may still accept access tokens until expiry. Hosts needing instant kill must shorten TTL or seek a new approved design.

## Alternatives considered

No historically verified alternatives recorded (e.g. HS256 or blacklist were not adopted; detailed comparison text not recovered).

## Source/evidence note

`RsaJwtAccessTokenGenerator`; AspNetCore JWT bearer extensions; security prompts.
