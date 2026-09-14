# Phase 1.1 — ASP.NET Core Identity alignment

## Goal

Correct architecture: build on ASP.NET Core Identity instead of Domain User/Role.

## Important decisions

**Verified historical decision:** Remove Domain User/Role/UserRole; Identity will own those primitives. VerificationChallenge loses Domain SecretHash (orchestration only).

## Implemented

Domain cleanup; Permission-focused validation tests.

## Excluded

Infrastructure Identity implementation (later Phase 3).

## Follow-up

Application core.
