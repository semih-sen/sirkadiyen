import { AdminPageFrame } from '@/components/AdminPageFrame';
import { AdminPageHeader } from '@/components/AdminShell';
import { UserWarningComposer } from '@/components/UserWarningComposer';

export default function UserWarningPage() {
  return (
    <AdminPageFrame active="user-warning">
      <AdminPageHeader
        eyebrow="Kullanıcı işlemleri"
        title="Tek kullanıcı uyarısı"
        description="Tek bir kullanıcının yönetilen takvimine izlenebilir bir uyarı yaz. Uyarı anahtarı kullanıcı, şablon ve tarihten türetilir; aynı gün ikinci gönderim yeni bir etkinlik oluşturmaz."
      />
      <section className="card admin-workspace-card"><UserWarningComposer /></section>
    </AdminPageFrame>
  );
}
