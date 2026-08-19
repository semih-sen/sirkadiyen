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

const SUPPORT = [
  { badge: 'badge-success', tone: 'Tam destek', title: 'Dönem 1', body: 'Türkçe ve İngilizce program dili, tüm müfredat grupları.' },
  { badge: 'badge-warning', tone: 'Kısmi destek', title: 'Dönem 2', body: 'Türkçe program tam destekli; İngilizce program kitle tanımı tamamlanınca eklenecek.' },
  { badge: 'badge-neutral', tone: 'Hazırlık aşamasında', title: 'Dönem 3', body: 'Kaynak yapısı doğrulanıyor; henüz genel kullanıma açık değil.' },
  { badge: 'badge-neutral', tone: 'Yol haritasında', title: 'Klinik dönemler', body: 'Staj ve poliklinik çizelgeleri için kaynak entegrasyonu planlama aşamasında.' },
];

const SECURITY = [
  { title: 'Google erişim kapsamı sınırlı', body: 'Yalnızca Sirkadiyen’in oluşturduğu takvime erişilir; kişisel takvimlerin okunmaz.' },
  { title: 'Gizli değerler asla görünmez', body: 'Erişim/yenileme belirteçleri, düz metin lisans kodu ve kimlik doğrulama başlıkları hiçbir ekranda gösterilmez.' },
  { title: 'Denetlenebilir işlemler', body: 'Askıya alma, veri erişimi kaldırma gibi işlemler gerekçe ve denetim kaydı ister.' },
  { title: 'Erişim kayıtları korunur', body: 'Oturum açma denemeleri IP, tarayıcı ve cihaz bilgisiyle kaydedilir; IP adresleri varsayılan olarak maskelidir.' },
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
          <div
            className="container"
            style={{
              display: 'grid',
              gridTemplateColumns: 'minmax(0, 1.05fr) minmax(0, 0.95fr)',
              gap: 56,
              alignItems: 'center',
            }}
          >
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
                <span>🎓 Dönem 1–2 için destek</span>
                <span>🛠️ Kaynak sorunlarında takvim bilerek beklemede</span>
              </div>
            </div>
            <div
              role="img"
              aria-label="Sirkadiyen ritmini simgeleyen iç içe geçmiş ince halkalardan oluşan soyut kompozisyon"
              style={{
                borderRadius: 'var(--radius-card)',
                overflow: 'hidden',
                border: '1px solid var(--border)',
                boxShadow: 'var(--shadow-md)',
              }}
            >
              <svg viewBox="0 0 640 480" width="100%" height="100%" style={{ background: 'var(--surface)', display: 'block' }}>
                <circle cx="320" cy="240" r="190" fill="none" stroke="#0b6b69" strokeOpacity="0.14" strokeWidth="1.5" />
                <circle cx="320" cy="240" r="160" fill="none" stroke="#0b6b69" strokeOpacity="0.22" strokeWidth="1.5" />
                <circle cx="320" cy="240" r="130" fill="none" stroke="#0b6b69" strokeOpacity="0.32" strokeWidth="1.5" />
                <circle cx="320" cy="240" r="100" fill="none" stroke="#0b6b69" strokeOpacity="0.45" strokeWidth="1.75" />
                <circle cx="320" cy="240" r="70" fill="none" stroke="#0b6b69" strokeWidth="2" />
                <path d="M 320 70 A 170 170 0 0 1 466 155" fill="none" stroke="#f2765b" strokeWidth="6" strokeLinecap="round" />
                <circle cx="466" cy="155" r="7" fill="#f2765b" />
                <circle cx="320" cy="240" r="6" fill="#0b6b69" />
                <g stroke="#0b6b69" strokeOpacity="0.5" strokeWidth="1.5">
                  <line x1="320" y1="40" x2="320" y2="60" />
                  <line x1="320" y1="420" x2="320" y2="440" />
                  <line x1="120" y1="240" x2="140" y2="240" />
                  <line x1="500" y1="240" x2="520" y2="240" />
                </g>
              </svg>
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
            <div className="container" style={{ padding: 0, marginTop: 20 }}>
            </div>
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
              belirtiyoruz.
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
          </div>
        </section>

        {/* GÜVENLİK */}
        <section className="section" id="guvenlik">
          <div className="container">
            <span className="eyebrow">Güvenlik ve gizlilik ilkeleri</span>
            <h2 style={{ marginTop: 10 }}>Arka uç yetkilidir; arayüz asla tahmin etmez.</h2>
            <div className="grid grid-2" style={{ marginTop: 28 }}>
              {SECURITY.map((item) => (
                <div className="card card-content" key={item.title}>
                  <h4>{item.title}</h4>
                  <p className="muted" style={{ marginTop: 8 }}>
                    {item.body}
                  </p>
                </div>
              ))}
            </div>
            <p className="muted" style={{ marginTop: 20 }}>
              Ayrıntılar için{' '}
              <Link href="/gizlilik" style={{ color: 'var(--fg)', fontWeight: 600 }}>
                Gizlilik Politikası
              </Link>
              ’nı okuyabilirsin.
            </p>
          </div>
        </section>

        {/* SSS */}
        <section className="section" style={{ background: 'var(--surface)' }} id="sss">
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
        <section className="section">
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
                  Lisans kodumu etkinleştir
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
