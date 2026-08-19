import { AdminPageFrame } from '@/components/AdminPageFrame';
import { AdminPageHeader } from '@/components/AdminShell';
import { FreezeControl } from '@/components/AdminOperations';
import { CalendarRepairControl } from '@/components/CalendarRepairControl';
import { ProfileRolloverControl } from '@/components/ProfileRolloverControl';

export default function OperationsPage() {
  return <AdminPageFrame active="operations"><AdminPageHeader eyebrow="Sistem güvenliği" title="Operasyon kontrolü" description="Kritik veri hattı davranışlarını gerekçeli, denetlenen işlemlerle yönet." /><div className="admin-narrow-workspace"><FreezeControl /><CalendarRepairControl /><ProfileRolloverControl /></div></AdminPageFrame>;
}
