import { LegalDocument, type LegalSection } from '@/components/LegalDocument';

const P: React.CSSProperties = { marginTop: 12, maxWidth: '70ch' };

const SECTIONS: LegalSection[] = [
  {
    id: 'kapsam',
    title: 'Hizmetin kapsamı',
    content: (
      <p style={P}>
        Sirkadiyen, İstanbul Tıp Fakültesi öğrencilerine yönelik, resmî ders programını
        kişiselleştirerek Google Calendar’a yansıtan bir eşitleme hizmetidir. Ders içeriği, akademik
        değerlendirme veya resmî kayıt işlemleriyle ilgisi yoktur.
      </p>
    ),
  },
  {
    id: 'kaynak-bagimlilik',
    title: 'Resmî kaynaklara bağımlılık',
    content: (
      <p style={P}>
        <strong>Takviminin doğruluğu, fakültenin yayımladığı kaynağın doğruluğuna bağlıdır.</strong>{' '}
        Kaynak veride hata veya belirsizlik varsa, bu Sirkadiyen tarafından tespit edilebilir ve
        güncelleme bilerek incelemeye alınabilir — ancak kaynağın kendisindeki hatalar Sirkadiyen’in
        sorumluluğunda değildir.
      </p>
    ),
  },
  {
    id: 'sorumluluklar',
    title: 'Kullanıcı sorumlulukları',
    content: (
      <p style={P}>
        Akademik profilini doğru ve güncel tutmak, lisans kodunu başkasıyla paylaşmamak ve hesabını
        yalnızca kendi adına kullanmak kullanıcının sorumluluğundadır.
      </p>
    ),
  },
  {
    id: 'lisans',
    title: 'Lisans ve deneme koşulları',
    content: (
      <p style={P}>
        Bir lisans kodu yalnızca bir kez etkinleştirilebilir. Deneme lisansları süreli olup, süre
        dolduğunda senkronizasyon otomatik olarak durur. Kalıcı lisansa geçiş ayrı bir süreçle
        sağlanır.
      </p>
    ),
  },
  {
    id: 'erisilebilirlik',
    title: 'Erişilebilirlik ve kesintiler',
    content: (
      <p style={P}>
        Hizmet, bakım veya beklenmeyen teknik sorunlar nedeniyle geçici olarak kesintiye uğrayabilir.
        Planlı bakımlar önceden duyurulmaya çalışılır.
      </p>
    ),
  },
  {
    id: 'askiya-alma',
    title: 'Askıya alma ve sonlandırma',
    content: (
      <p style={P}>
        Kötüye kullanım, sahte bilgi girişi veya lisans paylaşımı şüphesi durumunda hesap askıya
        alınabilir. Askıya alma işlemi gerekçesiyle birlikte kayıt altına alınır ve kullanıcıya
        bildirilir.
      </p>
    ),
  },
  {
    id: 'sorumluluk-siniri',
    title: 'Sorumluluk sınırları',
    content: (
      <p style={P}>
        Sirkadiyen, kaynak veri hatalarından, Google servis kesintilerinden veya kullanıcı tarafından
        girilen hatalı bilgilerden kaynaklanan sonuçlardan sorumlu tutulamaz. Bu bölüm nihai hukuki
        dil değildir.
      </p>
    ),
  },
  {
    id: 'senkron-sinirlari',
    title: 'Senkronizasyon sınırlamaları',
    content: (
      <p style={P}>
        Senkronizasyon anlık değildir; kaynak değişikliği ile takvim güncellemesi arasında bir
        gecikme olabilir. Kaynak veride anomali tespit edilirse güncelleme bilerek durdurulur.
      </p>
    ),
  },
  {
    id: 'iletisim',
    title: 'İletişim',
    content: (
      <p style={{ marginTop: 12 }}>
        Kullanım koşullarıyla ilgili sorular için:{' '}
        <a href="mailto:destek@sirkadiyen.app" style={{ color: 'var(--fg)', fontWeight: 600 }}>
          destek@sirkadiyen.app
        </a>
      </p>
    ),
  },
];

export default function TermsPage() {
  return (
    <LegalDocument
      title="Kullanım Koşulları"
      updated="2 Ağustos 2026"
      bannerText="Prototip metni — nihai hukuki tavsiye değildir, yayına almadan önce hukuki inceleme gerekir."
      sections={SECTIONS}
    />
  );
}
