import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { FinanceWorkspace } from './FinanceWorkspace';

const api = vi.hoisted(() => {
  class ApiError extends Error { status: number; constructor(status: number, message: string) { super(message); this.status = status; } }
  return {
    ApiError,
    listFinanceHolders: vi.fn(), listFinanceAccounts: vi.fn(), getFinanceSummary: vi.fn(), getFinanceTrend: vi.fn(),
    listFinanceTransactions: vi.fn(), getFinanceTransaction: vi.fn(), getFinanceTransactionHistory: vi.fn(), exportFinanceTransactions: vi.fn(),
    recordFinanceIncome: vi.fn(), recordFinanceExpense: vi.fn(), recordFinanceTransfer: vi.fn(), recordFinanceOpeningBalance: vi.fn(), updateFinanceTransaction: vi.fn(), deleteFinanceTransaction: vi.fn(),
    listFinanceObligations: vi.fn(), getFinanceObligation: vi.fn(), createFinanceObligation: vi.fn(), settleFinanceObligation: vi.fn(), cancelFinanceObligationSettlement: vi.fn(), writeOffFinanceObligation: vi.fn(), cancelFinanceObligation: vi.fn(),
    createFinanceHolder: vi.fn(), setFinanceHolderShare: vi.fn(), deactivateFinanceHolder: vi.fn(), openFinanceAccount: vi.fn(), closeFinanceAccount: vi.fn(),
    listFinanceDistributions: vi.fn(), previewFinanceDistribution: vi.fn(), executeFinanceDistribution: vi.fn(), reverseFinanceDistribution: vi.fn(),
    listFinanceAudit: vi.fn(),
  };
});
vi.mock('@/lib/api', () => api);

const holder = { holderId: 'holder-1', displayName: 'Semih Şen', userId: null, shareBasisPoints: 10000, status: 'Active' };
const account = { accountId: 'account-1', financeAccountHolderId: 'holder-1', holderDisplayName: 'Semih Şen', name: 'Ana banka', kind: 'Bank', currencyCode: 'TRY', status: 'Active', openedOn: '2026-01-01', currentBalance: 612900, balanceAsOfOn: '2026-08-05' };
const summary = { periodStartOn: '2026-08-01', periodEndOn: '2026-08-31', accountId: null, carriedOver: 564700, income: 184500, expenses: 136300, balance: 48200, currentBalance: 612900, asOfOn: '2026-08-05', toBeCarriedOver: 612900, receivables: 25000, collections: 12000, debts: 8000, payments: 4000, periodStartsInFuture: false, periodIsClosed: false, categoryTotals: [{ category: 'LicenseSales', kind: 'Income', total: 184500 }] };
const emptyPage = { items: [], page: 1, pageSize: 50, totalCount: 0, totalPages: 0 };

