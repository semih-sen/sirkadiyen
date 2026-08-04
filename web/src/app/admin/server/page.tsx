import { AdminPageFrame } from '@/components/AdminPageFrame';
import { AdminPageHeader } from '@/components/AdminShell';
import { AdminServerStatus } from '@/components/AdminServerStatus';
export default function Page() { return <AdminPageFrame active="server"><AdminPageHeader eyebrow="Operasyonel görünürlük" title="Sunucu ve servis izleme" description="API sağlığını ve veritabanından hesaplanan operasyon sayılarını uydurma sinyal üretmeden izle." /><AdminServerStatus /></AdminPageFrame>; }
