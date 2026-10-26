import { Component, effect, inject, signal, viewChild, ElementRef } from '@angular/core';
import { FormsModule } from '@angular/forms';
import {
  MaskedSettings,
  ProviderName,
  SettingsRequest,
  SettingsService,
} from '../core/settings.service';

type TestState = { status: 'idle' | 'testing' | 'ok' } | { status: 'failed'; error: string };

// Gear button + native <dialog>: provider, credentials, model discovery,
// connection test and save
@Component({
  selector: 'app-settings-dialog',
  imports: [FormsModule],
  templateUrl: './settings-dialog.html',
  styleUrl: './settings-dialog.css',
})
export class SettingsDialog {
  private readonly api = inject(SettingsService);
  private readonly dialog = viewChild.required<ElementRef<HTMLDialogElement>>('dlg');

  protected readonly keyPlaceholder = signal('sk-...');
  protected readonly activeLlm = signal('');
  // null = loading; [] = discovery failed, fall back to a text input
  protected readonly models = signal<string[] | null>(null);
  protected readonly modelsError = signal('');
  protected readonly test = signal<TestState>({ status: 'idle' });
  protected readonly saveError = signal('');
  protected readonly provider = signal<ProviderName>('openai');

  protected openaiApiKey = '';
  protected openaiBaseUrl = 'https://api.openai.com/v1';
  protected openaiModel = 'gpt-4o-mini';
  protected ollamaBaseUrl = 'http://localhost:11434';
  protected ollamaModel = '';

  constructor() {
    // Refresh the model list whenever the provider changes while open
    effect(() => {
      const p = this.provider();
      if (this.dialog().nativeElement.open) this.loadModels(p);
    });
  }

  protected get model(): string {
    return this.provider() === 'openai' ? this.openaiModel : this.ollamaModel;
  }
  protected set model(v: string) {
    if (this.provider() === 'openai') this.openaiModel = v;
    else this.ollamaModel = v;
  }

  protected async open() {
    this.test.set({ status: 'idle' });
    this.saveError.set('');
    this.dialog().nativeElement.showModal();
    try {
      const s = await this.api.get();
      this.apply(s);
      this.loadModels(s.provider);
    } catch {
      this.saveError.set('Could not load settings');
    }
  }

  protected close() {
    this.dialog().nativeElement.close();
  }

  private apply(s: MaskedSettings) {
    this.provider.set(s.provider);
    this.openaiApiKey = '';
    this.openaiBaseUrl = s.openai.baseUrl;
    this.openaiModel = s.openai.model;
    this.ollamaBaseUrl = s.ollama.baseUrl;
    this.ollamaModel = s.ollama.model;
    this.keyPlaceholder.set(
      s.openai.apiKeyConfigured
        ? `${s.openai.apiKeyDisplay} (saved, leave blank to keep)`
        : 'sk-...',
    );
    const active = s[s.provider].model;
    this.activeLlm.set(
      `${s.provider === 'openai' ? 'OpenAI' : 'Ollama'}${active ? ` · ${active}` : ''}`,
    );
  }

  private async loadModels(p: ProviderName) {
    this.models.set(null);
    this.modelsError.set('');
    try {
      this.models.set(await this.api.models(p));
    } catch (err) {
      this.models.set([]);
      this.modelsError.set(err instanceof Error ? err.message : 'Failed to load models');
    }
  }

  // An empty key is omitted: the server reads that as "keep the saved key"
  private body(): SettingsRequest {
    return {
      provider: this.provider(),
      openai: {
        ...(this.openaiApiKey ? { apiKey: this.openaiApiKey } : {}),
        baseUrl: this.openaiBaseUrl,
        model: this.openaiModel,
      },
      ollama: { baseUrl: this.ollamaBaseUrl, model: this.ollamaModel },
    };
  }

  protected async runTest() {
    this.test.set({ status: 'testing' });
    try {
      const r = await this.api.test(this.body());
      this.test.set(r.ok ? { status: 'ok' } : { status: 'failed', error: r.error ?? 'Failed' });
    } catch (err) {
      this.test.set({
        status: 'failed',
        error: err instanceof Error ? err.message : 'Server unreachable',
      });
    }
  }

  protected async save() {
    this.saveError.set('');
    try {
      await this.api.save(this.body());
      this.close();
    } catch {
      this.saveError.set('Failed to save settings');
    }
  }
}
