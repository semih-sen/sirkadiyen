import { AdminPageFrame } from '@/components/AdminPageFrame';
import { AdminPageHeader } from '@/components/AdminShell';
import { RosterCatalogEditor } from '@/components/RosterCatalogEditor';

export default function RostersPage() {
  return <AdminPageFrame active="rosters"><AdminPageHeader eyebrow="Akademik veri" title="Öğrenci listeleri" description="Kayıt sırasında öğrenci numarasının aranacağı yayınlanmış listeleri ve sütunlarının ne anlama geldiğini düzenle." /><RosterCatalogEditor /></AdminPageFrame>;
}
