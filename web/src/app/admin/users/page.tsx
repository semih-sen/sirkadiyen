import { AdminPageFrame } from '@/components/AdminPageFrame';
import { AdminPageHeader } from '@/components/AdminShell';
import { AdminUserDirectory } from '@/components/AdminUserDirectory';

export default function UsersPage() {
  return <AdminPageFrame active="users"><AdminPageHeader eyebrow="Kimlik & erişim" title="Kullanıcılar ve lisanslar" description="Kullanıcı durumlarını, akademik profilleri ve lisans yaşam döngüsünü backend kayıtlarından incele." /><AdminUserDirectory /></AdminPageFrame>;
}
