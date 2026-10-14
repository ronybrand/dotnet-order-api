# ADR 0001: Binary RBAC (authenticated user vs admin), not per-resource permissions

## Status
Accepted

## Context
`CustomersController` and `OrdersController` need to tell apart who can read data from who can mutate
customers. The domain (see `DOMAIN.md`) has no concept of ownership or tenancy: it is an internal
order-management API, not a customer-facing self-service one where a caller would be restricted to
their own records.

## Decision
Two roles only, read from the `roles` claim of the JWT (`JwtAuthenticationExtensions`,
`RoleClaimType = "roles"`). Any authenticated caller (`Roles.User`) can call every read route and every
`/orders` route; `Roles.Admin` is additionally required for every mutating `/customers` route, through
`[Authorize(Roles = Roles.Admin)]`. Role names are constants in `Roles`, never inline strings.
`ControllerAuthorizationTests` locks this in by reflection: every controller declares `[Authorize]`, no
action is `[AllowAnonymous]`, and customer mutations require admin.

## Alternatives considered
- **Ownership-based authorization:** does not map to the domain. There is no notion of a customer being
  the same principal as an API caller managing their own order.
- **A single role (everyone authenticated can do everything):** simpler, but gives up the read-vs-write
  authorization boundary, which is exactly the access-control granularity worth demonstrating.
- **Fine-grained permissions (`orders:write`, `customers:delete`):** proportional to a multi-team API
  with many kinds of caller. This API has two, and two roles already capture that distinction.

## Consequences
- Positive: authorization lives in attributes on the controllers and is easy to audit with a grep for
  `Roles.`.
- Negative accepted: a third kind of caller (for example a read-only auditor) means touching every
  mutating action one by one. There is no role hierarchy.
