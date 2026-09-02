'use client';

import { useCallback, useEffect, useState } from 'react';
import { ApiError, getAdminMetrics, getAdminServerResources, getAdminServiceHealth, getAdminWorkers, getHealth } from '@/lib/api';
import { formatDateTime } from '@/components/AdminData';
import type {
  AdminMetricsSnapshot,
  AdminServiceHealthSnapshot,
  HealthStatus,
  ServerResourceReading,
  ServerResourceSnapshot,
  ServiceHealthView,
  WorkerInstancesResponse,
  WorkerInstanceStatus,
} from '@/lib/types';

export function AdminServerStatus() {
  const [metrics, setMetrics] = useState<AdminMetricsSnapshot | null>(null);
  const [services, setServices] = useState<AdminServiceHealthSnapshot | null>(null);
  const [workers, setWorkers] = useState<WorkerInstancesResponse | null>(null);
  const [resources, setResources] = useState<ServerResourceSnapshot | null>(null);
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
      getAdminWorkers(),
      getAdminServerResources(),
    ]);
    setLive(results[0].status === 'fulfilled' ? results[0].value : { ok: false, status: 0, text: 'Yanıt alınamadı' });
    setReady(results[1].status === 'fulfilled' ? results[1].value : { ok: false, status: 0, text: 'Yanıt alınamadı' });
    if (results[2].status === 'fulfilled') setMetrics(results[2].value);
    else setError(results[2].reason instanceof ApiError ? results[2].reason.message : 'Operasyon metrikleri alınamadı.');
    setServices(results[3].status === 'fulfilled' ? results[3].value : null);
    setWorkers(results[4].status === 'fulfilled' ? results[4].value : null);
    setResources(results[5].status === 'fulfilled' ? results[5].value : null);
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
      <ServerResources resources={resources} loading={loading} />
      <WorkerInstances workers={workers} unavailable={!loading && !workers} />
      {metrics && <Metrics metrics={metrics} />}
      <div className="impl-note">Redis ve Google API hata oranı için backend sözleşmesi bulunmadığından gösterilmez.</div>
    </div>
  );
}

const READING_LABELS: Record<number, string> = { 0: 'Şimdi', 60: '1 dk önce', 300: '5 dk önce', 900: '15 dk önce' };

function ServerResources({ resources, loading }: { resources: ServerResourceSnapshot | null; loading: boolean }) {
  if (!resources) {
    return (
      <section className="card card-content">
        <h2 style={{ fontSize: 17 }}>Sunucu kaynakları</h2>
        <p className="muted" style={{ fontSize: 13, marginTop: 8 }}>
          {loading ? 'Yükleniyor…' : 'Kaynak verisi alınamadı.'}
        </p>
      </section>
    );
  }

  if (!resources.isAvailable) {
    return (
      <section className="card card-content">
        <h2 style={{ fontSize: 17 }}>Sunucu kaynakları</h2>
        <p className="muted" style={{ fontSize: 13, marginTop: 8 }}>
          {resources.unavailableReason ?? 'Bu ana bilgisayarda kaynak sayaçları okunamıyor.'}
        </p>
      </section>
    );
  }

  const loads = resources.loadAverages ?? null;
  return (
    <section className="card card-content">
      <div className="cluster" style={{ justifyContent: 'space-between' }}>
        <h2 style={{ fontSize: 17 }}>Sunucu kaynakları</h2>
        <span className="badge badge-neutral">{resources.processorCount} çekirdek</span>
      </div>
      <p className="muted" style={{ fontSize: 12, marginTop: 4 }}>
        Ubuntu ana bilgisayarının anlık ve geçmiş kullanımı; {resources.sampleIntervalSeconds} sn’de bir
        gerçek örnekleme yapılır. Üretim: {formatDateTime(resources.generatedAtUtc)}
      </p>

      {loads && (
        <div className="admin-overview-grid" style={{ marginTop: 14 }}>
          {[['Yük ort. (1 dk)', loads[0]], ['Yük ort. (5 dk)', loads[1]], ['Yük ort. (15 dk)', loads[2]]].map(
            ([label, value]) => (
              <div className="admin-area-card" key={label as string}>
                <span className="muted">{label}</span>
                <strong style={{ display: 'block', fontSize: 26, marginTop: 6 }}>
                  {typeof value === 'number' ? value.toFixed(2) : '—'}
                </strong>
                <small className="muted">{loadHint(value as number, resources.processorCount)}</small>
              </div>
            ),
          )}
        </div>
      )}

      <div className="table-wrap" style={{ marginTop: 16 }}>
        <table className="data-table data-table--stack">
          <thead>
            <tr><th>Zaman</th><th>CPU</th><th>RAM</th><th>RAM kullanımı</th><th>Disk (kök)</th></tr>
          </thead>
          <tbody>
            {resources.readings.map((reading) => (
              <ResourceRow key={reading.targetSecondsAgo} reading={reading} memoryTotalBytes={resources.memoryTotalBytes ?? null} />
            ))}
          </tbody>
        </table>
      </div>
      <p className="muted" style={{ fontSize: 12, marginTop: 8 }}>
        Geçmiş değerler süreç içi tamponda tutulur; API yeni başladıysa 5 ve 15 dakikalık satırlar tampon
        dolana kadar boş kalır. Değerler ana bilgisayarın tamamına aittir, yalnız API sürecine değil.
      </p>

      <DiskTable resources={resources} />
    </section>
  );
}

