# ADR 0004: Optimistic concurrency with Postgres xmin, soft delete with filtered unique indexes

## Status
Accepted

## Context
Two concurrent writes to the same order must not silently overwrite each other, and a customer's tax id
must stay unique among active customers while a soft-deleted customer must release its values.

## Decision
- `Order.Version` is mapped to the Postgres system column `xmin` as the EF Core concurrency token. Item
  mutations also refresh the order's audit columns (`SaveItemChangeAsync`), so a change that only touches
  children still issues an `UPDATE` of the order row and is covered by the token. A stale write raises
  `DbUpdateConcurrencyException`, which `GlobalExceptionHandlerMiddleware` maps to `409 CONFLICT-00`.
- `ISoftDeletable` entities get a global EF query filter applied by reflection, so no read query can
  forget `deleted_at IS NULL`.
- The unique indexes on `tax_id` and `passport_number` are filtered by `deleted_at IS NULL` (and
  `passport_number IS NOT NULL`), matching the service-level checks that only consider active records.
  A unique violation (`23505`) that slips past the service check under concurrency is translated to
  `CONFLICT-01` or `CONFLICT-02` in the repository by constraint name.

## Alternatives considered
- **Integer version column:** works everywhere, but needs an explicit increment and an extra column
  already provided by the database for free.
- **Unfiltered unique indexes:** the service would allow recreating a deleted customer's tax id while the
  database rejected it, turning a valid request into a 500.
- **Pessimistic locking (`SELECT ... FOR UPDATE`):** serializes writes, which this domain does not need.
  The conflict rate on a single order is low, and a clear 409 lets the client retry.

## Consequences
- Positive: correct under concurrency, covered by integration tests (parallel duplicate creates and
  parallel item additions).
- Negative accepted: `xmin` is Postgres specific, and EF generates a real `xmin` column in `CreateTable`
  that must be removed from the migration by hand (a Postgres system column cannot be created).
- A soft-deleted customer's tax id can be reused by a new customer, so history can be ambiguous about
  which record a historical value belongs to.
