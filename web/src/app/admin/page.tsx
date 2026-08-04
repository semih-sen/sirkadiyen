'use client';

import Link from 'next/link';
import { AdminPageFrame } from '@/components/AdminPageFrame';
import { AdminPageHeader } from '@/components/AdminShell';

const AREAS = [
  { href: '/admin/sources', icon: '⇄', title: 'Kaynaklar', text: 'Program belgelerini yükle ve son aktarım kayıtlarını izle.', state: 'Canlı' },
  { href: '/admin/revisions', icon: '✓', title: 'Revizyonlar', text: 'İnceleme bekleyen revizyonları, bulguları ve yayın kararlarını yönet.', state: 'Canlı' },
  { href: '/admin/colors', icon: '◐', title: 'Anabilim dalı renkleri', text: '45 anabilim dalının fakülte genelindeki takvim görünümünü düzenle.', state: 'Canlı' },
  { href: '/admin/operations', icon: '⚙', title: 'Operasyon kontrolü', text: 'Acil durumda veri hattını denetimli biçimde dondur veya yeniden aç.', state: 'Canlı' },
  { href: '/admin/users', icon: '◉', title: 'Kullanıcılar & lisans', text: 'Kullanıcı, profil, giriş ve lisans yaşam döngüsünü incele.', state: 'Canlı' },
  { href: '/admin/server', icon: '▣', title: 'Sistem sağlığı', text: 'API sağlığı ve gerçek operasyon sayaçlarını izle.', state: 'Canlı' },
  { href: '/admin/access-logs', icon: '☰', title: 'Erişim & audit', text: 'Maskeli erişim ve hesap etkinliği kayıtlarını incele.', state: 'Canlı' },
];

export default function AdminOverviewPage() {
  return (
    <AdminPageFrame active="dashboard">
      <AdminPageHeader
        eyebrow="Kontrol merkezi"
        title="Genel bakış"
        description="Operasyonları buradan çalıştırmak yerine doğru çalışma alanına geç. Bu ekran yalnızca yönetim haritası ve sistem kapsamını gösterir."
      />
      <div className="admin-overview-grid">
        {AREAS.map((area) => (
          <Link className="admin-area-card" href={area.href} key={area.href}>
            <span className="admin-area-icon" aria-hidden="true">{area.icon}</span>
            <span className={`badge ${area.state === 'Canlı' ? 'badge-success' : ''}`}>{area.state}</span>
            <h2>{area.title}</h2>
            <p>{area.text}</p>
            <span className="admin-area-link">Panele git <span aria-hidden="true">→</span></span>
          </Link>
        ))}
      </div>
    </AdminPageFrame>
  );
}
