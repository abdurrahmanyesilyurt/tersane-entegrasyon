import { useMemo, useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { fetchMessages, fetchSummary, utc, type MinuteBucket, type OutboxStatus, type SyncState } from './api';

const statusLabels: Record<OutboxStatus, string> = {
  Pending: 'Bekliyor',
  Processing: 'İşleniyor',
  Processed: 'İşlendi',
  Failed: 'Hatalı',
};

const feedLabels: Record<string, string> = {
  ErpParts: 'Malzeme kartları',
  ErpStock: 'Stok',
  ErpRequisitionStates: 'Talep durumları',
};

const time = (value: string | null) => utc(value)?.toLocaleTimeString('tr-TR') ?? '—';

function ago(value: string | null): string {
  const date = utc(value);
  if (!date) return 'hiç';
  const seconds = Math.max(0, Math.round((Date.now() - date.getTime()) / 1000));
  if (seconds < 60) return `${seconds} sn önce`;
  if (seconds < 3600) return `${Math.floor(seconds / 60)} dk önce`;
  return `${Math.floor(seconds / 3600)} sa önce`;
}

export function App() {
  const [status, setStatus] = useState<OutboxStatus | ''>('');
  const summary = useQuery({ queryKey: ['summary'], queryFn: fetchSummary });
  const messages = useQuery({ queryKey: ['messages', status], queryFn: () => fetchMessages(status) });
  const health = summary.data?.health;

  return (
    <div className="page">
      <header className="top">
        <div>
          <h1>Entegrasyon Paneli</h1>
          <p className="muted">İç sistem ↔ sahte (mock) ERP · 5 sn'de bir yenilenir</p>
        </div>
        <span className={`live ${summary.isError ? 'is-down' : ''}`}>
          {summary.isError ? 'API erişilemiyor' : summary.isFetching ? 'güncelleniyor…' : 'canlı'}
        </span>
      </header>

      {summary.isError && <div className="alert">Tersane.Web API'sine ulaşılamadı: {String(summary.error)}</div>}

      <section className="kpis">
        <Kpi label="Kuyrukta" value={health ? health.pendingCount + health.processingCount : '…'} />
        <Kpi label="Hatalı" value={health?.failedCount ?? '…'} tone={health && health.failedCount > 0 ? 'bad' : 'good'}
             hint="MVC ekranından yeniden işlenir" />
        <Kpi label="Son 24 sa işlenen" value={health?.processedLast24h ?? '…'} tone="good" />
        <Kpi label="Son 1 sa hata" value={health?.errorsLastHour ?? '…'} tone={health && health.errorsLastHour > 0 ? 'warn' : undefined} />
        <Kpi label="En eski bekleyen" value={health?.oldestPendingAgeSeconds != null ? `${health.oldestPendingAgeSeconds} sn` : '—'} />
      </section>

      <section className="grid">
        <div className="card">
          <h2>Son 60 dakika: ERP çağrıları</h2>
          <TrafficChart buckets={summary.data?.perMinute ?? []} now={utc(summary.data?.serverTime ?? null) ?? new Date()} />
        </div>
        <div className="card">
          <h2>Delta senkron</h2>
          <SyncTable states={summary.data?.sync ?? []} />
        </div>
      </section>

      <section className="card">
        <div className="card-head">
          <h2>Outbox mesajları</h2>
          <select value={status} onChange={(e) => setStatus(e.target.value as OutboxStatus | '')} aria-label="Durum filtresi">
            <option value="">Tüm durumlar</option>
            {(Object.keys(statusLabels) as OutboxStatus[]).map((s) => (
              <option key={s} value={s}>{statusLabels[s]}</option>
            ))}
          </select>
        </div>
        <div className="table-wrap">
          <table>
            <thead>
              <tr><th>Oluşma</th><th>İdempotency anahtarı</th><th>Durum</th><th className="num">Deneme</th><th>ERP no</th><th>Son hata</th></tr>
            </thead>
            <tbody>
              {(messages.data ?? []).map((m) => (
                <tr key={m.messageId} className={m.status === 'Failed' ? 'row-bad' : undefined}>
                  <td className="mono">{time(m.createdAt)}</td>
                  <td className="mono">{m.idempotencyKey}</td>
                  <td><span className={`pill s-${m.status}`}>{statusLabels[m.status]}</span></td>
                  <td className="num">{m.attemptCount} / {m.maxAttempts}</td>
                  <td className="mono">{m.externalRef ?? '—'}</td>
                  <td className="err">{m.lastError}</td>
                </tr>
              ))}
              {messages.data?.length === 0 && <tr><td colSpan={6} className="empty">Mesaj yok.</td></tr>}
            </tbody>
          </table>
        </div>
      </section>
    </div>
  );
}

function Kpi({ label, value, hint, tone }: { label: string; value: number | string; hint?: string; tone?: 'good' | 'bad' | 'warn' }) {
  return (
    <div className={`card kpi ${tone ?? ''}`}>
      <div className="kpi-label">{label}</div>
      <div className="kpi-value">{value}</div>
      {hint && <div className="muted small">{hint}</div>}
    </div>
  );
}

/** 60 one-minute stacked bars (successful vs failed ERP calls), drawn as plain SVG. */
function TrafficChart({ buckets, now }: { buckets: MinuteBucket[]; now: Date }) {
  const minutes = useMemo(() => {
    const byKey = new Map(buckets.map((b) => [`${b.hour}:${b.minute}`, b]));
    return Array.from({ length: 60 }, (_, i) => {
      const at = new Date(now.getTime() - (59 - i) * 60_000);
      const b = byKey.get(`${at.getUTCHours()}:${at.getUTCMinutes()}`);
      return { at, ok: b?.ok ?? 0, error: b?.error ?? 0 };
    });
  }, [buckets, now]);

  const max = Math.max(1, ...minutes.map((m) => m.ok + m.error));
  const width = 600, height = 140, gap = 2, barWidth = width / 60 - gap;
  const total = minutes.reduce((sum, m) => sum + m.ok + m.error, 0);
  const errors = minutes.reduce((sum, m) => sum + m.error, 0);

  return (
    <figure className="chart">
      <svg viewBox={`0 0 ${width} ${height}`} role="img" aria-label={`Son 60 dakikada ${total} çağrı, ${errors} hata`}>
        {minutes.map((m, i) => {
          const okHeight = (m.ok / max) * (height - 10);
          const errHeight = (m.error / max) * (height - 10);
          const x = i * (barWidth + gap);
          return (
            <g key={i}>
              <title>{`${m.at.toLocaleTimeString('tr-TR', { hour: '2-digit', minute: '2-digit' })} · ${m.ok} başarılı, ${m.error} hata`}</title>
              <rect x={x} y={height - okHeight} width={barWidth} height={okHeight} className="bar-ok" />
              <rect x={x} y={height - okHeight - errHeight} width={barWidth} height={errHeight} className="bar-err" />
            </g>
          );
        })}
      </svg>
      <figcaption className="muted small">
        {total} kayıtlı çağrı/olay · <span className="legend ok" /> başarılı · <span className="legend err" /> hata ({errors})
      </figcaption>
    </figure>
  );
}

function SyncTable({ states }: { states: SyncState[] }) {
  return (
    <table>
      <thead><tr><th>Akış</th><th>Watermark</th><th>Son başarılı</th></tr></thead>
      <tbody>
        {states.map((s) => (
          <tr key={s.name}>
            <td>{feedLabels[s.name] ?? s.name}</td>
            <td className="mono">{time(s.watermark)}</td>
            <td>{s.lastError ? <span className="pill s-Failed" title={s.lastError}>hata</span> : <span className="pill s-Processed">{ago(s.lastSuccessAt)}</span>}</td>
          </tr>
        ))}
      </tbody>
    </table>
  );
}
