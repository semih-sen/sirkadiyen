import { AdminPageFrame } from '@/components/AdminPageFrame';
import { AdminPageHeader } from '@/components/AdminShell';
import { DepartmentColorEditor } from '@/components/DepartmentColorEditor';

export default function ColorsPage() {
  return <AdminPageFrame active="colors"><AdminPageHeader eyebrow="Takvim görünümü" title="Anabilim dalı renkleri" description="Fakülte genelindeki varsayılan paleti yönet. Kullanıcıların kişisel tercihleri bu varsayılanların önüne geçmeye devam eder." /><DepartmentColorEditor mode="admin" /></AdminPageFrame>;
}
