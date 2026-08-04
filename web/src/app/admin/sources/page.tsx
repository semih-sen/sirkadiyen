import { AdminPageFrame } from '@/components/AdminPageFrame';
import { AdminPageHeader } from '@/components/AdminShell';
import { AdminSourceWorkspace } from '@/components/AdminSourceStatus';

export default function SourcesPage() { return <AdminPageFrame active="sources"><AdminPageHeader eyebrow="Akademik veri" title="Kaynaklar" description="Kaynak sağlığını saklanmış kanıtlarla izle ve idari yükleme kaynaklarına belge aktar." /><AdminSourceWorkspace /></AdminPageFrame>; }
