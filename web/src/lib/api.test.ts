import { beforeEach, describe, expect, it, vi } from 'vitest';

function json(value: unknown, status = 200) {
  return new Response(JSON.stringify(value), { status, headers: { 'content-type': 'application/json' } });
}

describe('API client', () => {
  beforeEach(() => { vi.resetModules(); vi.stubGlobal('fetch', vi.fn()); });

  it('encodes schedule query parameters', async () => {
    vi.mocked(fetch).mockResolvedValueOnce(json([]));
    const { getUpcomingSchedule } = await import('./api');
    await getUpcomingSchedule(30);
    expect(fetch).toHaveBeenCalledWith('/api/schedule/upcoming?days=30', expect.objectContaining({ method: 'GET' }));
  });

  it('sends CSRF for reconciliation', async () => {
    vi.mocked(fetch)
      .mockResolvedValueOnce(json({ headerName: 'X-CSRF-TOKEN', requestToken: 'token' }))
      .mockResolvedValueOnce(json({ requested: true }, 202));
    const { requestReconciliation } = await import('./api');
    await expect(requestReconciliation()).resolves.toEqual({ requested: true });
    expect(fetch).toHaveBeenNthCalledWith(2, '/api/calendar/reconcile', expect.objectContaining({ method: 'POST', headers: expect.objectContaining({ 'X-CSRF-TOKEN': 'token' }) }));
  });

  it('sends the audited unmask reason', async () => {
    vi.mocked(fetch)
      .mockResolvedValueOnce(json({ headerName: 'X-CSRF-TOKEN', requestToken: 'token' }))
      .mockResolvedValueOnce(json({ auditEventId: 'a1', ip: '192.0.2.1' }));
    const { unmaskAuditIp } = await import('./api');
    await unmaskAuditIp('a/1', 'incident review');
    expect(fetch).toHaveBeenNthCalledWith(2, '/api/admin/access-logs/a%2F1/unmask', expect.objectContaining({ body: JSON.stringify({ reason: 'incident review' }) }));
  });

  it('reads plain-text health responses without JSON parsing', async () => {
    vi.mocked(fetch).mockResolvedValueOnce(new Response('Healthy', { status: 200 }));
    const { getHealth } = await import('./api');
    await expect(getHealth('ready')).resolves.toEqual({ ok: true, status: 200, text: 'Healthy' });
    expect(fetch).toHaveBeenCalledWith('/health/ready', { headers: { Accept: 'text/plain' } });
  });
});
