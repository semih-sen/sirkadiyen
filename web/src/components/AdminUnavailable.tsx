import { AdminPageHeader, type AdminNavKey } from '@/components/AdminShell';
import { AdminPageFrame } from '@/components/AdminPageFrame';

export function AdminUnavailable({ active, title, description, capabilities }: { active: AdminNavKey; title: string; description: string; capabilities: string[] }) {
  return (
    <AdminPageFrame active={active}>
      <AdminPageHeader eyebrow="Hazırlanan çalışma alanı" title={title} description={description} />
      <section className="admin-empty-state">
        <div className="admin-empty-mark" aria-hidden="true">◇</div>
        <div><span className="badge">Backend bağlantısı bekleniyor</span><h2>Panel yapısı hazır, operasyon henüz açılmadı</h2><p>Sahte kayıt veya metrik göstermiyoruz. Aşağıdaki yetenekler için yetkili backend sözleşmesi tamamlandığında bu alan doğrudan canlanacak.</p></div>
        <ul>{capabilities.map((item) => <li key={item}>{item}</li>)}</ul>
      </section>
    </AdminPageFrame>
  );
}
