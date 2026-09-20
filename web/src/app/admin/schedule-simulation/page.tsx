import { AdminPageFrame } from '@/components/AdminPageFrame';
import { AdminPageHeader } from '@/components/AdminShell';
import { ScheduleSimulation } from '@/components/ScheduleSimulation';

export default function ScheduleSimulationPage() {
  return <AdminPageFrame active="schedule-simulation"><AdminPageHeader eyebrow="Akademik veri" title="Program simülasyonu" description="Herhangi bir kitle için yayımlanmış canlı programı, o kitledeki bir öğrencinin Google Takvim'inde göreceği gibi haftalık olarak görüntüle. Hiçbir şey yazılmaz; gerçek bir kullanıcı gerekmez." /><ScheduleSimulation /></AdminPageFrame>;
}
