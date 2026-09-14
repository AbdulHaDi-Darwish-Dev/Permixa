# Phase C — User administration + account state

## Goal

Admin create user; lock/unlock; disable/enable with hierarchy and Owner protections.

## Important decisions

`IsDisabled` distinct from lockout; refresh must re-read target user for disabled/locked rejection as specified then.

## Migration

`20260913200000_AddUserIsDisabled`

## Follow-up

Phase D — Sessions.
