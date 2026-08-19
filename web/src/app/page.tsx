import Image from 'next/image';
import Link from 'next/link';
import { SiteNav, SiteFooter } from '@/components/ui';

// Public landing page (plan §5.1). No session required; the nav's "Giriş yap"
// routes to /sign-in, which redirects an already-signed-in user to where they
// belong. Static server component — the FAQ uses native <details>, so it needs
// no client JavaScript.

const STEPS = [
  { title: 'Google ile giriş yap', body: 'Şifre yok. Fakülte e-postanla ilişkili Google hesabınla giriş yaparsın.' },
  { title: 'Lisansını ve profilini tanımla', body: 'Lisans kodunu gir, akademik yıl/dönem/grup bilgilerini seç.' },
  { title: 'Takvim iznini ver', body: 'Sirkadiyen’in kendi takvimini oluşturmasına izin verirsin.' },
  { title: 'İlk senkronizasyonu izle', body: 'Sunucu programını okur, etkinlikleri oluşturur; ilerlemeyi canlı görürsün.' },
];

// Bu liste config/schedule-sources.json içindeki kaynak kataloğunu yansıtır;
// katalog genişledikçe burası da güncellenmelidir.
const SUPPORT = [
  {
    badge: 'badge-success',
    tone: 'Tam destek',
    title: 'Dönem 1',
    body: 'Türkçe ve İngilizce program. Yıllık ders programının yanında uygulama çizelgesi de işlenir; A–H grupları ve alt grupları ayrı ayrı ele alınır.',
  },
  {
    badge: 'badge-success',
    tone: 'Tam destek',
    title: 'Dönem 2',
    body: 'Türkçe ve İngilizce program. Yıllık program ve uygulama çizelgesine ek olarak anatomi salon grup saatleri ile dikey koridor beceri uygulamaları da takvime girer.',
  },
  {
    badge: 'badge-success',
    tone: 'Tam destek',
    title: 'Dönem 3 — Türkçe',
    body: 'A ve B müfredat grupları. Yıllık program, hasta başı uygulama konuları ve öğretim üyesi uygulama grupları uygulama yerleriyle birlikte eşitlenir.',
  },
  {
    badge: 'badge-warning',
    tone: 'Kısmi destek',
    title: 'Dönem 3 — İngilizce',
    body: 'Yıllık ders programı desteklenir. Hasta başı ve öğretim üyesi uygulama çizelgeleri kaynak tarafında ayrıştığında eklenecek.',
  },
  {
    badge: 'badge-neutral',
    tone: 'Yol haritasında',
    title: 'Klinik dönemler (4–6)',
    body: 'Staj ve poliklinik çizelgeleri için kaynak entegrasyonu planlama aşamasında; henüz genel kullanıma açık değil.',
  },
];

const FAQ = [
  {
    q: 'Sirkadiyen takvimimdeki diğer etkinliklere dokunur mu?',
    a: 'Hayır. Sirkadiyen yalnızca kendi oluşturduğu ayrı takvimde işlem yapar; mevcut kişisel veya iş takvimlerine erişmez, değiştirmez.',
  },
  {
    q: 'Program değiştiğinde takvimim ne zaman güncellenir?',
    a: 'Kaynak fakülte programı değiştiğinde, sunucu tarafı senkronizasyon süreci bunu algılar ve etkinlikleri günceller. Güncelleme anlık değildir; panelde son senkronizasyon zamanını görebilirsin.',
  },
  {
    q: 'Lisans kodumu kaybedersem ne olur?',
    a: 'İletişim panelinden destek ekibiyle iletişime geçebilirsin. Güvenlik nedeniyle düz metin lisans kodu geçmişi tutulmaz.',
  },
  {
    q: 'Google izni verdikten sonra vazgeçebilir miyim?',
    a: 'Evet. Google hesap ayarlarından erişimi istediğin zaman iptal edebilirsin. Bu durumda Sirkadiyen bir sonraki senkronizasyonda “yeniden yetkilendirme gerekli” durumunu gösterir.',
  },
  {
    q: 'Kaynak veride bir sorun olursa takvimim yanlış mı güncellenir?',
    a: 'Hayır. Kaynak veride anomali tespit edilirse güncelleme bilerek beklemeye alınır ve incelemeye gönderilir; eski hâli korunur.',
  },
];