function ResourceRow({ reading, memoryTotalBytes }: { reading: ServerResourceReading; memoryTotalBytes: number | null }) {
  const label = READING_LABELS[reading.targetSecondsAgo] ?? `${reading.targetSecondsAgo} sn önce`;
  const memoryUsage =
    reading.memoryUsedBytes != null
      ? memoryTotalBytes != null
        ? `${formatBytes(reading.memoryUsedBytes)} / ${formatBytes(memoryTotalBytes)}`
        : formatBytes(reading.memoryUsedBytes)
      : '—';
  return (
    <tr>
      <td>
        <strong>{label}</strong>
        {reading.sampleAtUtc && <small className="muted" style={{ display: 'block' }}>{formatDateTime(reading.sampleAtUtc)}</small>}
      </td>
      <td>{formatPercent(reading.cpuPercent)}</td>
      <td>{formatPercent(reading.memoryPercent)}</td>
      <td>{memoryUsage}</td>
      <td>{formatPercent(reading.diskPercent)}</td>
    </tr>
  );
}

function DiskTable({ resources }: { resources: ServerResourceSnapshot }) {
  if (resources.disks.length === 0) {
    return <p className="muted" style={{ fontSize: 13, marginTop: 14 }}>Bağlı fiziksel disk bulunamadı.</p>;
  }
  return (
    <div className="table-wrap" style={{ marginTop: 16 }}>
      <table className="data-table data-table--stack">
        <thead>
          <tr><th>Disk bölümü</th><th>Kullanım</th><th>Kullanılan</th><th>Boş</th><th>Toplam</th></tr>
        </thead>
        <tbody>
          {resources.disks.map((disk) => (
            <tr key={disk.mountPoint}>
              <td><strong className="mono">{disk.mountPoint}</strong></td>
              <td>
                <span className={`badge ${disk.usedPercent >= 90 ? 'badge-danger' : disk.usedPercent >= 75 ? 'badge-warning' : 'badge-success'}`}>
                  %{disk.usedPercent.toFixed(1)}
                </span>
              </td>
              <td>{formatBytes(disk.usedBytes)}</td>
              <td>{formatBytes(disk.availableBytes)}</td>
              <td>{formatBytes(disk.totalBytes)}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

/** A quick read on load average relative to core count: at/above core count means the CPU is saturated. */
function loadHint(load: number, cores: number): string {
  if (!Number.isFinite(load) || cores <= 0) return '';
  const ratio = load / cores;
  if (ratio >= 1) return 'çekirdek başına dolu';
  if (ratio >= 0.7) return 'yükseliyor';
  return 'rahat';
}

function formatPercent(value: number | null): string {
  return value == null ? '—' : `%${value.toFixed(1)}`;
}

function formatBytes(bytes: number): string {
  if (!Number.isFinite(bytes)) return '—';
  if (bytes < 1024) return `${bytes} B`;
  const units = ['KB', 'MB', 'GB', 'TB', 'PB'];
  let value = bytes / 1024;
  let unit = 0;
  while (value >= 1024 && unit < units.length - 1) {
    value /= 1024;
    unit += 1;
  }
  return `${value.toFixed(value >= 100 || unit === 0 ? 0 : 1)} ${units[unit]}`;
}

function WorkerInstances({
  workers,
  unavailable,
}: {
  workers: WorkerInstancesResponse | null;
  unavailable: boolean;
}) {
  if (!workers) {
    return (
      <section className="card card-content">
        <h2 style={{ fontSize: 17 }}>Worker instance’ları</h2>
        <p className="muted" style={{ fontSize: 13, marginTop: 8 }}>
          {unavailable ? 'Instance verisi alınamadı.' : 'Yükleniyor…'}
        </p>
      </section>
    );
  }

  const active = workers.activeInstanceCount;
  return (
    <section className="card card-content">
      <div className="cluster" style={{ justifyContent: 'space-between' }}>
        <h2 style={{ fontSize: 17 }}>Worker instance’ları</h2>
        <span className={`badge ${active > 1 ? 'badge-warning' : active === 1 ? 'badge-success' : 'badge-neutral'}`}>
          {active} aktif instance
        </span>
      </div>
      {active > 1 && (
        <div className="error" role="alert" style={{ marginTop: 10 }}>
          Birden fazla aktif worker instance’ı çalışıyor. Tek instance çalıştırılması önerilir; fence
          eşzamanlı yazmayı engelliyor ama çoklu instance beklenmiyorsa gözden geçir.
        </div>
      )}
      <p className="muted" style={{ fontSize: 12, marginTop: 8 }}>
        Her instance her döngüde kendini bildirir. {workers.activeThresholdSeconds} sn içinde bildirim
        yapan “aktif” sayılır; bildirmeyenler 1 gün sonra otomatik silinir. Kontrol:{' '}
        {formatDateTime(workers.checkedAtUtc)}
      </p>
      {workers.instances.length === 0 ? (
        <p className="muted" style={{ fontSize: 13, marginTop: 8 }}>Kayıtlı instance yok.</p>
      ) : (
        <div className="table-wrap" style={{ marginTop: 12 }}>
          <table className="data-table data-table--stack">
            <thead><tr><th>Instance</th><th>Şu an</th><th>Çalışma süresi</th><th>Son bildirim</th><th>Sonraki tarama</th></tr></thead>
            <tbody>
              {workers.instances.map((instance) => (
                <WorkerRow key={instance.instanceId} instance={instance} checkedAtUtc={workers.checkedAtUtc} />
              ))}
            </tbody>
          </table>
        </div>
      )}
    </section>
  );
}

function WorkerRow({ instance, checkedAtUtc }: { instance: WorkerInstanceStatus; checkedAtUtc: string }) {
  return (
    <tr style={{ opacity: instance.isActive ? 1 : 0.55 }}>
      <td>
        <strong className="mono">{instance.instanceId}</strong>
        <small className="muted" style={{ display: 'block' }}>
          {instance.isActive ? 'aktif' : 'yanıt vermiyor'} · {instance.status}
        </small>
      </td>
      <td><span className="badge badge-neutral">{instance.currentStage}</span></td>
      <td>{formatDuration(instance.startedAtUtc, checkedAtUtc)}</td>
      <td>{formatDuration(instance.lastHeartbeatAtUtc, checkedAtUtc)} önce</td>
      <td>{formatNextPoll(instance.nextSourcePollAtUtc, checkedAtUtc)}</td>
    </tr>
  );
}

/** The next scheduled source poll as a relative hint: due now, or in roughly how long. */
function formatNextPoll(nextIso: string | null, nowIso: string): string {
  if (!nextIso) return '—';
  const deltaSeconds = Math.round((Date.parse(nextIso) - Date.parse(nowIso)) / 1000);
  if (deltaSeconds <= 0) return 'şimdi';
  return `~${formatDuration(nowIso, nextIso)} sonra`;
}

function formatDuration(fromIso: string, toIso: string): string {
  const seconds = Math.max(0, Math.round((Date.parse(toIso) - Date.parse(fromIso)) / 1000));
  if (seconds < 60) return `${seconds} sn`;
  const minutes = Math.floor(seconds / 60);
  if (minutes < 60) return `${minutes} dk`;
  const hours = Math.floor(minutes / 60);
  if (hours < 24) return `${hours} sa ${minutes % 60} dk`;
  return `${Math.floor(hours / 24)} g ${hours % 24} sa`;
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
      {service?.lastSeenAtUtc && <small>Son aktivite: {formatDateTime(service.lastSeenAtUtc)}</small>}
    </section>
  );
}
