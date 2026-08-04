import { AdminPageFrame } from '@/components/AdminPageFrame';
import { AdminPageHeader } from '@/components/AdminShell';
import { AdminAccessLogWorkspace } from '@/components/AdminAccessLogs';
export default function Page() { return <AdminPageFrame active="access-logs"><AdminPageHeader eyebrow="Hassas kişisel veri" title="Erişim ve audit kayıtları" description="Maskeli erişim kayıtlarını ve hesap etkinliklerini yetkili, sayfalı görünümlerden incele." /><AdminAccessLogWorkspace /></AdminPageFrame>; }
