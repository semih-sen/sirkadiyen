import { render, screen } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { AdminServerStatus } from './AdminServerStatus';

const api = vi.hoisted(() => ({
  getHealth: vi.fn(),
  getAdminMetrics: vi.fn(),
  getAdminServiceHealth: vi.fn(),
  getAdminWorkers: vi.fn(),
  getAdminServerResources: vi.fn(),
}));
vi.mock('@/lib/api', () => api);

describe('AdminServerStatus', () => {
  beforeEach(() => {
    api.getHealth.mockImplementation((kind: string) => Promise.resolve({
      ok: kind === 'live',
      status: kind === 'live' ? 200 : 503,
      text: kind === 'live' ? 'Healthy' : 'Unhealthy',
    }));
    api.getAdminMetrics.mockResolvedValue({ generatedAtUtc: '2026-08-04T10:00:00Z', totalUsers: 10, activeLicenses: 8, initialSyncsInProgress: 1, completedConnections: 7, revisionsAwaitingReview: 2, heldDiffs: 3, pollingSourcesOverdue: 4, operationalFreezeActive: false });
    api.getAdminServiceHealth.mockResolvedValue({
      checkedAtUtc: '2026-08-04T10:00:00Z',
      worker: { service: 'worker', state: 'Healthy', lastSeenAtUtc: '2026-08-04T09:59:55Z', detail: "Worker is in stage 'waiting'." },
      parser: { service: 'parser', state: 'Unhealthy', detail: 'Parser /health could not be reached.' },
    });
    api.getAdminWorkers.mockResolvedValue({
      checkedAtUtc: '2026-08-04T10:00:00Z',
      activeThresholdSeconds: 150,
      activeInstanceCount: 1,
      instances: [{
        instanceId: 'host-a:1234',
        status: 'healthy',
        currentStage: 'waiting',
        startedAtUtc: '2026-08-04T09:00:00Z',
        lastActivityAtUtc: '2026-08-04T09:59:55Z',
        lastHeartbeatAtUtc: '2026-08-04T09:59:58Z',
        isActive: true,
      }],
    });
    api.getAdminServerResources.mockResolvedValue({
      generatedAtUtc: '2026-08-04T10:00:00Z',
      isAvailable: true,
      sampleIntervalSeconds: 10,
      retainedSampleCount: 90,
      processorCount: 4,
      loadAverages: [0.52, 0.41, 0.33],
      memoryTotalBytes: 16_000_000_000,
      readings: [
        { targetSecondsAgo: 0, sampleAtUtc: '2026-08-04T10:00:00Z', cpuPercent: 12.5, memoryPercent: 48, memoryUsedBytes: 7_680_000_000, diskPercent: 61.2 },
        { targetSecondsAgo: 60, sampleAtUtc: '2026-08-04T09:59:00Z', cpuPercent: 14, memoryPercent: 47.5, memoryUsedBytes: 7_600_000_000, diskPercent: 61.2 },
        { targetSecondsAgo: 300, sampleAtUtc: '2026-08-04T09:55:00Z', cpuPercent: 9, memoryPercent: 46, memoryUsedBytes: 7_360_000_000, diskPercent: 61.1 },
        { targetSecondsAgo: 900, sampleAtUtc: null, cpuPercent: null, memoryPercent: null, memoryUsedBytes: null, diskPercent: null },
      ],
      disks: [
        { mountPoint: '/', totalBytes: 100_000_000_000, usedBytes: 61_200_000_000, availableBytes: 38_800_000_000, usedPercent: 61.2 },
      ],
    });
  });

  it('shows contract-backed API, worker, parser and metrics states', async () => {
    render(<AdminServerStatus />);
    expect(await screen.findByText('Toplam kullanıcı')).toBeInTheDocument();
    expect(screen.getByText('Worker')).toBeInTheDocument();
    expect(screen.getByText("Worker is in stage 'waiting'.")).toBeInTheDocument();
    expect(screen.getByText('Parser /health could not be reached.')).toBeInTheDocument();
    expect(screen.getByText('Yanıt başarısız')).toBeInTheDocument();
    expect(screen.getByText('host-a:1234')).toBeInTheDocument();
    expect(screen.getByText('1 aktif instance')).toBeInTheDocument();
  });

  it('renders host CPU, memory and disk readings for now and 1/5/15 minutes ago', async () => {
    render(<AdminServerStatus />);
    // Wait for a loaded-state marker; the heading also shows while loading, so awaiting it would
    // catch the transient node that React replaces once the resources arrive.
    expect(await screen.findByText('4 çekirdek')).toBeInTheDocument();
    expect(screen.getByText('Sunucu kaynakları')).toBeInTheDocument();
    expect(screen.getByText('Şimdi')).toBeInTheDocument();
    expect(screen.getByText('1 dk önce')).toBeInTheDocument();
    expect(screen.getByText('5 dk önce')).toBeInTheDocument();
    expect(screen.getByText('15 dk önce')).toBeInTheDocument();
    // The 15-minute reading has no sample yet, so its cells read as empty.
    expect(screen.getByText('%12.5')).toBeInTheDocument();
    expect(screen.getByText('4 çekirdek')).toBeInTheDocument();
    // The root filesystem shows in the disk table.
    expect(screen.getByText('/')).toBeInTheDocument();
  });

  it('reports when host resource counters are unavailable', async () => {
    api.getAdminServerResources.mockResolvedValue({
      generatedAtUtc: '2026-08-04T10:00:00Z',
      isAvailable: false,
      unavailableReason: 'Sunucu kaynak sayaçları yalnızca Linux (/proc) üzerinde okunur.',
      sampleIntervalSeconds: 10,
      retainedSampleCount: 0,
      processorCount: 4,
      readings: [],
      disks: [],
    });

    render(<AdminServerStatus />);

    expect(await screen.findByText(/yalnızca Linux/)).toBeInTheDocument();
  });

  it('warns when more than one worker instance is active', async () => {
    api.getAdminWorkers.mockResolvedValue({
      checkedAtUtc: '2026-08-04T10:00:00Z',
      activeThresholdSeconds: 150,
      activeInstanceCount: 2,
      instances: [
        { instanceId: 'host-a:1', status: 'healthy', currentStage: 'calendar-maintenance', startedAtUtc: '2026-08-04T09:00:00Z', lastActivityAtUtc: '2026-08-04T09:59:55Z', lastHeartbeatAtUtc: '2026-08-04T09:59:58Z', isActive: true },
        { instanceId: 'host-b:2', status: 'healthy', currentStage: 'waiting', startedAtUtc: '2026-08-04T09:30:00Z', lastActivityAtUtc: '2026-08-04T09:59:50Z', lastHeartbeatAtUtc: '2026-08-04T09:59:57Z', isActive: true },
      ],
    });

    render(<AdminServerStatus />);

    expect(await screen.findByText('2 aktif instance')).toBeInTheDocument();
    expect(screen.getByText(/Birden fazla aktif worker instance/)).toBeInTheDocument();
  });
});
