import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { DepartmentColorEditor } from './DepartmentColorEditor';

const api = vi.hoisted(() => ({ getDepartmentColors: vi.fn(), setDepartmentColor: vi.fn(), resetDepartmentColor: vi.fn() }));
vi.mock('@/lib/api', async (original) => ({ ...(await original<typeof import('@/lib/api')>()), ...api }));

const colors = [
  { key: 'practice', kind: 'EventCategory', name: 'Uygulama', description: 'Laboratuvar ve pratik', division: null, systemDefaultColor: '#0B6B69', effectiveColor: '#0B6B69', adminDefaultColor: null, userColor: null },
  { key: 'anatomi', kind: 'Department', name: 'Anatomi', description: null, division: 'Basic', systemDefaultColor: '#123456', effectiveColor: '#AA1122', adminDefaultColor: null, userColor: '#AA1122' },
];

describe('DepartmentColorEditor (collapsible)', () => {
  beforeEach(() => { vi.clearAllMocks(); api.getDepartmentColors.mockResolvedValue(colors); });

  it('starts collapsed and does not fetch the palette until opened', async () => {
    render(<DepartmentColorEditor mode="user" collapsible />);
    const trigger = screen.getByRole('button', { name: /Kişisel takvim paletim/ });
    expect(trigger).toHaveAttribute('aria-expanded', 'false');
    expect(api.getDepartmentColors).not.toHaveBeenCalled();

    await userEvent.click(trigger);
    expect(trigger).toHaveAttribute('aria-expanded', 'true');
    expect(await screen.findByRole('heading', { name: 'Anatomi' })).toBeVisible();
    expect(screen.getByText('1 özel renk')).toBeInTheDocument();
  });

  it('keeps the loaded palette when reopened instead of refetching', async () => {
    render(<DepartmentColorEditor mode="user" collapsible />);
    const trigger = screen.getByRole('button', { name: /Kişisel takvim paletim/ });
    await userEvent.click(trigger);
    await waitFor(() => expect(api.getDepartmentColors).toHaveBeenCalledTimes(1));

    await userEvent.click(trigger);
    expect(trigger).toHaveAttribute('aria-expanded', 'false');
    // Kapalı panel erişilebilirlik ağacından da düşer; bu yüzden hidden araması gerekir.
    expect(screen.getByRole('heading', { name: 'Anatomi', hidden: true })).not.toBeVisible();

    await userEvent.click(trigger);
    expect(screen.getByRole('heading', { name: 'Anatomi' })).toBeVisible();
    expect(api.getDepartmentColors).toHaveBeenCalledTimes(1);
  });

  it('loads immediately when rendered without the collapsible flag', async () => {
    render(<DepartmentColorEditor mode="user" />);
    expect(await screen.findByRole('heading', { name: 'Anatomi' })).toBeVisible();
    expect(screen.queryByRole('button', { name: /Kişisel takvim paletim/ })).not.toBeInTheDocument();
  });
});
