import { AdminPageFrame } from '@/components/AdminPageFrame';
import { AdminPageHeader } from '@/components/AdminShell';
import { RevisionReview } from '@/components/RevisionReview';

export default function RevisionsPage() {
  return <AdminPageFrame active="revisions"><AdminPageHeader eyebrow="Yayın güvenliği" title="Revizyon inceleme" description="Otomatik doğrulamanın beklettiği program değişikliklerini bulgularıyla değerlendir; yalnızca kaynağı kontrol ettiğinde yayınla." /><section className="card admin-workspace-card"><RevisionReview /></section></AdminPageFrame>;
}
