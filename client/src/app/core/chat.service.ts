import { Injectable } from '@angular/core';

export interface ReportColumn {
  name: string;
  label: string;
  type: 'Text' | 'Integer' | 'Money' | 'Percent' | number;
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

export type StreamEvent =
  | { type: 'delta'; text: string }
  | { type: 'report'; report: ReportResult }
  | { type: 'done'; model?: string; durationMs?: number }
  | { type: 'error'; message: string };

// Talks to POST /api/chat and parses the NDJSON stream
// ({"delta"} lines, then {"done"}, or {"error"} mid-stream)
@Injectable({ providedIn: 'root' })
export class ChatService {
  async *stream(
    prompt: string,
    conversationId: string,
    signal: AbortSignal,
  ): AsyncGenerator<StreamEvent> {
    const res = await fetch('/api/chat', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ prompt, conversationId }),
      signal,
    });
    if (!res.ok || !res.body) {
      throw new Error(`Request failed with status ${res.status}`);
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
          delta?: string;
          report?: ReportResult;
          done?: boolean;
          model?: string;
          durationMs?: number;
          error?: string;
        };
        if (line.delta) yield { type: 'delta', text: line.delta };
        else if (line.report) yield { type: 'report', report: line.report };
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
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body),
    });
    if (!res.ok) {
      const err = (await res.json().catch(() => null)) as { error?: string } | null;
      throw new Error(err?.error ?? `Export failed with status ${res.status}`);
    }
    return res.blob();
  }
}
