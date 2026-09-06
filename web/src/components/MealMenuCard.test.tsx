import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { MealMenuCard } from './MealMenuCard';

const api = vi.hoisted(() => ({
  getMealSubscription: vi.fn(),
  setMealSubscription: vi.fn(),
  ApiError: class ApiError extends Error {},
}));
vi.mock('@/lib/api', () => api);

describe('MealMenuCard', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    api.getMealSubscription.mockResolvedValue({ enabled: false });
    api.setMealSubscription.mockImplementation((enabled: boolean) =>
      Promise.resolve({ enabled }));
  });

  it('reflects the stored preference once loaded', async () => {
    api.getMealSubscription.mockResolvedValue({ enabled: true });
    render(<MealMenuCard />);

    const checkbox = await screen.findByRole('checkbox');
    await waitFor(() => expect(checkbox).toBeChecked());
  });

  it('enabling records the choice and confirms it will be added', async () => {
    render(<MealMenuCard />);
    const checkbox = await screen.findByRole('checkbox');
    expect(checkbox).not.toBeChecked();

    await userEvent.click(checkbox);

    expect(api.setMealSubscription).toHaveBeenCalledWith(true);
    await waitFor(() => expect(checkbox).toBeChecked());
    expect(await screen.findByRole('status')).toHaveTextContent(/eklenecek/i);
  });

  it('disabling records the choice and confirms removal', async () => {
    api.getMealSubscription.mockResolvedValue({ enabled: true });
    render(<MealMenuCard />);
    const checkbox = await screen.findByRole('checkbox');
    await waitFor(() => expect(checkbox).toBeChecked());

    await userEvent.click(checkbox);

    expect(api.setMealSubscription).toHaveBeenCalledWith(false);
    await waitFor(() => expect(checkbox).not.toBeChecked());
    expect(await screen.findByRole('status')).toHaveTextContent(/kaldırılacak/i);
  });

  it('reverts and shows an error when saving fails', async () => {
    render(<MealMenuCard />);
    const checkbox = await screen.findByRole('checkbox');
    api.setMealSubscription.mockRejectedValueOnce(new api.ApiError('Tercih kaydedilemedi.'));

    await userEvent.click(checkbox);

    await waitFor(() => expect(checkbox).not.toBeChecked());
    expect(screen.getByText('Tercih kaydedilemedi.')).toBeInTheDocument();
  });
});
