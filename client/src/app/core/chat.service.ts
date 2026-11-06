import { Injectable, inject } from '@angular/core';
import { AuthService } from './auth.service';

export interface ReportColumn {
  name: string;
  label: string;
  type: 'Text' | 'Integer' | 'Money' | 'Percent' | 'Decimal' | number;
}

export interface ReportEvidence {
  reportId: string;
  metricVersion: string;
  approvalStatus: string;
  source: string;
  dateBasis: string;
  period: string;
  filters: Record<string, string>;
  rowCount: number;
  truncated: boolean;
  generatedUtc: string;
  caveats: string[];
  dataThrough?: string | null;
}

// A table the server built from the report service; the model never touches these numbers
export interface ReportResult {
  columns: ReportColumn[];
  rows: (string | number | null)[][];
  evidence: ReportEvidence;
}

// Built by the server from the report evidence: a scope line, a note for empty results, and click-to-send replies
export interface Assist {
  scope?: string | null;
  note?: string | null;
  chips: { label: string; send: string }[];
}

export type StreamEvent =
  | { type: 'delta'; text: string }
  | { type: 'step'; text: string }
  | { type: 'assist'; assist: Assist }
  | { type: 'report'; report: ReportResult }
  | { type: 'done'; model?: string; durationMs?: number }
  | { type: 'error'; message: string };

// Talks to POST /api/chat and parses the NDJSON stream
// ({"delta"} lines, then {"done"}, or {"error"} mid-stream)
@Injectable({ providedIn: 'root' })
export class ChatService {
  private readonly auth = inject(AuthService);

  async feedback(conversationId: string, rating: 'up' | 'down', reason?: string): Promise<void> {
    const res = await fetch('/api/feedback', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', ...(await this.auth.headers()) },
      body: JSON.stringify({ conversationId, rating, reason }),
    });
    if (!res.ok) throw new Error(`Feedback failed with status ${res.status}`);
  }

  async *stream(
    prompt: string,
    conversationId: string,
    signal: AbortSignal,
  ): AsyncGenerator<StreamEvent> {
    const res = await fetch('/api/chat', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', ...(await this.auth.headers()) },
      body: JSON.stringify({ prompt, conversationId }),
      signal,
    });
    if (!res.ok || !res.body) {
      // 401/403/503 carry a user-facing message from the API
      const err = (await res.json().catch(() => null)) as { error?: string } | null;
      throw new Error(err?.error ?? `Request failed with status ${res.status}`);
    }

    const reader = res.body.getReader();
    const decoder = new TextDecoder();
    let buffer = '';
    while (true) {
      const { done, value } = await reader.read();
      if (done) break;
      buffer += decoder.decode(value, { stream: true });
      const lines = buffer.split('\n');
      buffer = lines.pop() ?? '';
      for (const raw of lines) {
        if (!raw.trim()) continue;
        const line = JSON.parse(raw) as {
          step?: string;
          delta?: string;
          report?: ReportResult;
          assist?: Assist;
          done?: boolean;
          model?: string;
          durationMs?: number;
          error?: string;
        };
        if (line.step) yield { type: 'step', text: line.step };
        else if (line.delta) yield { type: 'delta', text: line.delta };
        else if (line.report) yield { type: 'report', report: line.report };
        else if (line.assist) yield { type: 'assist', assist: line.assist };
        else if (line.done) yield { type: 'done', model: line.model, durationMs: line.durationMs };
        else if (line.error) yield { type: 'error', message: line.error };
      }
    }
  }

  // Re-runs the report on the server (permissions are checked again) and returns the CSV file
  async exportCsv(evidence: ReportEvidence): Promise<Blob> {
    const f = evidence.filters;
    const body = {
      startDate: f['startDate'],
      endDate: f['endDate'],
      asOfDate: f['asOfDate'],
      topN: f['topN'] ? Number(f['topN']) : undefined,
    };
    const res = await fetch(`/api/reports/${encodeURIComponent(evidence.reportId)}/export`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', ...(await this.auth.headers()) },
      body: JSON.stringify(body),
    });
    if (!res.ok) {
      const err = (await res.json().catch(() => null)) as { error?: string } | null;
      throw new Error(err?.error ?? `Export failed with status ${res.status}`);
    }
    return res.blob();
  }
}
