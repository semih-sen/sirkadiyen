import { AdminPageFrame } from '@/components/AdminPageFrame';
import { AdminPageHeader } from '@/components/AdminShell';
import { AdminVault } from '@/components/AdminVault';

export default function Page() {
  return (
    <AdminPageFrame active="vault">
      <AdminPageHeader
        eyebrow="Kişisel"
        title="Obsidian notları"
        description="Claude Code ile vault'a yazılan notların geçmişi; buradan yeni not isteği de gönderilebilir."
      />
      <AdminVault />
    </AdminPageFrame>
  );
}
