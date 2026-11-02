import { Injectable, signal } from '@angular/core';
import {
  InteractionRequiredAuthError,
  PublicClientApplication,
  type AccountInfo,
} from '@azure/msal-browser';

// Public sign-in settings, served as /auth-config.json (no secrets: a SPA is a public client).
// Missing file or enabled=false = no sign-in (local dev against the API's Dev mode).
interface AuthConfig {
  enabled: boolean;
  tenantId: string;
  clientId: string;
  scope: string; // e.g. api://<api-client-id>/access_as_user
}

// Azure AD sign-in with MSAL (authorization code + PKCE). Tokens stay in MSAL's sessionStorage cache;
// the app only ever asks for a fresh access token per request.
@Injectable({ providedIn: 'root' })
export class AuthService {
  private msal?: PublicClientApplication;
  private config?: AuthConfig;

  readonly ready = signal(false);
  readonly enabled = signal(false);
  readonly account = signal<AccountInfo | null>(null);
  readonly error = signal('');

  async init(): Promise<void> {
    try {
      const res = await fetch('/auth-config.json', { cache: 'no-store' });
      const cfg = res.ok ? ((await res.json().catch(() => null)) as AuthConfig | null) : null;
      if (cfg?.enabled && cfg.tenantId && cfg.clientId && cfg.scope) {
        this.config = cfg;
        this.msal = new PublicClientApplication({
          auth: {
            clientId: cfg.clientId,
            authority: `https://login.microsoftonline.com/${cfg.tenantId}`,
            redirectUri: window.location.origin,
          },
          cache: { cacheLocation: 'sessionStorage' },
        });
        await this.msal.initialize();
        const result = await this.msal.handleRedirectPromise();
        const account = result?.account ?? this.msal.getAllAccounts()[0] ?? null;
        if (account) this.msal.setActiveAccount(account);
        this.account.set(account);
        this.enabled.set(true);
      }
    } catch (err) {
      console.error('Sign-in setup failed:', err);
      this.error.set('Sign-in could not start. Please reload the page.');
      this.enabled.set(true); // fail closed: show the sign-in gate, not the app
    } finally {
      this.ready.set(true);
    }
  }

  get signedIn(): boolean {
    return !this.enabled() || this.account() !== null;
  }

  signIn(): Promise<void> {
    return this.msal!.loginRedirect({ scopes: [this.config!.scope] });
  }

  async signOut(): Promise<void> {
    await this.msal?.logoutRedirect({ account: this.account() ?? undefined });
  }

  // Authorization header for API calls; empty when sign-in is off. A silent refresh failure sends the user to sign in.
  async headers(): Promise<Record<string, string>> {
    if (!this.enabled() || !this.msal) return {};
    const account = this.account();
    if (!account) throw new Error('Please sign in.');
    try {
      const r = await this.msal.acquireTokenSilent({ scopes: [this.config!.scope], account });
      return { Authorization: `Bearer ${r.accessToken}` };
    } catch (err) {
      if (err instanceof InteractionRequiredAuthError) {
        await this.msal.acquireTokenRedirect({ scopes: [this.config!.scope], account });
      }
      throw new Error('Please sign in again.');
    }
  }
}
