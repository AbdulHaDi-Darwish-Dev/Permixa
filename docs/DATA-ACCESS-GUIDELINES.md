# Data access guidelines

Human-readable form of `.cursor/rules/database-access.mdc` (machine-enforced, always apply).

## Default

Database access must be **set-based by default**.

## Forbidden

- Repository / EF Core / Identity persistence calls inside `foreach` / `for` / `while` over collections when a bulk query is reasonable
- `Task.WhenAll` over N per-item database queries as a substitute for bulk loading (still N queries)
- Lazy-loaded navigation access that triggers per-item queries
- Repeated `SaveChanges` / `SaveChangesAsync` inside a loop when one unit of work can commit all changes

## Preferred

- Collect and deduplicate ids, then one bulk query (`Where(ids.Contains(...))` / dedicated set methods)
- Dedicated set-based repository methods for use-case data shapes
- Projections when full entity graphs are unnecessary
- Stage all mutations, then a single `SaveChangesAsync`

## Acceptable

- Fixed O(1) sequential lookups in a single-item use case
- Persistence inside iteration only when bulk is genuinely inappropriate, with an **explicit written justification**

## Additional expectations

- Always inspect new database-access code for N+1 behavior
- Query count should generally remain bounded as processed items increase
- Do not over-optimize fixed O(1) query sequences merely to reduce query count

## Audit note

Default SQL audit sink stages entities and relies on ambient UoW/`SaveChanges` — do not add hidden per-call `SaveChanges` inside the sink without an approved design change.
