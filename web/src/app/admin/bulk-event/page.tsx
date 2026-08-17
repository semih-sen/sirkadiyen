import { AdminPageFrame } from '@/components/AdminPageFrame';
import { AdminPageHeader } from '@/components/AdminShell';
import { BulkEventComposer } from '@/components/BulkEventComposer';

export default function BulkEventPage() {
  return (
    <AdminPageFrame active="bulk-event">
      <AdminPageHeader
        eyebrow="Kullanıcı işlemleri"
        title="Toplu takvim etkinliği"
        description="Bir akademik kitleye etkinlik yaz. Alıcılar sunucuda çözülür, hariç bırakılanlar gerekçesiyle listelenir ve onay yalnızca gördüğün plana bağlanır; bu bir gönder düğmesi değil, bir dağıtım işlemidir."
      />
      <section className="card admin-workspace-card"><BulkEventComposer /></section>
    </AdminPageFrame>
  );
}
