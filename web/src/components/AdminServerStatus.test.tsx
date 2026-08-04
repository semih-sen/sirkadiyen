import { render, screen } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { AdminServerStatus } from './AdminServerStatus';

const api = vi.hoisted(() => ({ getHealth: vi.fn(), getAdminMetrics: vi.fn() }));
vi.mock('@/lib/api', () => api);

describe('AdminServerStatus', () => {
  beforeEach(() => {
    api.getHealth.mockImplementation((kind: string) => Promise.resolve({ ok: kind === 'live', status: kind === 'live' ? 200 : 503, text: kind === 'live' ? 'Healthy' : 'Unhealthy' }));
    api.getAdminMetrics.mockResolvedValue({ generatedAtUtc: '2026-08-04T10:00:00Z', totalUsers: 10, activeLicenses: 8, initialSyncsInProgress: 1, completedConnections: 7, revisionsAwaitingReview: 2, heldDiffs: 3, pollingSourcesOverdue: 4, operationalFreezeActive: false });
  });

  it('shows only contract-backed health and metrics', async () => {
    render(<AdminServerStatus />);
    expect(await screen.findByText('Toplam kullanıcı')).toBeInTheDocument();
    expect(screen.getByText(/Worker, Parser veya Redis durumu değildir/)).toBeInTheDocument();
    expect(screen.queryByText('CPU')).not.toBeInTheDocument();
    expect(screen.getByText('Yanıt başarısız')).toBeInTheDocument();
  });
});
