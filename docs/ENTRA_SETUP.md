# Entra API setup (local verification stage)

The API defaults to AUTH_MODE=Entra. Configure ENTRA_TENANT_ID as the tenant GUID and ENTRA_API_AUDIENCE as the API application client ID. It uses the tenant v2 authority, signed bearer tokens, issuer/audience/expiry validation, tenant and object ID claims, and delegated Billing.Access scope. Health is anonymous; API routes require authentication.

Register an API application in that tenant, configure v2 access tokens and expose the delegated Billing.Access scope. Register a separate SPA application, grant that delegated API permission and configure its localhost redirect URI. SPA/MSAL integration is still pending; never paste tokens or client secrets into chat or source.

Project requestor/manager/partner/financeApprover values must be tenant object IDs in Entra mode. Current fixtures contain demo names, so real users will have no project access until those mappings are configured. Mutation Actor fields are overwritten by the validated oid claim; object IDs are scoped by the configured tenant. Applications using only app roles or client credentials are deliberately unsupported by the delegated policy.

Local demonstration: scripts/run-local.sh explicitly uses AUTH_MODE=LocalDemo. That mode uses synthetic actor strings and is suitable only for the existing Development/loopback restriction. Production startup remains disabled in either mode until Azure persistence and complete gates are implemented.

Verified: default-mode config failure, anonymous API rejection, compiled local workflow recovery and builds. Successful Entra sign-in, signed-token negative cases and browser login remain unverified. Anonymous rejection does not prove all JWT claim validation paths.

Reference: https://learn.microsoft.com/en-us/aspnet/core/security/authentication/configure-jwt-bearer-authentication?view=aspnetcore-10.0
