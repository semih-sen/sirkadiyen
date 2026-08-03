import { AdminPageFrame } from '@/components/AdminPageFrame';
import { AdminPageHeader } from '@/components/AdminShell';
import { SelfActivationCard } from '@/components/AdminOperations';
import { LicenseAdministration } from '@/components/LicenseAdministration';

export default function UsersPage() {
  return <AdminPageFrame active="users"><AdminPageHeader eyebrow="Kimlik & erişim" title="Kullanıcılar ve lisanslar" description="Tek kullanımlık lisans üret, gerektiğinde iptal et ve öğrenci akışını güvenli biçimde test et." /><LicenseAdministration /><div className="admin-two-column" style={{ marginTop: 18 }}><SelfActivationCard /><section className="admin-empty-state compact"><div className="admin-empty-mark">◉</div><div><span className="badge">Liste API’si bekleniyor</span><h2>Kullanıcı dizini</h2><p>Kullanıcı arama, profil, lisans ve senkron geçmişi için okuma uçları henüz yok. Bu nedenle kişisel veri uydurulmadan alan kapalı tutuluyor.</p></div></section></div></AdminPageFrame>;
}
