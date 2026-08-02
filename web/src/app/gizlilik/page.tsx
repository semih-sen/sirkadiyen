import Link from 'next/link';
import { LegalDocument, type LegalSection } from '@/components/LegalDocument';

const P: React.CSSProperties = { marginTop: 12, maxWidth: '70ch' };

const SECTIONS: LegalSection[] = [
  {
    id: 'toplanan-veriler',
    title: 'Toplanan veriler',
    content: (
      <table className="legal-table">
        <thead>
          <tr>
            <th>Veri türü</th>
            <th>Örnek</th>
            <th>Kaynak</th>
          </tr>
        </thead>
        <tbody>
          <tr>
            <td>Kimlik bilgisi</td>
            <td>Ad, e-posta, Google hesap kimliği</td>
            <td>Google ile giriş</td>
          </tr>
          <tr>
            <td>Akademik profil</td>
            <td>Dönem, program dili, akademik gruplar, öğrenci numarası</td>
            <td>Kullanıcı girişi</td>
          </tr>
          <tr>
            <td>Lisans kaydı</td>
            <td>Lisans durumu, etkinleştirme tarihi (kod metni değil)</td>
            <td>Lisans etkinleştirme</td>
          </tr>
          <tr>
            <td>Takvim bağlantı durumu</td>
            <td>Yetki durumu, izin kapsamı (belirteç değil)</td>
            <td>Google Calendar yetkilendirme</td>
          </tr>
          <tr>
            <td>Oturum açma kaydı</td>
            <td>Zaman, IP (maskeli), tarayıcı, işletim sistemi, cihaz türü</td>
            <td>Kimlik doğrulama sistemi</td>
          </tr>
        </tbody>
      </table>
    ),
  },
  {
    id: 'google-kullanimi',
    title: 'Google kimliği ve Calendar verisi',
    content: (
      <p style={P}>
        Google ile girişte yalnızca kimliğini doğrulamak için gerekli temel profil bilgileri (ad,
        e-posta, hesap kimliği) alınır. Google Calendar izni ayrı bir adımda istenir ve yalnızca
        Sirkadiyen’in kendi oluşturduğu takvime sınırlıdır; mevcut kişisel takvimlerin okunmaz veya
        değiştirilmez.
      </p>
    ),
  },
  {
    id: 'oturum-kayitlari',
    title: 'Oturum açma kayıtları',
    content: (
      <p style={P}>
        Her kimlik doğrulama denemesi; zaman damgası, IP adresi, tarayıcı, işletim sistemi ve cihaz
        türüyle birlikte kaydedilir. IP adresleri arayüzde varsayılan olarak maskelenir; tam adresi
        görüntülemek ayrı yetki ve gerekçe gerektiren, denetlenen bir eylemdir.
      </p>
    ),
  },
  {
    id: 'amac-dayanak',
    title: 'Amaç ve hukuki dayanak',
    content: (
      <table className="legal-table">
        <thead>
          <tr>
            <th>Amaç</th>
            <th>Hukuki dayanak (taslak)</th>
          </tr>
        </thead>
        <tbody>
          <tr>
            <td>Kimlik doğrulama ve hesap yönetimi</td>
            <td>Sözleşmenin ifası</td>
          </tr>
          <tr>
            <td>Takvim senkronizasyonu</td>
            <td>Açık rıza (Google izin akışı)</td>
          </tr>
          <tr>
            <td>Güvenlik ve kötüye kullanım önleme</td>
            <td>Meşru menfaat</td>
          </tr>
        </tbody>
      </table>
    ),
  },
  {
    id: 'saklama',
    title: 'Saklama süreleri',
    content: (
      <p style={P}>
        Hesap verileri, hesap aktif olduğu sürece saklanır. Oturum açma kayıtları sınırlı bir süre
        için tutulur ve sonrasında anonimleştirilir. Kesin süreler hukuki inceleme sonrasında
        netleştirilecektir.
      </p>
    ),
  },
  {
    id: 'guvenlik',
    title: 'Güvenlik uygulamaları',
    content: (
      <p style={P}>
        Google erişim ve yenileme belirteçleri hiçbir arayüzde görüntülenmez. Gizli değerler
        yalnızca sunucu tarafında, şifrelenmiş biçimde tutulur. Yönetici işlemleri gerekçe ve
        denetim kaydı gerektirir.
      </p>
    ),
  },
  {
    id: 'haklar',
    title: 'Erişim, düzeltme, silme hakları',
    content: (
      <p style={P}>
        Kendi verilerine erişim talep edebilir, hatalı bilgileri düzeltebilir veya hesabının
        silinmesini isteyebilirsin. Talepler{' '}
        <Link href="/iletisim?kategori=gizlilik" style={{ color: 'var(--fg)', fontWeight: 600 }}>
          iletişim formu
        </Link>{' '}
        üzerinden iletilir.
      </p>
    ),
  },
  {
    id: 'ucuncu-taraf',
    title: 'Üçüncü taraf servisler',
    content: (
      <p style={P}>
        Google kimlik doğrulama ve Google Calendar API’si dışında, üçüncü taraf bir servisle veri
        paylaşılmaz. Fakülte kaynak sistemleri yalnızca okunur, veri gönderilmez.
      </p>
    ),
  },
  {
    id: 'iletisim',
    title: 'İletişim',
    content: (
      <p style={{ marginTop: 12 }}>
        Gizlilik ile ilgili sorular için:{' '}
        <a href="mailto:gizlilik@sirkadiyen.app" style={{ color: 'var(--fg)', fontWeight: 600 }}>
          gizlilik@sirkadiyen.app
        </a>
      </p>
    ),
  },
];

export default function PrivacyPage() {
  return (
    <LegalDocument
      title="Gizlilik Politikası"
      updated="2 Ağustos 2026"
      bannerText="Prototip metni — yayına almadan önce hukuki inceleme gerekir."
      sections={SECTIONS}
    />
  );
}
