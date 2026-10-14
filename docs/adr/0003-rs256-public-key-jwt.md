# ADR 0003: Validate JWTs against a local RSA public key

## Status
Accepted

## Context
The API needs to authenticate callers but never issues tokens: there is no login endpoint. Something
else issues them in a real deployment. The sibling implementations moved from a shared HS256 secret to an
RS256 public key after a review flagged the shared secret as the most severe finding: a leaked secret lets
an attacker mint tokens, not just verify them.

## Decision
`Jwt:PublicKeyPath` points at the PEM public key of the issuer's key pair, loaded once at startup with
`RSA.ImportFromPem`. `TokenValidationParameters` accepts only `RS256` and validates issuer
(`Jwt:Issuer`), audience (`Jwt:Audience`), lifetime and signing key, with a 30 second clock skew. A missing
key path fails the startup in every environment, so there is no insecure default to guard against.

Integration tests generate a throwaway RSA key pair per test run, write the public half to a temporary
file and sign real tokens with the private half. No key is committed, and the tests exercise the real
validation (wrong audience and a token signed by another key both return 401).

## Alternatives considered
- **Shared HS256 secret:** simpler, but a leak grants the ability to forge tokens.
- **Full OIDC discovery against Keycloak:** demonstrates JWKS rotation and realm provisioning, but adds a
  heavy container and operational weight without showing anything new about this stack.

## Consequences
- Positive: leaking the key file grants nothing beyond what a public key allows.
- Positive: zero external identity dependency to start the app or run the test suite.
- Negative accepted: no live demonstration of OIDC discovery or key rotation. Rotating the key means
  redeploying with the new file.
