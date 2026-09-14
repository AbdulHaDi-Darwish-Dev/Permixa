# Phase 5 — Redis versioned authorization cache

## Goal

Disposable Redis (and memory) snapshot cache for effective permissions keyed with authorization versions.

## Decision

SQL remains source of truth; Redis failures → miss / best-effort no-op.

## Follow-up

Phase 6 Verification.
