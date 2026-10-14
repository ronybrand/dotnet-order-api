# Security Policy

## Supported versions

This is a single-branch portfolio project (no maintained release lines): security fixes land on `main`
only. There is no LTS or backport policy.

## Reporting a vulnerability

Please **do not** open a public GitHub issue for a suspected vulnerability. Use
[GitHub's private vulnerability reporting](../../security/advisories/new) for this repository (Security
tab, "Report a vulnerability"), or contact the maintainer through the email on the
[GitHub profile](https://github.com/ronybrand).

Include, where applicable:

- A description of the vulnerability and its potential impact.
- Steps to reproduce (a minimal request or payload is ideal).
- The affected endpoint(s) or component(s).

This project is maintained on a best-effort basis (no SLA), but reports are taken seriously and triaged
as soon as possible.

## Scope

In scope: the application code in `src/`, the `OrderApi.Worker` notification process, and the Dockerfile
and CI workflows that build and ship them.

Out of scope: the third-party services this project integrates with when self-hosted for local
development (PostgreSQL, RabbitMQ, Mailpit). Report issues in those projects upstream. Dependency
vulnerabilities are tracked automatically through Dependabot and CodeQL rather than manual reports.

## What this project already does

- Automated dependency updates through Dependabot, auto-merged after CI passes (major bumps are always
  reviewed by hand).
- Static analysis on every push and pull request: CodeQL, plus the .NET analyzers running as errors and
  `dotnet format --verify-no-changes` in CI. NuGet vulnerability warnings fail the build.
- No secret is committed. Configuration comes from environment variables, with local-development-only
  defaults backed by `docker-compose.yml`. The JWT verification key is a public key.
- Security headers, rate limiting, a request body limit, JWT audience/issuer/algorithm validation and a
  production CORS fail-fast guard are always on, and are covered by integration tests.
- PII fields are marked `[Sensitive]` and never appear in responses or logs.
