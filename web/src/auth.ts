import { PublicClientApplication, InteractionRequiredAuthError } from '@azure/msal-browser';

export const hostedDemo = import.meta.env.VITE_AUTH_MODE === 'HostedDemo';
export const localDemo = hostedDemo || import.meta.env.VITE_AUTH_MODE === 'LocalDemo';
const clientId = import.meta.env.VITE_ENTRA_CLIENT_ID;
const tenantId = import.meta.env.VITE_ENTRA_TENANT_ID;
const scope = import.meta.env.VITE_ENTRA_API_SCOPE;
export const configurationError = hostedDemo ? '' : localDemo
  ? (import.meta.env.DEV && ['localhost', '127.0.0.1', '[::1]'].includes(location.hostname) ? '' : 'Local demo requires the development server on localhost.')
  : (!clientId || !tenantId || !scope ? 'Microsoft sign-in has not been configured for this application.' : '');
const client = !localDemo && !configurationError ? new PublicClientApplication({
  auth: { clientId, authority: `https://login.microsoftonline.com/${tenantId}`, redirectUri: `${location.origin}/redirect.html` },
  cache: { cacheLocation: 'sessionStorage' }
}) : null;
let initialization: Promise<void> | undefined;
export async function initializeAuth() {
  if (!client) return;
  initialization ??= client.initialize();
  await initialization;
  // Restore only an explicitly selected account; never pick an arbitrary cached account.
}
export function signedInName() { return client?.getActiveAccount()?.name || client?.getActiveAccount()?.username || ''; }
export async function signIn() {
  if (!client) throw new Error(configurationError || 'Sign-in unavailable');
  await initializeAuth();
  const result = await client.loginPopup({ scopes: [scope], prompt: 'select_account' });
  client.setActiveAccount(result.account);
}
export async function signOut() {
  if (!client) return;
  await client.logoutPopup({ account: client.getActiveAccount(), postLogoutRedirectUri: location.origin });
  client.setActiveAccount(null);
}
export async function accessToken() {
  if (localDemo && !configurationError) return '';
  if (!client || configurationError) throw new Error(configurationError || 'Sign-in unavailable');
  await initializeAuth();
  const account = client.getActiveAccount();
  if (!account) throw new Error('Sign in to access the billing workspace.');
  try { return (await client.acquireTokenSilent({ scopes: [scope], account })).accessToken; }
  catch (error) {
    if (error instanceof InteractionRequiredAuthError) throw new Error('Your sign-in needs attention. Sign out and sign in again.');
    throw error;
  }
}
