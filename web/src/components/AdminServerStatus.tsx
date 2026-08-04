'use client';

import { useCallback, useEffect, useState } from 'react';
import { ApiError, getAdminMetrics, getAdminServiceHealth, getHealth } from '@/lib/api';
import { formatDateTime } from '@/components/AdminData';
import type {
  AdminMetricsSnapshot,
  AdminServiceHealthSnapshot,
  HealthStatus,
  ServiceHealthView,
} from '@/lib/types';

export function AdminServerStatus() {
  const [metrics, setMetrics] = useState<AdminMetricsSnapshot | null>(null);
  const [services, setServices] = useState<AdminServiceHealthSnapshot | null>(null);
  const [live, setLive] = useState<HealthStatus | null>(null);
  const [ready, setReady] = useState<HealthStatus | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    const results = await Promise.allSettled([
      getHealth('live'),
      getHealth('ready'),
      getAdminMetrics(),
      getAdminServiceHealth(),
    ]);
    setLive(results[0].status === 'fulfilled' ? results[0].value : { ok: false, status: 0, text: 'Yanıt alınamadı' });
    setReady(results[1].status === 'fulfilled' ? results[1].value : { ok: false, status: 0, text: 'Yanıt alınamadı' });
    if (results[2].status === 'fulfilled') setMetrics(results[2].value);
    else setError(results[2].reason instanceof ApiError ? results[2].reason.message : 'Operasyon metrikleri alınamadı.');
    setServices(results[3].status === 'fulfilled' ? results[3].value : null);
    setLoading(false);
  }, []);

  useEffect(() => { void load(); }, [load]);

  return (
    <div className="stack" style={{ gap: 18 }}>
      <div className="cluster" style={{ justifyContent: 'space-between' }}>
        <p className="muted">Otomatik yenileme yapılmaz; servisler yalnız “Şimdi yenile” ile anlık kontrol edilir.</p>
        <button className="btn btn-secondary btn-sm" type="button" disabled={loading} onClick={() => void load()}>{loading ? 'Yenileniyor…' : 'Şimdi yenile'}</button>
      </div>

      <div className="grid grid-2">
        <HealthCard title="API liveness" health={live} note="HTTP sürecinin yanıt verdiğini gösterir." />
        <HealthCard title="API readiness" health={ready} note="Hazırlık kontrolü PostgreSQL bağlantısını içerir." />
        <ServiceCard title="Worker" service={services?.worker ?? null} unavailable={!loading && !services} />
        <ServiceCard title="Parser" service={services?.parser ?? null} unavailable={!loading && !services} />
      </div>

      {services && <p className="muted" style={{ fontSize: 12 }}>Servis kontrolü: {formatDateTime(services.checkedAtUtc)}</p>}
      {error && <div className="error" role="alert">{error}</div>}
      {metrics && <Metrics metrics={metrics} />}
      <div className="impl-note">CPU, RAM, disk, Redis ve hata oranı için backend sözleşmesi bulunmadığından gösterilmez.</div>
    </div>
  );
}

function Metrics({ metrics }: { metrics: AdminMetricsSnapshot }) {
  const cards: Array<[string, number]> = [
    ['Toplam kullanıcı', metrics.totalUsers],
    ['Aktif lisans', metrics.activeLicenses],
    ['İlk senkron sürüyor', metrics.initialSyncsInProgress],
    ['Tamamlanmış bağlantı', metrics.completedConnections],
    ['İnceleme bekleyen revizyon', metrics.revisionsAwaitingReview],
    ['Bekletilen diff', metrics.heldDiffs],
    ['Geciken polling kaynağı', metrics.pollingSourcesOverdue],
  ];
  return (
    <section className="card card-content">
      <div className="cluster" style={{ justifyContent: 'space-between' }}>
        <h2 style={{ fontSize: 17 }}>Operasyon özeti</h2>
        <span className={`badge ${metrics.operationalFreezeActive ? 'badge-warning' : 'badge-success'}`}>{metrics.operationalFreezeActive ? 'En az bir hat donduruldu' : 'Hatlar aktif'}</span>
      </div>
      <p className="muted" style={{ fontSize: 12, marginTop: 4 }}>Üretim: {formatDateTime(metrics.generatedAtUtc)}</p>
      <div className="admin-overview-grid" style={{ marginTop: 16 }}>
        {cards.map(([label, value]) => <div className="admin-area-card" key={label}><span className="muted">{label}</span><strong style={{ display: 'block', fontSize: 28, marginTop: 6 }}>{value}</strong></div>)}
      </div>
    </section>
  );
}

function HealthCard({ title, health, note }: { title: string; health: HealthStatus | null; note: string }) {
  return (
    <section className="card card-content">
      <div className="cluster" style={{ justifyContent: 'space-between' }}><h2 style={{ fontSize: 16 }}>{title}</h2><span className={`badge ${health?.ok ? 'badge-success' : health ? 'badge-danger' : 'badge-neutral'}`}>{health ? health.ok ? 'Sağlıklı' : 'Yanıt başarısız' : 'Yükleniyor'}</span></div>
      <p className="muted" style={{ fontSize: 13, marginTop: 8 }}>{note}</p>
      {health && <small className="mono">HTTP {health.status} · {health.text || 'boş yanıt'}</small>}
    </section>
  );
}

function ServiceCard({ title, service, unavailable }: { title: string; service: ServiceHealthView | null; unavailable: boolean }) {
  const healthy = service?.state === 'Healthy';
  const label = unavailable ? 'Kontrol alınamadı' : service?.state === 'Healthy' ? 'Sağlıklı' : service?.state === 'Unhealthy' ? 'Sağlıksız' : service ? 'Bilinmiyor' : 'Yükleniyor';
  return (
    <section className="card card-content">
      <div className="cluster" style={{ justifyContent: 'space-between' }}><h2 style={{ fontSize: 16 }}>{title}</h2><span className={`badge ${healthy ? 'badge-success' : service || unavailable ? 'badge-danger' : 'badge-neutral'}`}>{label}</span></div>
      <p className="muted" style={{ fontSize: 13, marginTop: 8 }}>{service?.detail ?? 'Servis sağlık sözleşmesi bekleniyor.'}</p>
      {service?.lastSeenAtUtc && <small>Son sinyal: {formatDateTime(service.lastSeenAtUtc)}</small>}
    </section>
  );
}
