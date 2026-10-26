import { Component, effect, ElementRef, inject, signal, viewChild } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ChatService } from '../core/chat.service';
import { SettingsDialog } from '../settings/settings-dialog';

interface Message {
  role: 'user' | 'bot';
  content: string;
  model?: string;
  durationMs?: number;
}

const sleep = (ms: number) => new Promise<void>((r) => setTimeout(r, ms));

// Top-level chat widget: owns the transcript, loading/error state and
// the conversation id the server keys history on
@Component({
  selector: 'app-chat',
  imports: [DecimalPipe, FormsModule, SettingsDialog],
  templateUrl: './chat.html',
  styleUrl: './chat.css',
})
export class Chat {
  private readonly chat = inject(ChatService);

  protected readonly messages = signal<Message[]>([]);
  protected readonly loading = signal(false);
  // True from request start until the stream ends or is stopped
  protected readonly streaming = signal(false);
  protected readonly error = signal('');
  protected draft = '';

  // One id per page load
  private readonly conversationId = crypto.randomUUID();
  private abort: AbortController | null = null;
  private readonly bottom = viewChild<ElementRef<HTMLElement>>('bottom');

  constructor() {
    // Keep the newest message in view
    effect(() => {
      this.messages();
      this.loading();
      queueMicrotask(() => this.bottom()?.nativeElement.scrollIntoView({ behavior: 'smooth' }));
    });
  }

  protected stop() {
    this.abort?.abort();
  }

  protected onKey(e: KeyboardEvent) {
    if (e.key === 'Enter' && !e.shiftKey) {
      e.preventDefault();
      this.send();
    }
  }

  protected async send() {
    const prompt = this.draft.trim();
    if (!prompt || this.loading() || this.streaming()) return;
    this.draft = '';

    this.messages.update((m) => [...m, { role: 'user', content: prompt }]);
    this.error.set('');
    this.loading.set(true);
    this.streaming.set(true);
    const controller = new AbortController();
    this.abort = controller;

    const patchLast = (fn: (m: Message) => Message) =>
      this.messages.update((all) => all.map((m, i) => (i === all.length - 1 ? fn(m) : m)));

    // Typewriter smoothing: network deltas land in `pending`; a ticker moves
    // a few chars at a time into the bubble so big chunks still look word-by-word
    let pending = '';
    let bubbleShown = false;
    const typer = setInterval(() => {
      if (!pending) return;
      const count = Math.max(2, Math.ceil(pending.length / 15));
      const chunk = pending.slice(0, count);
      pending = pending.slice(count);
      this.loading.set(false);
      patchLast((m) => ({ ...m, content: m.content + chunk }));
    }, 30);
    // Instant drain for Stop/error paths: partial text must stay
    const flush = () => {
      if (!pending || !bubbleShown) return;
      const rest = pending;
      pending = '';
      this.loading.set(false);
      patchLast((m) => ({ ...m, content: m.content + rest }));
    };

    try {
      let info: { model?: string; durationMs?: number } | null = null;
      for await (const ev of this.chat.stream(prompt, this.conversationId, controller.signal)) {
        if (!bubbleShown) {
          this.messages.update((m) => [...m, { role: 'bot', content: '' }]);
          bubbleShown = true;
        }
        if (ev.type === 'delta') pending += ev.text;
        else if (ev.type === 'done') info = { model: ev.model, durationMs: ev.durationMs };
        else this.error.set(ev.message); // mid-stream failure: keep partial text
      }
      // Let the typer finish; Stop during the drain flushes instantly
      while (pending && !controller.signal.aborted) await sleep(30);
      flush();
      if (info) {
        const done = info;
        patchLast((m) => ({ ...m, ...done }));
      }
    } catch (err) {
      flush();
      if (err instanceof DOMException && err.name === 'AbortError') return;
      console.error('Chat request failed:', err);
      this.error.set('Something went wrong. Please try again.');
    } finally {
      clearInterval(typer);
      this.loading.set(false);
      this.streaming.set(false);
      this.abort = null;
    }
  }
}
