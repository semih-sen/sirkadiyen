import Link from 'next/link';
import { LegalDocument, type LegalSection } from '@/components/LegalDocument';
import { CONTACT_EMAILS, OPERATORS } from '@/lib/contact';

/**
 * The privacy notice in force (KVKK m.10 aydınlatma yükümlülüğü).
 *
 * Every statement here is written against what the system actually does, not against what a
 * template says a service usually does — the OAuth scopes, the fields that are stored, the
 * encryption at rest, the masked IP, the Google Fonts request the layout makes. A privacy notice
 * that overstates or understates any of those is worse than none: it is a false statement to the
 * person deciding whether to hand over their calendar.
 */
const P: React.CSSProperties = { marginTop: 12, maxWidth: '72ch' };
const LIST: React.CSSProperties = { marginTop: 12, maxWidth: '72ch', paddingLeft: 20 };
const STRONG: React.CSSProperties = { color: 'var(--fg)', fontWeight: 600 };

const SECTIONS: LegalSection[] = [
  {
    id: 'ozet',
    title: 'Kısaca',
    content: (
      <>
        <p style={P}>
          Sirkadiyen, fakültenin yayımladığı ders programını okur, akademik profiline göre
          kişiselleştirir ve Google Takvim’inde <strong style={STRONG}>yalnızca kendisinin
          oluşturduğu ayrı bir takvimde</strong> güncel tutar. Bunu yapabilmek için kimliğine,
          akademik profiline ve takvim yazma iznine ihtiyaç duyar.
        </p>
        <ul style={LIST}>
          <li>Mevcut takvimlerindeki etkinlikler okunmaz ve değiştirilmez.</li>
          <li>Verilerin reklam amacıyla kullanılmaz, satılmaz, pazarlama için paylaşılmaz.</li>
          <li>Google hesabına ait şifreni hiçbir zaman görmeyiz; giriş Google tarafında yapılır.</li>
          <li>Sunucular Türkiye’de bulunur.</li>
          <li>
            Bu özet bilgilendirme amaçlıdır; bağlayıcı olan aşağıdaki tam metindir.
          </li>
        </ul>
      </>
    ),
  },
  {
    id: 'veri-sorumlusu',
    title: 'Veri sorumlusu',
    content: (
      <>
        <p style={P}>
          6698 sayılı Kişisel Verilerin Korunması Kanunu (KVKK) anlamında veri sorumluları,
          Sirkadiyen’i birlikte işleten <strong style={STRONG}>Halil Semih Şen</strong> ve{' '}
          <strong style={STRONG}>Abdullah Ceylan</strong>’dır. Bu metinde “biz” denildiğinde
          kastedilen budur.
        </p>
        <table className="legal-table">
          <thead>
            <tr>
              <th>Veri sorumlusu</th>
              <th>E-posta</th>
              <th>Telefon</th>
            </tr>
          </thead>
          <tbody>
            {OPERATORS.map((operator) => (
              <tr key={operator.email}>
                <td>{operator.name}</td>
                <td>
                  <a href={`mailto:${operator.email}`} style={STRONG}>
                    {operator.email}
                  </a>
                </td>
                <td>
                  <a href={`tel:+${operator.phoneDigits}`} style={STRONG}>
                    {operator.phone}
                  </a>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
        <p style={P}>
          Yazılı başvuru için posta adresi, talep hâlinde yukarıdaki adreslerden paylaşılır.
        </p>
      </>
    ),
  },
  {
    id: 'islenen-veriler',
    title: 'İşlediğimiz kişisel veriler',
    content: (
      <>
        <table className="legal-table">
          <thead>
            <tr>
              <th>Veri</th>
              <th>Neleri kapsar</th>
              <th>Nereden gelir</th>
            </tr>
          </thead>
          <tbody>
            <tr>
              <td>Kimlik ve iletişim</td>
              <td>Ad-soyad, e-posta adresi, Google hesap kimliği (sayısal), e-postanın Google
                tarafından doğrulanmış olup olmadığı</td>
              <td>Google ile giriş</td>
            </tr>
            <tr>
              <td>Akademik profil</td>
              <td>Akademik yıl, dönem, program dili, grup/alt grup bilgileri ve öğrenci numarası</td>
              <td>Senin girişin</td>
            </tr>
            <tr>
              <td>Lisans kaydı</td>
              <td>Lisansın durumu, etkinleştirme ve varsa iptal tarihi, iptal gerekçesi.{' '}
                <strong style={STRONG}>Lisans kodunun kendisi saklanmaz</strong>; yalnızca geri
                döndürülemez bir özeti tutulur.</td>
              <td>Lisans etkinleştirme</td>
            </tr>
            <tr>
              <td>Google Takvim bağlantısı</td>
              <td>Verdiğin iznin kapsamı, bağlantının durumu, oluşturduğumuz takvimin kimliği ve
                yenileme belirteci (şifrelenmiş olarak)</td>
              <td>Takvim yetkilendirme adımı</td>
            </tr>
            <tr>
              <td>Takvim eşleme kayıtları</td>
              <td>Hangi dersin senin takviminde hangi etkinliğe karşılık geldiği, son yazılan
                içeriğin özeti, yazma/güncelleme zamanları</td>
              <td>Eşitleme işlemleri</td>
            </tr>
            <tr>
              <td>Görünüm tercihleri</td>
              <td>Anabilim dallarına verdiğin takvim renkleri</td>
              <td>Senin tercihin</td>
            </tr>
            <tr>
              <td>Giriş ve işlem kayıtları</td>
              <td>Giriş zamanı, tarayıcı bilgisi (user agent), IP adresi ve hesabınla ilgili
                yönetici işlemleri</td>
              <td>Sistem kayıtları</td>
            </tr>
          </tbody>
        </table>
        <p style={P}>
          Sağlık verisi, ders notu, sınav sonucu, akademik başarı bilgisi veya konum verisi
          işlemiyoruz. Ödeme bilgisi işlemiyoruz: lisans kodunun edinimi bu platform üzerinden
          yapılmaz, sitede kart veya banka bilgisi toplanmaz.
        </p>
      </>
    ),
  },
  {
    id: 'amac-dayanak',
    title: 'İşleme amaçları ve hukuki sebepler',
    content: (
      <>
        <table className="legal-table">
          <thead>
            <tr>
              <th>Amaç</th>
              <th>Hukuki sebep (KVKK m.5)</th>
            </tr>
          </thead>
          <tbody>
            <tr>
              <td>Hesabını oluşturmak, kimliğini doğrulamak, oturumunu sürdürmek</td>
              <td>Sözleşmenin kurulması ve ifası (m.5/2-c)</td>
            </tr>
            <tr>
              <td>Lisansı etkinleştirmek, aynı kodun iki kez kullanılmasını engellemek</td>
              <td>Sözleşmenin ifası (m.5/2-c); hakkın tesisi ve korunması (m.5/2-e)</td>
            </tr>
            <tr>
              <td>Programı akademik profiline göre kişiselleştirmek</td>
              <td>Sözleşmenin ifası (m.5/2-c)</td>
            </tr>
            <tr>
              <td>Takvimini oluşturmak ve güncel tutmak</td>
              <td>Sözleşmenin ifası (m.5/2-c) — Google izni ayrıca senin onayınla verilir</td>
            </tr>
            <tr>
              <td>Güvenlik, kötüye kullanım ve lisans paylaşımının tespiti</td>
              <td>Meşru menfaat (m.5/2-f)</td>
            </tr>
            <tr>
              <td>Destek taleplerini yanıtlamak, hataları gidermek</td>
              <td>Meşru menfaat (m.5/2-f); sözleşmenin ifası (m.5/2-c)</td>
            </tr>
            <tr>
              <td>Hukuki yükümlülüklere uymak, resmî taleplere yanıt vermek</td>
              <td>Hukuki yükümlülük (m.5/2-ç)</td>
            </tr>
          </tbody>
        </table>
        <p style={P}>
          Kişisel verilerini profil çıkarma, davranışsal reklam, otomatik karar verme veya
          herhangi bir puanlama amacıyla kullanmıyoruz.
        </p>
      </>
    ),
  },
  {
    id: 'google-verileri',
    title: 'Google hesabı ve takvim verilerinin kullanımı',
    content: (
      <>
        <p style={P}>
          Google ile giriş yaptığında Google bize yalnızca adını, e-posta adresini, hesap kimliğini
          ve e-postanın doğrulanmış olup olmadığını bildirir. Şifreni görmeyiz ve saklamayız.
        </p>
        <p style={P}>
          Takvim izni ayrı bir adımda, senin onayınla istenir ve iki yetkiden oluşur:
        </p>
        <ul style={LIST}>
          <li>
            <strong style={STRONG}>Uygulamanın kendi oluşturduğu takvim</strong>{' '}
            (<span className="mono">calendar.app.created</span>): Sirkadiyen yalnızca kendi
            oluşturduğu takvimi görebilir ve o takvime yazabilir. Kişisel takvimin, iş takvimin veya
            başka uygulamaların takvimleri bu yetkinin kapsamı dışındadır — etkinliklerini okuyamaz,
            değiştiremez, silemeyiz.
          </li>
          <li>
            <strong style={STRONG}>Takvim listesini okuma</strong>{' '}
            (<span className="mono">calendar.calendarlist.readonly</span>): yalnızca takvimlerinin{' '}
            <em>listesini</em> (ad ve kimlik gibi künye bilgilerini) görmemizi sağlar; içlerindeki
            etkinliklere erişim vermez. Buna, Google’da takvim oluşturulduktan sonra bizim tarafta
            bir kesinti olursa aynı takvimi tekrar bulup ikinci bir takvim oluşturmamak için
            ihtiyaç duyulur.
          </li>
        </ul>
        <p style={P}>
          <strong style={STRONG}>Sınırlı Kullanım taahhüdü.</strong> Sirkadiyen’in Google API’leri
          üzerinden aldığı bilgileri kullanması ve başkasına aktarması, Google API Hizmetleri
          Kullanıcı Verileri Politikası’na ve bu politikanın Sınırlı Kullanım (Limited Use)
          gerekliliklerine uygundur. Bu kapsamda Google kullanıcı verilerini reklam amacıyla
          kullanmayız, satmayız ve; (i) senin açık onayınla, (ii) güvenlik ihlali incelemesi veya
          kötüye kullanımın araştırılması, (iii) yürürlükteki hukukun gerektirdiği hâller dışında
          hiçbir insanın okumasına açmayız.
        </p>
        <p style={P}>
          İzni istediğin an Google Hesap ayarlarından geri alabilirsin. İzni geri aldığında
          eşitleme durur; daha önce yazılmış etkinlikler takviminde kalır ve o takvimi Google
          üzerinden kendin silebilirsin.
        </p>
      </>
    ),
  },
  {
    id: 'takvimine-yazilanlar',
    title: 'Takvimine ne yazılır',
    content: (
      <>
        <p style={P}>
          Oluşturduğumuz takvime yalnızca akademik profiline uyan ders ve etkinlik kayıtları
          yazılır: dersin adı, tarihi, saati, yeri, varsa öğretim üyesi ve anabilim dalı bilgisi ile
          kaynağına dair künye. Bunların kaynağı fakültenin yayımladığı programlardır.
        </p>
        <p style={P}>
          Yöneticiler, dönemini veya grubunu ilgilendiren bir duyuruyu bu takvime etkinlik olarak
          ekleyebilir. Kimlere gönderildiği ve kim tarafından onaylandığı kayıt altına alınır.
        </p>
      </>
    ),
  },
  {
    id: 'aktarim',
    title: 'Verilerin paylaşımı ve yurt dışına aktarım',
    content: (
      <>
        <p style={P}>
          Verilerini satmıyoruz, kiralamıyoruz ve pazarlama amacıyla üçüncü taraflarla
          paylaşmıyoruz. Sunucularımız <strong style={STRONG}>Türkiye’de</strong> bulunur; hesap ve
          program verilerin burada saklanır.
        </p>
        <p style={P}>
          Hizmetin yapısı gereği aşağıdaki aktarımlar gerçekleşir:
        </p>
        <ul style={LIST}>
          <li>
            <strong style={STRONG}>Google (Google Ireland Ltd. / Google LLC).</strong> Girişin
            Google tarafında doğrulanır ve takvim etkinlikleri senin Google hesabına yazılır. Bu,
            KVKK m.9 anlamında yurt dışına aktarımdır ve hizmetin verilebilmesi için zorunludur;
            takvim izni verilmediği sürece takvim verisi aktarılmaz.
          </li>
          <li>
            <strong style={STRONG}>Google Fonts.</strong> Sitenin yazı tipleri Google’ın sunucularından
            yüklenir; bu istek sırasında IP adresin ve tarayıcı bilgin Google’a ulaşır. Bunun için
            çerez kullanılmaz.
          </li>
          <li>
            <strong style={STRONG}>Yetkili kamu kurumları.</strong> Yalnızca hukuken zorunlu olduğu
            ölçüde ve talebin dayanağını inceleyerek.
          </li>
        </ul>
        <p style={P}>
          Fakültenin yayımladığı program belgeleri yalnızca okunur; o sistemlere senin hakkında
          hiçbir veri gönderilmez.
        </p>
      </>
    ),
  },
  {
    id: 'saklama',
    title: 'Saklama',
    content: (
      <>
        <p style={P}>
          Kişisel verilerini, işlendikleri amaç için gerekli olduğu sürece saklarız. Bir verinin ne
          kadar tutulacağını şu ölçütlere göre belirleriz:
        </p>
        <ul style={LIST}>
          <li>Hesap, akademik profil ve takvim bağlantın: hesabın açık olduğu sürece.</li>
          <li>
            Lisans kayıtları: aynı kodun ikinci kez kullanılmasını engellemek ve hakkı ispatlamak
            için gerekli olduğu sürece. Kodun düz metni hiçbir zaman saklanmaz.
          </li>
          <li>
            Giriş ve işlem kayıtları: güvenlik incelemesi ve kötüye kullanımın tespiti için gerekli
            olduğu sürece.
          </li>
          <li>
            Destek yazışmaları: talebin sonuçlanması ve benzeri sorunların izlenmesi için gerekli
            olduğu sürece.
          </li>
        </ul>
        <p style={P}>
          Bu sürelerin üzerine, ilgili mevzuattaki saklama yükümlülükleri ile hak arama
          (zamanaşımı) süreleri eklenir. Saklamayı gerektiren sebep ortadan kalktığında veriler
          silinir, yok edilir veya kimliğe bağlanamayacak hâle getirilir. Silme talebinde
          bulunursan bu, talebini aldığımızda başlar.
        </p>
      </>
    ),
  },
  {
    id: 'guvenlik',
    title: 'Aldığımız güvenlik önlemleri',
    content: (
      <ul style={LIST}>
        <li>Tüm trafik şifreli (HTTPS) taşınır.</li>
        <li>
          Google yenileme belirtecin veritabanında şifrelenmiş olarak tutulur; hiçbir yönetim
          ekranında görüntülenmez ve kayıtlara yazılmaz.
        </li>
        <li>
          Lisans kodunun düz metni saklanmaz; yalnızca geri döndürülemez bir özeti tutulur.
        </li>
        <li>
          IP adresin kayıtlarda varsayılan olarak maskeli görünür. Tam adresin şifreli tutulur ve
          görüntülenmesi ayrı yetki, gerekçe ve ayrı bir denetim kaydı gerektirir.
        </li>
        <li>
          Yönetici işlemleri rol bazlı yetkiyle sınırlıdır; kim, ne zaman, hangi gerekçeyle yaptı
          bilgisi kalıcı olarak kaydedilir.
        </li>
        <li>
          Oturum çerezi tarayıcı tarafından okunamayacak biçimde (HttpOnly) ayarlanır ve istekler
          siteler arası istek sahteciliğine karşı korunur.
        </li>
        <li>Giriş ve lisans uçlarında hız sınırlaması uygulanır.</li>
      </ul>
    ),
  },
  {
    id: 'cerezler',
    title: 'Çerezler',
    content: (
      <>
        <p style={P}>
          Sirkadiyen reklam, ölçümleme veya izleme çerezi kullanmaz. Analitik yazılım, piksel veya
          üçüncü taraf izleyici yoktur. Kullanılan iki çerez zorunludur:
        </p>
        <table className="legal-table">
          <thead>
            <tr>
              <th>Çerez</th>
              <th>Amaç</th>
              <th>Süre</th>
            </tr>
          </thead>
          <tbody>
            <tr>
              <td className="mono">__Host-Sirkadiyen.Session</td>
              <td>Oturumunun açık kalması</td>
              <td>30 gün (kullandıkça yenilenir)</td>
            </tr>
            <tr>
              <td className="mono">__Host-Sirkadiyen.Antiforgery</td>
              <td>Form ve işlem güvenliği (CSRF koruması)</td>
              <td>Oturum boyunca</td>
            </tr>
          </tbody>
        </table>
        <p style={P}>
          Bu çerezler hizmetin çalışması için zorunlu olduğundan onaya tabi değildir; tarayıcından
          engellersen giriş yapamazsın.
        </p>
      </>
    ),
  },
  {
    id: 'haklarin',
    title: 'KVKK kapsamındaki haklarınız',
    content: (
      <>
        <p style={P}>KVKK m.11 uyarınca:</p>
        <ul style={LIST}>
          <li>Kişisel verinin işlenip işlenmediğini öğrenme, işlenmişse buna ilişkin bilgi talep etme,</li>
          <li>İşlenme amacını ve amacına uygun kullanılıp kullanılmadığını öğrenme,</li>
          <li>Yurt içinde veya yurt dışında aktarıldığı üçüncü kişileri bilme,</li>
          <li>Eksik veya yanlış işlenmişse düzeltilmesini isteme,</li>
          <li>Şartları oluştuğunda silinmesini veya yok edilmesini isteme,</li>
          <li>Düzeltme, silme ve yok etme işlemlerinin aktarıldığı üçüncü kişilere bildirilmesini isteme,</li>
          <li>Yalnızca otomatik sistemlerle analiz edilmesi suretiyle aleyhine bir sonuç ortaya
            çıkmasına itiraz etme,</li>
          <li>Kanuna aykırı işleme nedeniyle zarara uğraman hâlinde zararın giderilmesini talep etme
            haklarına sahipsin.</li>
        </ul>
        <p style={P}>
          Başvurunu hesabında kayıtlı e-posta adresinden{' '}
          <a href={`mailto:${CONTACT_EMAILS}`} style={STRONG}>
            veri sorumlularına
          </a>{' '}
          ya da{' '}
          <Link href="/iletisim?kategori=gizlilik" style={STRONG}>
            iletişim formu
          </Link>{' '}
          üzerinden iletebilirsin. Başvurular en geç <strong style={STRONG}>30 gün</strong> içinde
          sonuçlandırılır. Talebini reddedersek veya yanıtı yetersiz bulursan Kişisel Verileri
          Koruma Kurulu’na şikâyette bulunma hakkın saklıdır.
        </p>
      </>
    ),
  },
  {
    id: 'hesap-silme',
    title: 'Hesabını silmek istersen',
    content: (
      <>
        <p style={P}>
          Silme talebini yukarıdaki adreslerden iletmen yeterlidir. Talebini KVKK’nın öngördüğü
          süre içinde sonuçlandırırız: hesabın, akademik profilin, takvim bağlantın ve eşleme
          kayıtların silinir, Google yenileme belirtecin yok edilir ve eşitleme durur.
        </p>
        <p style={P}>
          Google Takvim’indeki <em>Sirkadiyen</em> takvimi senin hesabında kalır — silindikten sonra
          ona erişimimiz olmadığı için oradaki etkinlikleri bizim kaldırmamız mümkün değildir. Takvimi
          Google Takvim ayarlarından kendin silebilirsin. İstersen silme talebinden önce bunu senin
          için temizleyebiliriz.
        </p>
      </>
    ),
  },
  {
    id: 'yas',
    title: 'Yaş sınırı',
    content: (
      <p style={P}>
        Hizmet, üniversite öğrencilerine yöneliktir. 18 yaşından küçüksen hesabı ancak veli veya
        vasinin bilgisi ve onayıyla kullanabilirsin.
      </p>
    ),
  },
  {
    id: 'degisiklikler',
    title: 'Bu metindeki değişiklikler',
    content: (
      <p style={P}>
        Metni güncellersek bu sayfadaki “son güncelleme” tarihi değişir. İşleme amaçlarını,
        saklama sürelerini veya paylaşımları esaslı biçimde etkileyen bir değişiklikte, değişiklik
        yürürlüğe girmeden önce e-posta ile veya uygulama içinde ayrıca bilgilendirme yapılır.
      </p>
    ),
  },
  {
    id: 'iletisim',
    title: 'İletişim',
    content: (
      <>
        <p style={P}>Gizlilikle ilgili her konu için doğrudan bize yazabilirsin:</p>
        <ul style={LIST}>
          {OPERATORS.map((operator) => (
            <li key={operator.email}>
              {operator.name} —{' '}
              <a href={`mailto:${operator.email}`} style={STRONG}>
                {operator.email}
              </a>{' '}
              ·{' '}
              <a href={`tel:+${operator.phoneDigits}`} style={STRONG}>
                {operator.phone}
              </a>
            </li>
          ))}
        </ul>
        <p style={P}>
          Diğer sorular için{' '}
          <Link href="/iletisim" style={STRONG}>
            iletişim sayfası
          </Link>
          .
        </p>
      </>
    ),
  },
];

export default function PrivacyPage() {
  return (
    <LegalDocument
      title="Gizlilik Politikası"
      updated="19 Ağustos 2026"
      effective="19 Ağustos 2026"
      sections={SECTIONS}
    />
  );
}
