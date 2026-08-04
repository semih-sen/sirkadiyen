import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { AdminAccessLogWorkspace } from './AdminAccessLogs';

const api = vi.hoisted(() => ({ listAccessLogs: vi.fn(), listAuditEvents: vi.fn(), listAdminUsers: vi.fn(), unmaskAuditIp: vi.fn() }));
vi.mock('@/lib/api', () => api);

describe('AdminAccessLogWorkspace', () => {
  beforeEach(() => {
    api.listAccessLogs.mockResolvedValue({ items: [{ id: 'a1', category: 'SignIn', occurredAtUtc: '2026-08-04T10:00:00Z', actorEmail: 'user@example.com', maskedIp: '192.0.2.***', hasProtectedIp: true }], page: 1, pageSize: 50, totalCount: 1, totalPages: 1 });
    api.listAuditEvents.mockResolvedValue({ items: [], page: 1, pageSize: 50, totalCount: 0, totalPages: 0 });
    api.unmaskAuditIp.mockResolvedValue({ auditEventId: 'a1', ip: '192.0.2.10' });
  });

  it('requires a reason and reveals the IP only after success', async () => {
    render(<AdminAccessLogWorkspace />);
    await screen.findByText('192.0.2.***');
    await userEvent.click(screen.getByRole('button', { name: 'IP maskesini kaldır' }));
    expect(screen.getByRole('button', { name: 'Görüntüle ve kaydet' })).toBeDisabled();
    await userEvent.type(screen.getByLabelText('IP görüntüleme gerekçesi'), 'Şüpheli oturum incelemesi');
    await userEvent.click(screen.getByRole('button', { name: 'Görüntüle ve kaydet' }));
    await waitFor(() => expect(screen.getByText('192.0.2.10')).toBeInTheDocument());
    expect(api.unmaskAuditIp).toHaveBeenCalledWith('a1', 'Şüpheli oturum incelemesi');
  });
});
