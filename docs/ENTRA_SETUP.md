# Entra API setup (local verification stage)

The API defaults to AUTH_MODE=Entra. Configure ENTRA_TENANT_ID as the tenant GUID and ENTRA_API_AUDIENCE as the API application client ID. It uses the tenant v2 authority, signed bearer tokens, issuer/audience/expiry validation, tenant and object ID claims, and delegated Billing.Access scope. Health is anonymous; API routes require authentication.

Register an API application in that tenant, configure v2 access tokens and expose the delegated Billing.Access scope. Register a separate SPA application, grant that delegated API permission and register http://localhost:5173/redirect.html as its SPA redirect URI (and the matching deployed URI later). React/MSAL integration is implemented; real browser sign-in is still pending; never paste tokens or client secrets into chat or source.

Project requestor/manager/partner/financeApprover values must be tenant object IDs in Entra mode. Current fixtures contain demo names, so real users will have no project access until those mappings are configured. Mutation Actor fields are overwritten by the validated oid claim; object IDs are scoped by the configured tenant. Applications using only app roles or client credentials are deliberately unsupported by the delegated policy.

Local demonstration: scripts/run-local.sh explicitly uses AUTH_MODE=LocalDemo. That mode uses synthetic actor strings and is suitable only for the existing Development/loopback restriction. Production startup remains disabled in either mode until Azure persistence and complete gates are implemented.

Verified: default-mode config failure, anonymous API rejection, compiled local workflow recovery and builds. Successful Entra sign-in, signed-token negative cases and browser login remain unverified. Anonymous rejection does not prove all JWT claim validation paths.

Reference: https://learn.microsoft.com/en-us/aspnet/core/security/authentication/configure-jwt-bearer-authentication?view=aspnetcore-10.0

## React configuration
Copy web/.env.example to web/.env.local and fill the SPA client ID, tenant ID and delegated API scope. No client secret is needed for the SPA. MSAL uses a dedicated redirect.html bridge entry, session storage, explicit account selection, silent access-token acquisition and bearer headers. The login screen waits for sign-in before loading projects; sign-out clears visible billing state. Popup cancellation/errors are shown without automatic retry. Tokens are not displayed or logged by application code.

For local synthetic demonstration only, run VITE_AUTH_MODE=LocalDemo npm run dev from web/ while scripts/run-local.sh runs the API. LocalDemo is refused by the production build and on non-loopback browser hosts. Entra mode remains the default. Real token and end-to-end sign-in verification requires the app registrations and project OID mapping.
