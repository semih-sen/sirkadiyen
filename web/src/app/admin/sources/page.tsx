import { AdminPageFrame } from '@/components/AdminPageFrame';
import { AdminPageHeader } from '@/components/AdminShell';
import { SourceDocumentUpload } from '@/components/SourceDocumentUpload';

export default function SourcesPage() {
  return <AdminPageFrame active="sources"><AdminPageHeader eyebrow="Akademik veri" title="Kaynaklar" description="Fakültenin doğrudan yayınlamadığı program belgelerini güvenli biçimde sisteme al ve aktarım geçmişini incele." /><section className="card admin-workspace-card"><SourceDocumentUpload /></section></AdminPageFrame>;
}
