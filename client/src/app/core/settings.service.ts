import { Injectable } from '@angular/core';

export type ProviderName = 'openai' | 'ollama';

// GET /api/settings: the API key only arrives masked
export interface MaskedSettings {
  provider: ProviderName;
  openai: { apiKeyConfigured: boolean; apiKeyDisplay: string; baseUrl: string; model: string };
  ollama: { baseUrl: string; model: string };
}

// Body for PUT /api/settings and POST /api/settings/test.
// Omitting openai.apiKey means "keep the saved key".
export interface SettingsRequest {
  provider: ProviderName;
  openai: { apiKey?: string; baseUrl: string; model: string };
  ollama: { baseUrl: string; model: string };
}

export interface ConnectionResult {
  ok: boolean;
  error?: string;
}

async function json<T>(res: Response): Promise<T> {
  const data = await res.json().catch(() => ({}));
  if (!res.ok) {
    throw new Error((data as { error?: string }).error ?? `Request failed (${res.status})`);
  }
  return data as T;
}

@Injectable({ providedIn: 'root' })
export class SettingsService {
  get(): Promise<MaskedSettings> {
    return fetch('/api/settings').then((r) => json<MaskedSettings>(r));
  }

  async models(provider: ProviderName): Promise<string[]> {
    const res = await fetch(`/api/settings/models?provider=${provider}`);
    return (await json<{ models: string[] }>(res)).models;
  }

  save(body: SettingsRequest): Promise<MaskedSettings> {
    return this.send<MaskedSettings>('PUT', '/api/settings', body);
  }

  test(body: SettingsRequest): Promise<ConnectionResult> {
    return this.send<ConnectionResult>('POST', '/api/settings/test', body);
  }

  private send<T>(method: string, url: string, body: SettingsRequest): Promise<T> {
    return fetch(url, {
      method,
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body),
    }).then((r) => json<T>(r));
  }
}
