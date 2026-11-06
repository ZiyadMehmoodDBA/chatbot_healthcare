import { Component, computed, effect, ElementRef, inject, signal, viewChild } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Assist, ChatService, ReportColumn, ReportResult } from '../core/chat.service';
import { SettingsDialog } from '../settings/settings-dialog';

interface Message {
  role: 'user' | 'bot';
  content: string;
  model?: string;
  durationMs?: number;
  reports?: ReportResult[];
  assist?: Assist;
  feedback?: 'up' | 'down' | 'asking';
}

// Tables that can be opened month by month, and the column holding each row's code. The server holds the real rule
// and the keys; this only decides where to show the button.
const DRILL_KEY: Record<string, string> = {
  'payments-by-payer': 'payerId',
  'payments-by-practice': 'practiceCode',
  'claim-volume': 'providerId',
  'claim-volume-by-practice': 'practiceCode',
  'charges-payments-by-practice': 'practiceCode',
  'denials-by-payer': 'payerId',
  'resubmissions-by-payer': 'payerId',
};

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
  // Latest progress line from a server-run investigation, shown beside the typing dots
  protected readonly step = signal('');
  protected draft = '';
  protected readonly reasons = [
    { key: 'wrong-figure', label: 'Wrong figure' },
    { key: 'not-what-i-asked', label: 'Not what I asked' },
    { key: 'unclear', label: 'Unclear' },
    { key: 'other', label: 'Other' },
  ];

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

  // Cells are formatted from the column type; values arrive as exact numbers or strings from the server
  protected cell(col: ReportColumn, v: string | number | null): string {
    if (v === null || v === undefined) return '—';
    const type = typeof col.type === 'number' ? ['Text', 'Integer', 'Money', 'Percent', 'Decimal'][col.type] : col.type;
    if (typeof v !== 'number') return String(v);
    if (type === 'Money') return v.toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
    if (type === 'Decimal') return v.toLocaleString('en-US', { minimumFractionDigits: 1, maximumFractionDigits: 1 });
    if (type === 'Integer') return v.toLocaleString('en-US', { maximumFractionDigits: 0 });
    if (type === 'Percent') return `${v.toLocaleString('en-US', { maximumFractionDigits: 2 })}%`;
    return String(v);
  }

  protected isNumeric(col: ReportColumn): boolean {
    return col.type !== 'Text' && col.type !== 0;
  }

  protected readonly exportError = signal('');

  // Only the newest table can be drilled: the server remembers the rows of the last report only
  private readonly latestReport = computed(() => {
    // Tables opened from a row do not count: their source table stays the one to drill
    const all = this.messages().flatMap((m) => m.reports ?? []).filter((r) => !r.evidence.filters['drill']);
    return all.length ? all[all.length - 1] : undefined;
  });

  protected canDrillTable(r: ReportResult): boolean {
    return r === this.latestReport() && !r.evidence.filters['comparison'] && r.evidence.reportId in DRILL_KEY;
  }

  protected canDrillRow(r: ReportResult, row: unknown[]): boolean {
    const at = r.columns.findIndex((c) => c.name === DRILL_KEY[r.evidence.reportId]);
    return at >= 0 && /^[0-9]+$/.test(String(row[at] ?? ''));
  }

  // Only the newest answer offers chips: an old suggestion would answer a question that is no longer open
  protected chipsOpen(m: Message): boolean {
    const last = this.messages().filter((x) => x.role === 'bot').at(-1);
    return m === last && !this.loading() && !this.streaming();
  }

  // A chip posts its text as the next message, exactly as if typed
  protected pick(text: string) {
    this.draft = text;
    void this.send();
  }

  // Thumbs under an answer. A thumbs-down asks for one fixed reason; nothing typed is sent.
  protected rate(index: number, rating: 'up' | 'down') {
    this.messages.update((ms) => ms.map((m, i) => (i === index ? { ...m, feedback: rating === 'down' ? 'asking' : 'up' } : m)));
    if (rating === 'up') void this.chat.feedback(this.conversationId, 'up').catch(() => undefined);
  }

  protected rateReason(index: number, reason: string) {
    this.messages.update((ms) => ms.map((m, i) => (i === index ? { ...m, feedback: 'down' } : m)));
    void this.chat.feedback(this.conversationId, 'down', reason).catch(() => undefined);
  }

  // A bar chart of the first measure (up to 12 rows), drawn only from the table's own cells. Plain CSS bars with the
  // value printed on each, so nothing is conveyed by colour alone.
  protected bars(r: ReportResult): { label: string; value: number; shown: string; pct: number }[] {
    const isMeasure = (c: ReportColumn, i: number) =>
      this.isNumeric(c) && !/(Prior|Change|ChangePct)$/.test(c.name) && c.type !== 'Percent' && i >= 0;
    const m = r.columns.findIndex((c, i) => isMeasure(c, i));
    const k = r.columns.findIndex((c) => !this.isNumeric(c));
    if (m < 0 || k < 0 || r.rows.length < 2) return [];
    const rows = r.rows.slice(0, 12).map((row) => ({ label: String(row[k] ?? ''), value: Number(row[m] ?? 0) }));
    const max = Math.max(...rows.map((x) => Math.abs(x.value)), 1);
    return rows.map((x) => ({ ...x, shown: this.cell(r.columns[m], x.value), pct: Math.round((Math.abs(x.value) / max) * 100) }));
  }

  protected barTitle(r: ReportResult): string {
    return r.columns.find((c, i) => this.isNumeric(c) && !/(Prior|Change|ChangePct)$/.test(c.name) && c.type !== 'Percent' && i >= 0)?.label ?? '';
  }

  protected drill(rowNumber: number) {
    this.draft = `drill into row ${rowNumber}`;
    void this.send();
  }

  protected async download(report: ReportResult) {
    this.exportError.set('');
    try {
      const blob = await this.chat.exportCsv(report.evidence);
      const url = URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = `${report.evidence.reportId}.csv`;
      a.click();
      URL.revokeObjectURL(url);
    } catch (err) {
      this.exportError.set(err instanceof Error ? err.message : 'Export failed.');
    }
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
    this.step.set('');
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
        if (ev.type === 'step') {
          this.step.set(ev.text);
          continue;
        }
        if (!bubbleShown) {
          this.messages.update((m) => [...m, { role: 'bot', content: '' }]);
          bubbleShown = true;
        }
        if (ev.type === 'delta') pending += ev.text;
        else if (ev.type === 'report') {
          this.loading.set(false);
          patchLast((m) => ({ ...m, reports: [...(m.reports ?? []), ev.report] }));
        }
        else if (ev.type === 'assist') patchLast((m) => ({ ...m, assist: ev.assist }));
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
      // Messages the API wrote for the user (sign in, no access, unavailable) are shown; network errors are not
      this.error.set(
        err instanceof Error && !(err instanceof TypeError) && err.message
          ? err.message
          : 'Something went wrong. Please try again.',
      );
    } finally {
      clearInterval(typer);
      this.loading.set(false);
      this.streaming.set(false);
      this.abort = null;
    }
  }
}
