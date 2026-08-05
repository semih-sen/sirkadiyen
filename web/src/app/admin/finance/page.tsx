import { AdminPageFrame } from '@/components/AdminPageFrame';
import { AdminPageHeader } from '@/components/AdminShell';
import { FinanceWorkspace } from '@/components/FinanceWorkspace';

export default function FinancePage() {
  return <AdminPageFrame active="finance">
    <AdminPageHeader
      eyebrow="Operasyon & yönetim"
      title="Finans"
      description="Nakit defterini, alacak ve borçları, hesapları ve bağlayıcı kâr dağıtım kararlarını backend kayıtlarından yönetin."
    />
    <FinanceWorkspace />
  </AdminPageFrame>;
}
