# Architecture Decision Records

Log of the non-obvious technical decisions made in this project: context, what was decided, alternatives
discarded and consequences (including the ones left deliberately unmitigated).

- [0001: Binary RBAC (authenticated user vs admin), not per-resource permissions](0001-binary-rbac-admin-user.md)
- [0002: Best-effort publish after commit, not a transactional outbox](0002-no-transactional-outbox.md)
- [0003: Validate JWTs against a local RSA public key](0003-rs256-public-key-jwt.md)
- [0004: Optimistic concurrency with Postgres xmin, soft delete with filtered unique indexes](0004-xmin-concurrency-and-filtered-unique-indexes.md)
