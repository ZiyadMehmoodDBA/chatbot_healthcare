import { Injectable } from '@angular/core';

export type StreamEvent =
  | { type: 'delta'; text: string }
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
          done?: boolean;
          model?: string;
          durationMs?: number;
          error?: string;
        };
        if (line.delta) yield { type: 'delta', text: line.delta };
        else if (line.done) yield { type: 'done', model: line.model, durationMs: line.durationMs };
        else if (line.error) yield { type: 'error', message: line.error };
      }
    }
  }
}
