# 3. The gateway terminates authentication

Status: accepted

## Context
Clients sign in with Keycloak (OIDC, authorization code + PKCE for the browser). Every service validating JWTs would duplicate configuration and couple services to the identity provider.

## Decision
YARP is the only public entry point (`:8080`). It validates the JWT, applies a per-user rate limit, strips any client-supplied `X-Customer-Id` header and sets it from the `sub` claim. Orders reads that header and scopes every query to the customer; other users' orders return 404. Service ports are not published, so the header cannot be forged from outside the compose network.

Keycloak runs with a fixed public hostname (`http://localhost:8180`) so token issuers match what browsers see, while the gateway fetches metadata over the internal address.

## Consequences
- One place for authentication, CORS and rate limiting.
- Internal services trust the network boundary; mutual TLS or token propagation would be the next step beyond a local setup.
- Demo users: `demo` / `demo`, `alice` / `alice` and `admin` / `admin` (realm in `keycloak/orderflow-realm.json`).
- Admin routes (`/api/admin/**`) require the realm role `admin`, flattened into a `roles` claim by a protocol mapper. Customers get 403. The catalog read routes are public.
