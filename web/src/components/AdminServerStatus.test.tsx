import { render, screen } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { AdminServerStatus } from './AdminServerStatus';

const api = vi.hoisted(() => ({
  getHealth: vi.fn(),
  getAdminMetrics: vi.fn(),
  getAdminServiceHealth: vi.fn(),
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
      worker: { service: 'worker', state: 'Healthy', lastSeenAtUtc: '2026-08-04T09:59:55Z', detail: 'Heartbeat is current.' },
      parser: { service: 'parser', state: 'Unhealthy', detail: 'Parser /health could not be reached.' },
    });
  });

  it('shows contract-backed API, worker, parser and metrics states', async () => {
    render(<AdminServerStatus />);
    expect(await screen.findByText('Toplam kullanıcı')).toBeInTheDocument();
    expect(screen.getByText('Worker')).toBeInTheDocument();
    expect(screen.getByText('Heartbeat is current.')).toBeInTheDocument();
    expect(screen.getByText('Parser /health could not be reached.')).toBeInTheDocument();
    expect(screen.getByText('Yanıt başarısız')).toBeInTheDocument();
    expect(screen.getByText(/CPU, RAM, disk, Redis/)).toBeInTheDocument();
  });
});
