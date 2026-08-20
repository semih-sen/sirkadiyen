import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, expect, it, vi } from 'vitest';
import { DeleteAccountCard } from './DeleteAccountCard';

const api = vi.hoisted(() => ({
  deleteOwnAccount: vi.fn(),
  ApiError: class extends Error {},
}));
vi.mock('@/lib/api', () => api);

const session = vi.hoisted(() => ({ setUser: vi.fn(), user: { email: 'me@example.com' } }));
vi.mock('@/components/SessionProvider', () => ({ useSession: () => session }));

const replace = vi.hoisted(() => vi.fn());
vi.mock('next/navigation', () => ({ useRouter: () => ({ replace }) }));

beforeEach(() => {
  vi.clearAllMocks();
});

it('deletes only when the typed e-mail matches, then ends the session', async () => {
  api.deleteOwnAccount.mockResolvedValue({
    hadManagedCalendar: true, googleCalendarDeleted: true, googleTokenRevoked: true,
  });
  render(<DeleteAccountCard />);

  await userEvent.click(screen.getByRole('button', { name: 'Hesabımı silmek istiyorum' }));
  const confirm = screen.getByRole('button', { name: 'Hesabımı kalıcı olarak sil' });
  expect(confirm).toBeDisabled();

  await userEvent.type(screen.getByLabelText(/e-posta adresini yaz/), 'me@example.com');
  expect(confirm).toBeEnabled();

  await userEvent.click(confirm);
  await waitFor(() => expect(api.deleteOwnAccount).toHaveBeenCalledWith('me@example.com'));
  expect(session.setUser).toHaveBeenCalledWith(null);
  expect(replace).toHaveBeenCalledWith('/sign-in?deleted=1');
});

it('keeps the delete button disabled for a mismatched e-mail', async () => {
  render(<DeleteAccountCard />);
  await userEvent.click(screen.getByRole('button', { name: 'Hesabımı silmek istiyorum' }));

  await userEvent.type(screen.getByLabelText(/e-posta adresini yaz/), 'someone@else.com');
  expect(screen.getByRole('button', { name: 'Hesabımı kalıcı olarak sil' })).toBeDisabled();
  expect(api.deleteOwnAccount).not.toHaveBeenCalled();
});
