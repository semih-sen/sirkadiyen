import { AdminPageFrame } from '@/components/AdminPageFrame';
import { AdminPageHeader } from '@/components/AdminShell';
import { FreezeControl } from '@/components/AdminOperations';

export default function OperationsPage() {
  return <AdminPageFrame active="operations"><AdminPageHeader eyebrow="Sistem güvenliği" title="Operasyon kontrolü" description="Kritik veri hattı davranışlarını gerekçeli, denetlenen işlemlerle yönet." /><div className="admin-narrow-workspace"><FreezeControl /></div></AdminPageFrame>;
}
