import { render, screen } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { AdminServerStatus } from './AdminServerStatus';

const api = vi.hoisted(() => ({
  getHealth: vi.fn(),
  getAdminMetrics: vi.fn(),
  getAdminServiceHealth: vi.fn(),
  getAdminWorkers: vi.fn(),
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
  });

  it('shows contract-backed API, worker, parser and metrics states', async () => {
    render(<AdminServerStatus />);
    expect(await screen.findByText('Toplam kullanıcı')).toBeInTheDocument();
    expect(screen.getByText('Worker')).toBeInTheDocument();
    expect(screen.getByText("Worker is in stage 'waiting'.")).toBeInTheDocument();
    expect(screen.getByText('Parser /health could not be reached.')).toBeInTheDocument();
    expect(screen.getByText('Yanıt başarısız')).toBeInTheDocument();
    expect(screen.getByText(/CPU, RAM, disk, Redis/)).toBeInTheDocument();
    expect(screen.getByText('host-a:1234')).toBeInTheDocument();
    expect(screen.getByText('1 aktif instance')).toBeInTheDocument();
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
