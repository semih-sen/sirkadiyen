import { AdminUnavailable } from '@/components/AdminUnavailable';
export default function Page() { return <AdminUnavailable active="finance" title="Finans" description="Gelir, gider ve dağıtım kararlarının denetimli yönetim alanı." capabilities={['Gelir ve gider kayıtları', 'Dönemsel özet ve kâr dağıtımı', 'Gerekçeli değişiklik ve denetim geçmişi']} />; }
