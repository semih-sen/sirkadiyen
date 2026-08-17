import { AdminPageFrame } from '@/components/AdminPageFrame';
import { AdminUserDetail } from '@/components/AdminUserDetail';

export default async function UserDetailPage({
  params,
}: {
  params: Promise<{ userId: string }>;
}) {
  const { userId } = await params;
  return (
    <AdminPageFrame active="users">
      <AdminUserDetail userId={userId} />
    </AdminPageFrame>
  );
}
