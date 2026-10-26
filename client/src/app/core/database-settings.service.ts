import { Injectable } from '@angular/core';

export type DataMode = 'synthetic' | 'sql';
export type DbAuth = 'windows' | 'sql';

// GET /api/settings/database: the password never arrives, only whether one is saved
export interface MaskedDatabaseSettings {
  mode: DataMode;
  server: string;
  database: string;
  auth: DbAuth;
  user: string;
  passwordConfigured: boolean;
  encrypt: boolean;
  trustServerCertificate: boolean;
  patientSummaryView: string;
  facilityMetricsView: string;
  saved: boolean;
  active: string;
}

// Body for PUT /api/settings/database and POST /api/settings/database/test.
// Omitting password means "keep the saved one" (only while server and user are unchanged).
export interface DatabaseSettingsRequest {
  mode: DataMode;
  server: string;
  database: string;
  auth: DbAuth;
  user: string;
  password?: string;
  encrypt: boolean;
  trustServerCertificate: boolean;
  patientSummaryView: string;
  facilityMetricsView: string;
}

export interface ViewCheck {
  name: string;
  ok: boolean;
  error?: string | null;
}

export interface DatabaseTestResult {
  connected: boolean;
  error?: string | null;
  views: ViewCheck[];
  warnings: string[];
  ok: boolean;
}

// Validation errors arrive as { errors: { field: message } }; other failures as { error }
async function json<T>(res: Response): Promise<T> {
  const data = await res.json().catch(() => ({}));
  if (!res.ok) {
    const d = data as { error?: string; errors?: Record<string, string> };
    const detail = d.errors ? Object.values(d.errors).join(' ') : d.error;
    throw new Error(detail || `Request failed (${res.status})`);
  }
  return data as T;
}

@Injectable({ providedIn: 'root' })
export class DatabaseSettingsService {
  get(): Promise<MaskedDatabaseSettings> {
    return fetch('/api/settings/database').then((r) => json<MaskedDatabaseSettings>(r));
  }

  save(body: DatabaseSettingsRequest): Promise<MaskedDatabaseSettings> {
    return this.send<MaskedDatabaseSettings>('PUT', '/api/settings/database', body);
  }

  test(body: DatabaseSettingsRequest): Promise<DatabaseTestResult> {
    return this.send<DatabaseTestResult>('POST', '/api/settings/database/test', body);
  }

  private send<T>(method: string, url: string, body: DatabaseSettingsRequest): Promise<T> {
    return fetch(url, {
      method,
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body),
    }).then((r) => json<T>(r));
  }
}
