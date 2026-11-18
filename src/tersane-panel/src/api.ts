// Read-only JSON endpoints of Tersane.Web (ApiController). Actions such as "Yeniden işle" stay in the MVC app,
// where they are protected by anti-forgery tokens.

export interface Health {
  pendingCount: number;
  processingCount: number;
  failedCount: number;
  processedLast24h: number;
  oldestPendingAgeSeconds: number | null;
  errorsLastHour: number;
  lastErrorAt: string | null;
  lastStockSyncAt: string | null;
}

export interface SyncState {
  name: string;
  watermark: string;
  lastRunAt: string | null;
  lastSuccessAt: string | null;
  lastError: string | null;
  lastRowCount: number;
}

export interface MinuteBucket {
  hour: number;
  minute: number;
  ok: number;
  error: number;
  avgMs: number | null;
}

export interface Summary {
  health: Health;
  sync: SyncState[];
  perMinute: MinuteBucket[];
  serverTime: string;
}

export type OutboxStatus = 'Pending' | 'Processing' | 'Processed' | 'Failed';

export interface OutboxMessage {
  messageId: string;
  messageType: string;
  idempotencyKey: string;
  status: OutboxStatus;
  attemptCount: number;
  maxAttempts: number;
  nextAttemptAt: string;
  lastError: string | null;
  externalRef: string | null;
  createdAt: string;
  processedAt: string | null;
}

const base = import.meta.env.VITE_API_BASE ?? '';

async function getJson<T>(path: string): Promise<T> {
  const response = await fetch(base + path, { headers: { Accept: 'application/json' } });
  if (!response.ok) throw new Error(`${path} → HTTP ${response.status}`);
  return (await response.json()) as T;
}

export const fetchSummary = () => getJson<Summary>('/api/entegrasyon/ozet');

export const fetchMessages = (status: OutboxStatus | '') =>
  getJson<OutboxMessage[]>(`/api/entegrasyon/mesajlar${status ? `?status=${status}` : ''}`);

/** The API returns UTC timestamps without a zone suffix (SQL Server datetime2). */
export function utc(value: string | null): Date | null {
  if (!value) return null;
  return new Date(/[zZ]|[+-]\d\d:\d\d$/.test(value) ? value : value + 'Z');
}
