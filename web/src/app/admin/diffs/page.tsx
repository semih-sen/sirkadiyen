import { AdminPageFrame } from '@/components/AdminPageFrame';
import { AdminPageHeader } from '@/components/AdminShell';
import { DiffQueues } from '@/components/DiffQueues';

export default function DiffsPage() {
  return (
    <AdminPageFrame active="diffs">
      <AdminPageHeader
        eyebrow="Takvim güvenliği"
        title="Diff kuyrukları"
        description="Güvenlik eşiklerinin beklettiği ve dağıtımı kalıcı olarak başarısız olan diff’leri değiştirdikleri derslerle birlikte incele; yalnızca sorumluluğu üstlenebiliyorsan işlem yap."
      />
      <section className="card admin-workspace-card"><DiffQueues /></section>
    </AdminPageFrame>
  );
}
