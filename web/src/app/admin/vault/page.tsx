import { AdminPageFrame } from '@/components/AdminPageFrame';
import { AdminPageHeader } from '@/components/AdminShell';
import { AdminVault } from '@/components/AdminVault';

export default function Page() {
  return (
    <AdminPageFrame active="vault">
      <AdminPageHeader
        eyebrow="Kişisel"
        title="Obsidian notları"
        description="Claude Code ile vault'a yazılan notların geçmişi ve vault'taki notlar; buradan yeni not isteyebilir, eski notlara flashcard ekletebilirsin."
      />
      <AdminVault />
    </AdminPageFrame>
  );
}