describe('FinanceWorkspace', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    api.listFinanceHolders.mockResolvedValue([holder]); api.listFinanceAccounts.mockResolvedValue([account]);
    api.getFinanceSummary.mockResolvedValue(summary); api.getFinanceTrend.mockResolvedValue([{ year: 2026, month: 8, income: 184500, expenses: 136300, net: 48200 }]);
    api.listFinanceTransactions.mockResolvedValue(emptyPage); api.listFinanceObligations.mockResolvedValue(emptyPage);
    api.listFinanceDistributions.mockResolvedValue([]); api.listFinanceAudit.mockResolvedValue(emptyPage);
    api.recordFinanceIncome.mockResolvedValue({ outcome: 'Recorded', transactionId: 'transaction-1' });
    api.cancelFinanceObligationSettlement.mockResolvedValue({ outcome: 'SettlementCancelled', obligationId: 'obligation-1' });
    api.executeFinanceDistribution.mockResolvedValue({ outcome: 'Executed', distributionId: 'distribution-1' });
  });

  it('renders authoritative summary, trend and category figures', async () => {
    render(<FinanceWorkspace />);
    expect((await screen.findAllByText('₺184.500,00')).length).toBeGreaterThan(0);
    expect(screen.getByText('₺136.300,00')).toBeInTheDocument();
    expect(screen.getByText('Lisans satışları')).toBeInTheDocument();
    expect(screen.getByRole('img', { name: /gelir ₺184.500,00/ })).toBeInTheDocument();
    expect(api.getFinanceSummary).toHaveBeenCalledWith({ period: 'CurrentMonth', accountId: undefined });
  });

  it('records income with the selected account and server category', async () => {
    const user = userEvent.setup(); render(<FinanceWorkspace />);
    await screen.findByRole('img', { name: /gelir ₺184.500,00/ }); await user.click(screen.getByRole('tab', { name: 'İşlemler' }));
    await user.click(screen.getByRole('button', { name: '+ Gelir' }));
    const dialog = screen.getByRole('dialog');
    await user.selectOptions(within(dialog).getByLabelText('Hesap'), 'account-1');
    await user.type(within(dialog).getByLabelText('Tutar (TRY)'), '142000,50');
    await user.type(within(dialog).getByLabelText('Açıklama'), 'Dönem lisans satışları');
    await user.click(within(dialog).getByRole('button', { name: 'İşlemi kaydet' }));
    await waitFor(() => expect(api.recordFinanceIncome).toHaveBeenCalledWith(expect.objectContaining({ accountId: 'account-1', amount: 142000.5, category: 'LicenseSales', description: 'Dönem lisans satışları' })));
  });

  it('cancels a historical settlement link without requesting transaction deletion', async () => {
    const obligation = { obligationId: 'obligation-1', direction: 'Receivable', category: 'Sponsorship', counterpartyName: 'A Corp', description: null, amount: 500, settledAmount: 200, issuedOn: '2026-08-01', dueOn: '2026-08-20', status: 'PartiallySettled', rowVersion: 1, settlements: [{ settlementId: 'settlement-1', transactionId: 'transaction-1', amount: 200, settledOn: '2026-08-05', recordedAtUtc: '2026-08-05T10:00:00Z', reference: 'INV-41' }] };
    api.listFinanceObligations.mockResolvedValue({ ...emptyPage, items: [obligation], totalCount: 1, totalPages: 1 }); api.getFinanceObligation.mockResolvedValue(obligation);
    const user = userEvent.setup(); render(<FinanceWorkspace />); await screen.findByRole('img', { name: /gelir ₺184.500,00/ });
    await user.click(screen.getByRole('tab', { name: 'Alacak & Borç' })); await user.click(await screen.findByText('A Corp'));
    await user.click(await screen.findByRole('button', { name: 'Bağlantıyı iptal et' }));
    const dialog = screen.getByRole('dialog'); await user.type(within(dialog).getByLabelText('Gerekçe'), 'Yanlış yükümlülük bağlantısı');
    await user.type(within(dialog).getByLabelText(/Onaylamak için/), 'BAĞLANTIYI KALDIR'); await user.click(within(dialog).getByRole('button', { name: 'Bağlantıyı kaldır' }));
    await waitFor(() => expect(api.cancelFinanceObligationSettlement).toHaveBeenCalledWith('obligation-1', 'settlement-1', 'Yanlış yükümlülük bağlantısı'));
    expect(api.deleteFinanceTransaction).not.toHaveBeenCalled();
  });

  it('executes exactly the server-previewed distribution plan', async () => {
    const plan = { outcome: 'Ready', periodStartOn: '2026-07-01', periodEndOn: '2026-07-31', sourceAccountId: 'account-1', distributableAmount: 48200, shares: [{ holderId: 'holder-1', holderDisplayName: 'Semih Şen', shareBasisPoints: 10000, exactShareMinorUnits: 4820000, allocatedAmount: 48200, remainderUnitAwarded: false }], exclusions: [], confirmationToken: 'token-1', planHash: 'hash-1', expectedConfirmationPhrase: '48200.00' };
    api.previewFinanceDistribution.mockResolvedValue(plan);
    const user = userEvent.setup(); render(<FinanceWorkspace />); await screen.findByRole('img', { name: /gelir ₺184.500,00/ });
    await user.click(screen.getByRole('tab', { name: 'Kâr Dağıtımı' })); await user.selectOptions(screen.getByLabelText('Kaynak hesap'), 'account-1');
    await user.click(screen.getByRole('button', { name: 'Sunucuda ön izle' })); await user.click(await screen.findByRole('button', { name: 'Güçlü onaya geç' }));
    const dialog = screen.getByRole('dialog'); await user.type(within(dialog).getByLabelText('Dağıtım gerekçesi'), 'Temmuz dağıtımı'); await user.type(within(dialog).getByLabelText(/Onaylamak için tutarı/), '48200.00');
    await user.click(within(dialog).getByRole('button', { name: 'Dağıtımı çalıştır' }));
    await waitFor(() => expect(api.executeFinanceDistribution).toHaveBeenCalledWith({ periodStartOn: '2026-07-01', periodEndOn: '2026-07-31', sourceAccountId: 'account-1', confirmationToken: 'token-1', planHash: 'hash-1', expectedConfirmationPhrase: '48200.00', reason: 'Temmuz dağıtımı' }));
  });
});
