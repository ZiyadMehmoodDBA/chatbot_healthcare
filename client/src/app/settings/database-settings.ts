import { Component, OnInit, computed, inject, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import {
  DataMode,
  DatabaseSettingsRequest,
  DatabaseSettingsService,
  DatabaseTestResult,
  DbAuth,
  MaskedDatabaseSettings,
} from '../core/database-settings.service';

type TestState =
  | { status: 'idle' | 'testing' }
  | { status: 'done'; result: DatabaseTestResult }
  | { status: 'failed'; error: string };

// Database tab of the settings dialog: choose demo data or SQL Server, test the connection, save.
@Component({
  selector: 'app-database-settings',
  imports: [FormsModule],
  templateUrl: './database-settings.html',
  styleUrl: './database-settings.css',
})
export class DatabaseSettings implements OnInit {
  private readonly api = inject(DatabaseSettingsService);

  readonly closed = output<void>();

  protected readonly loading = signal(true);
  protected readonly active = signal('');
  protected readonly mode = signal<DataMode>('synthetic');
  protected readonly server = signal('');
  protected readonly database = signal('');
  protected readonly auth = signal<DbAuth>('windows');
  protected readonly user = signal('');
  protected readonly password = signal('');
  protected readonly passwordSaved = signal(false);
  protected readonly encrypt = signal(true);
  protected readonly trustCert = signal(false);
  protected readonly patientView = signal('reporting.vw_PatientSummary');
  protected readonly facilityView = signal('reporting.vw_FacilityMetrics');
  protected readonly test = signal<TestState>({ status: 'idle' });
  protected readonly message = signal<{ ok: boolean; text: string } | null>(null);
  protected readonly busy = signal(false);

  protected readonly passwordPlaceholder = computed(() =>
    this.passwordSaved() ? '•••• (saved, leave blank to keep)' : 'password',
  );

  ngOnInit() {
    void this.load();
  }

  private async load() {
    try {
      this.apply(await this.api.get());
    } catch (err) {
      this.message.set({ ok: false, text: err instanceof Error ? err.message : 'Could not load settings' });
    } finally {
      this.loading.set(false);
    }
  }

  private apply(s: MaskedDatabaseSettings) {
    this.active.set(s.active);
    this.mode.set(s.mode);
    this.server.set(s.server);
    this.database.set(s.database);
    this.auth.set(s.auth);
    this.user.set(s.user);
    this.password.set('');
    this.passwordSaved.set(s.passwordConfigured);
    this.encrypt.set(s.encrypt);
    this.trustCert.set(s.trustServerCertificate);
    this.patientView.set(s.patientSummaryView);
    this.facilityView.set(s.facilityMetricsView);
  }

  // An empty password is omitted: the server reads that as "keep the saved password"
  private body(): DatabaseSettingsRequest {
    return {
      mode: this.mode(),
      server: this.server(),
      database: this.database(),
      auth: this.auth(),
      user: this.auth() === 'sql' ? this.user() : '',
      ...(this.auth() === 'sql' && this.password() ? { password: this.password() } : {}),
      encrypt: this.encrypt(),
      trustServerCertificate: this.trustCert(),
      patientSummaryView: this.patientView(),
      facilityMetricsView: this.facilityView(),
    };
  }

  protected changed() {
    // Any edit makes the last test result stale
    this.test.set({ status: 'idle' });
    this.message.set(null);
  }

  protected async runTest() {
    this.message.set(null);
    this.test.set({ status: 'testing' });
    try {
      this.test.set({ status: 'done', result: await this.api.test(this.body()) });
    } catch (err) {
      this.test.set({ status: 'failed', error: err instanceof Error ? err.message : 'Server unreachable' });
    }
  }

  protected async save() {
    this.message.set(null);
    this.busy.set(true);
    try {
      const saved = await this.api.save(this.body());
      this.apply(saved);
      this.message.set({ ok: true, text: `Saved. Active: ${saved.active}` });
    } catch (err) {
      this.message.set({ ok: false, text: err instanceof Error ? err.message : 'Failed to save' });
    } finally {
      this.busy.set(false);
    }
  }
}