export default function LandingPage() {
  return (
    <>
      <a className="skip-link" href="#main">
        İçeriğe geç
      </a>
      <SiteNav />

      <main id="main">
        {/* HERO */}
        <section style={{ padding: '56px 0 88px' }}>
          <div className="container hero-grid">
            <div>
              <span className="eyebrow">İstanbul Tıp Fakültesi için akademik takvim eşitleme</span>
              <h1 style={{ marginTop: 14 }}>Ders programın değişse bile takvimin güncel kalır.</h1>
              <p className="lede" style={{ marginTop: 18 }}>
                Sirkadiyen fakültenin resmî ders programını okur, senin akademik grubuna göre
                kişiselleştirir ve Google Takvim’inde kendi oluşturduğu ayrı bir takvimde günceller.
                Program değiştiğinde, takvimin de değişir — sen bir şey yapmadan.
              </p>
              <div className="cluster" style={{ gap: 12, marginTop: 28 }}>
                <Link className="btn btn-primary" href="/sign-in">
                  Google ile giriş yap
                </Link>
                <Link className="btn btn-secondary" href="/sign-in?intent=lisans">
                  Lisans kodumu etkinleştir
                </Link>
              </div>
              <div className="cluster" style={{ gap: 22, marginTop: 32, fontSize: 13, color: 'var(--ink-70)' }}>
                <span>🔒 Yalnızca Sirkadiyen’in oluşturduğu takvime erişim</span>
                <span>🎓 Dönem 1–3 için destek</span>
                <span>🛠️ Kaynak sorunlarında takvim bilerek beklemede</span>
              </div>
            </div>
            {/* Gerçek bir öğrencinin senkronize edilmiş takvimi; masaüstü ve mobil
                Google Takvim ekran görüntüleri public/demo altında durur. */}
            <div className="demo-shot">
              <Image
                className="demo-shot__desktop"
                src="/demo/takvim-desktop.png"
                alt="Google Takvim’in hafta görünümünde Sirkadiyen’in oluşturduğu ders programı: her güne yayılmış ders, uygulama ve serbest çalışma blokları."
                width={1081}
                height={792}
                priority
                sizes="(max-width: 960px) 92vw, 540px"
              />
              <Image
                className="demo-shot__mobile"
                src="/demo/takvim-mobile.jpeg"
                alt="Google Takvim mobil uygulamasında aynı ders programının haftalık görünümü."
                width={739}
                height={1600}
                sizes="(max-width: 560px) 42vw, 140px"
              />
            </div>
          </div>
        </section>

        {/* GOOGLE CALENDAR */}
        <section className="section" style={{ background: 'var(--surface)' }}>
          <div className="container">
            <span className="eyebrow">Google Takvim entegrasyonu</span>
            <h2 style={{ marginTop: 10, maxWidth: '20ch' }}>
              Kendi takvimlerine karışmayan, ayrı ve yönetilen bir takvim.
            </h2>
            <p className="lede" style={{ marginTop: 14 }}>
              Sirkadiyen, hesabında “Sirkadiyen Ders Programı” adında ayrı bir takvim oluşturur. Bu
              takvim açılıp kapatılabilir, renklendirilebilir ve kişisel takvimlerinle karışmaz.
            </p>
            <div className="grid grid-2" style={{ marginTop: 32 }}>
              <div className="card card-content">
                <h4>✅ Erişebildiği</h4>
                <ul style={{ listStyle: 'none', margin: '14px 0 0', padding: 0, display: 'grid', gap: 10, fontSize: 14.5 }}>
                  <li>Yalnızca kendi oluşturduğu “Sirkadiyen Ders Programı” takvimi</li>
                  <li>Bu takvimdeki etkinlikleri oluşturma, güncelleme, kaldırma</li>
                  <li>Google hesap kimliğini doğrulama amaçlı okuma</li>
                </ul>
              </div>
              <div className="card card-content">
                <h4>🚫 Erişemediği</h4>
                <ul className="muted" style={{ listStyle: 'none', margin: '14px 0 0', padding: 0, display: 'grid', gap: 10, fontSize: 14.5 }}>
                  <li>Mevcut kişisel veya iş takvimlerin</li>
                  <li>Diğer takvimlerdeki etkinliklerin</li>
                  <li>Gmail, Drive veya başka bir Google servisi</li>
                </ul>
              </div>
            </div>
            <p className="muted" style={{ marginTop: 20 }}>
              Verilerinin nasıl işlendiğini{' '}
              <Link href="/privacy" style={{ color: 'var(--fg)', fontWeight: 600 }}>
                Gizlilik Politikası
              </Link>
              ’nda okuyabilirsin.
            </p>
          </div>
        </section>

        {/* NASIL ÇALIŞIR */}
        <section className="section" id="nasil-calisir">
          <div className="container">
            <span className="eyebrow">Nasıl çalışır</span>
            <h2 style={{ marginTop: 10 }}>Dört adımda kurulum, sonrasında kendiliğinden devam eder.</h2>
            <div className="grid grid-4" style={{ marginTop: 36 }}>
              {STEPS.map((step, index) => (
                <div className="card card-content" key={step.title}>
                  <div style={{ fontFamily: 'var(--font-display)', fontSize: 26, fontWeight: 700, color: 'var(--muted)' }}>
                    {index + 1}
                  </div>
                  <h4 style={{ marginTop: 8 }}>{step.title}</h4>
                  <p className="muted" style={{ marginTop: 8 }}>
                    {step.body}
                  </p>
                </div>
              ))}
            </div>
          </div>
        </section>

        {/* KAPSAM */}
        <section className="section" style={{ background: 'var(--surface)' }} id="kapsam">
          <div className="container">
            <span className="eyebrow">Desteklenen kapsam</span>
            <h2 style={{ marginTop: 10 }}>Bugün gerçekten desteklenen dönem ve programlar.</h2>
            <p className="lede" style={{ marginTop: 14 }}>
              Kapsamı olduğundan geniş göstermiyoruz — hangi dönemin ne durumda olduğunu açıkça
              belirtiyoruz. Desteklenen her dönemde yalnızca genel ders programı değil, kendi
              uygulama ve grup çizelgen de takvime işlenir.
            </p>
            <div className="support-grid" style={{ marginTop: 28 }}>
              {SUPPORT.map((item) => (
                <div className="support-card" key={item.title}>
                  <span className={`badge ${item.badge}`}>{item.tone}</span>
                  <h4 style={{ marginTop: 12 }}>{item.title}</h4>
                  <p className="muted">{item.body}</p>
                </div>
              ))}
            </div>
            <p className="muted" style={{ marginTop: 20 }}>
              Profilinde seçebileceğin dönem, program dili ve gruplar bu kapsamdan üretilir; kapsam
              dışında kalan bir seçim kurulum sırasında hiç gösterilmez.
            </p>
          </div>
        </section>

        {/* SSS */}
        <section className="section" id="sss">
          <div className="container" style={{ maxWidth: 820 }}>
            <span className="eyebrow">Sıkça sorulan sorular</span>
            <h2 style={{ marginTop: 10 }}>Merak edilenler</h2>
            <div style={{ marginTop: 24 }}>
              {FAQ.map((item, index) => (
                <details className="faq-item" key={item.q} open={index === 0}>
                  <summary>{item.q}</summary>
                  <p>{item.a}</p>
                </details>
              ))}
            </div>
          </div>
        </section>

        {/* SON CTA */}
        <section className="section" style={{ background: 'var(--surface)' }}>
          <div className="container">
            <div style={{ background: 'var(--fg)', color: '#fff', borderRadius: 'var(--radius-card)', padding: 56, textAlign: 'center' }}>
              <h2 style={{ color: '#fff' }}>Ders programını takip etmeyi bırak, Sirkadiyen’e bırak.</h2>
              <p className="lede" style={{ color: 'color-mix(in oklab, #fff 78%, transparent)', margin: '12px auto 28px' }}>
                Google ile giriş yap ya da elindeki lisans kodunu etkinleştirerek başla.
              </p>
              <div className="cluster" style={{ gap: 12, justifyContent: 'center' }}>
                <Link className="btn" style={{ background: '#fff', color: 'var(--fg)' }} href="/sign-in">
                  Google ile giriş yap
                </Link>
                <Link className="btn btn-secondary" style={{ borderColor: '#fff', color: '#fff' }} href="/sign-in?intent=lisans">
                  Lisansımı etkinleştir
                </Link>
              </div>
            </div>
          </div>
        </section>
      </main>

      <SiteFooter />
    </>
  );
}
