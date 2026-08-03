import { AdminUnavailable } from '@/components/AdminUnavailable';
export default function Page() { return <AdminUnavailable active="user-warning" title="Kullanıcı uyarısı" description="Tek bir kullanıcının yönetilen takvimine izlenebilir uyarı iletimi." capabilities={['Kullanıcı arama ve doğrulama', 'Şablonlu uyarı önizlemesi', 'Idempotent gönderim ve denetim kaydı']} />; }
