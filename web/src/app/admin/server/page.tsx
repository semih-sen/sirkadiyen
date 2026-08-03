import { AdminUnavailable } from '@/components/AdminUnavailable';
export default function Page() { return <AdminUnavailable active="server" title="Sunucu ve servis izleme" description="API, worker, parser, veri tabanı ve kuyrukların operasyonel görünürlüğü." capabilities={['Servis sağlık durumları', 'Kuyruk derinliği ve gecikme', 'Kaynak, parser ve Google API hata oranları']} />; }
