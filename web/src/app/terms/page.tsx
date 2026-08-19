import Link from 'next/link';
import { LegalDocument, type LegalSection } from '@/components/LegalDocument';
import { OPERATORS } from '@/lib/contact';

/**
 * The terms in force.
 *
 * Two things drive the shape of this document. First, the service writes to a calendar a student
 * relies on to be somewhere at a given hour, so the limits of what it can promise — source
 * accuracy, delay, deliberate holds on suspicious data — have to be stated plainly rather than
 * buried. Second, licence codes are obtained outside this platform, so the payment sections say
 * what is and is not handled here instead of inventing a checkout that does not exist.
 */
const P: React.CSSProperties = { marginTop: 12, maxWidth: '72ch' };
const LIST: React.CSSProperties = { marginTop: 12, maxWidth: '72ch', paddingLeft: 20 };
const STRONG: React.CSSProperties = { color: 'var(--fg)', fontWeight: 600 };

const SECTIONS: LegalSection[] = [
  {
    id: 'taraflar',
    title: 'Taraflar ve kapsam',
    content: (
      <>
        <p style={P}>
          Bu koşullar, Sirkadiyen’i işleten <strong style={STRONG}>Halil Semih Şen</strong> ve{' '}
          <strong style={STRONG}>Abdullah Ceylan</strong> (“Sirkadiyen”, “biz”) ile hizmeti kullanan
          kişi (“sen”, “kullanıcı”) arasındaki sözleşmedir.
        </p>
        <p style={P}>
          Google ile giriş yaparak bu koşulları ve{' '}
          <Link href="/privacy" style={STRONG}>
            Gizlilik Politikası
          </Link>
          ’nı kabul etmiş olursun. Kabul etmiyorsan hizmeti kullanma.
        </p>
      </>
    ),
  },
  {
    id: 'hizmet',
    title: 'Hizmetin tanımı',
    content: (
      <>
        <p style={P}>
          Sirkadiyen, İstanbul Tıp Fakültesi’nin yayımladığı ders programlarını okur, senin
          bildirdiğin akademik profile göre kişiselleştirir ve Google Takvim’inde{' '}
          <strong style={STRONG}>yalnızca kendisinin oluşturduğu ayrı bir takvimde</strong> güncel
          tutar.
        </p>
        <p style={P}>
          Sirkadiyen bir eğitim, danışmanlık veya resmî kayıt hizmeti değildir. Fakülte, üniversite
          veya herhangi bir resmî kurumla bağlantılı, onlar tarafından yetkilendirilmiş ya da
          onaylanmış değildir. Ders içeriği üretmez, akademik değerlendirme yapmaz, devamsızlık veya
          sınav kaydı tutmaz.
        </p>
      </>
    ),
  },
  {
    id: 'dogruluk',
    title: 'Doğruluk sınırı — bunu okuman önemli',
    content: (
      <>
        <p style={P}>
          <strong style={STRONG}>
            Takvimindeki bilginin doğruluğu, fakültenin yayımladığı belgenin doğruluğuna bağlıdır.
          </strong>{' '}
          Kaynakta hata, eksik ya da belirsizlik varsa bu takvimine de yansıyabilir.
        </p>
        <p style={P}>
          Bu nedenle <strong style={STRONG}>Sirkadiyen resmî kaynağın yerine geçmez</strong>. Bir
          dersin, sınavın veya uygulamanın yerini ve saatini kesin olarak öğrenmen gereken
          durumlarda fakültenin kendi duyurusunu esas al. Takvimde göründüğü için bir yükümlülüğü
          kaçırdığını veya yanlış yerde bulunduğunu ileri süremezsin.
        </p>
        <p style={P}>
          Kaynakta beklenmedik bir değişiklik saptandığında (örneğin çok sayıda dersin aynı anda
          silinmiş görünmesi) güncellemeyi <strong style={STRONG}>bilerek durdurur</strong> ve insan
          incelemesine alırız. Bu, hatalı bir programın toplu şekilde takvimlere yazılmasını
          önlemek içindir ve hizmetin normal işleyişinin parçasıdır.
        </p>
      </>
    ),
  },
  {
    id: 'hesap',
    title: 'Hesap ve etkinleştirme',
    content: (
      <>
        <ul style={LIST}>
          <li>Hesap yalnızca Google ile açılır; ayrı bir parola oluşturulmaz.</li>
          <li>
            Hesabın kullanıma açılması için geçerli bir lisans kodunu etkinleştirmen ya da yönetici
            tarafından kayıt altına alınan bir etkinleştirme yapılması gerekir.
          </li>
          <li>
            Hesabını yalnızca kendi adına kullanabilirsin. Bir kişi bir hesap tutar; hesabını
            başkasına devredemez, ödünç veremezsin.
          </li>
          <li>
            Google hesabının güvenliği senin sorumluluğundadır. Hesabına izinsiz erişildiğini
            düşünüyorsan bize bildir.
          </li>
        </ul>
      </>
    ),
  },
  {
    id: 'lisans',
    title: 'Lisans koşulları',
    content: (
      <>
        <ul style={LIST}>
          <li>
            Bir lisans kodu <strong style={STRONG}>tek kullanımlıktır</strong>; ilk etkinleştirmeden
            sonra başka bir hesapta çalışmaz.
          </li>
          <li>
            Lisans kişiye özeldir; paylaşılamaz, satılamaz, devredilemez. Kodunu paylaşmandan doğan
            sonuçlardan sen sorumlusun.
          </li>
          <li>Bir lisansın süresi olabilir. Süre dolduğunda eşitleme durur.</li>
          <li>
            Kötüye kullanım, kod paylaşımı veya gerçeğe aykırı bilgi girişi hâlinde lisans iptal
            edilebilir. İptal, gerekçesiyle birlikte kayıt altına alınır.
          </li>
          <li>
            Lisansın sona ermesi veya iptali eşitlemeyi durdurur;{' '}
            <strong style={STRONG}>takvimine daha önce yazılmış etkinlikler silinmez</strong>, ancak
            güncellenmez.
          </li>
        </ul>
      </>
    ),
  },
  {
    id: 'ucret',
    title: 'Ücret ve ödeme',
    content: (
      <>
        <p style={P}>
          Bu platform üzerinden ödeme alınmaz. Sitede kart, banka veya ödeme bilgisi toplanmaz;
          lisans kodları platform dışında dağıtılır ve varsa bedeli platform dışında tahsil edilir.
        </p>
        <p style={P}>
          Kodun edinimine ilişkin ücret, iade ve benzeri talepler, kodu edindiğin kanal ile aranızdadır.
          Bize ulaştırdığın bu tür talepleri, kodun ilgili olduğu hesap ve lisans kaydı üzerinden
          değerlendirir, sonucu bildiririz. Tüketici mevzuatından doğan haklarınız saklıdır.
        </p>
        <p style={P}>
          Sirkadiyen’in sana ödeme yapmanı isteyen bir sayfası veya e-postası yoktur; böyle bir
          taleple karşılaşırsan bize bildir.
        </p>
      </>
    ),
  },
  {
    id: 'yukumlulukler',
    title: 'Kullanıcı yükümlülükleri',
    content: (
      <>
        <p style={P}>Hizmeti kullanırken:</p>
        <ul style={LIST}>
          <li>
            Akademik profilini (dönem, program dili, grup, öğrenci numarası){' '}
            <strong style={STRONG}>doğru ve güncel</strong> tutarsın. Yanlış profil, yanlış dersleri
            görmene yol açar ve bunun sonucu sana aittir.
          </li>
          <li>Başkasının bilgileriyle veya sahte bilgiyle hesap açmazsın.</li>
          <li>
            Hizmete otomatik araçlarla aşırı istek göndermez, güvenlik önlemlerini aşmaya
            çalışmaz, sistemi tersine mühendisliğe tabi tutmazsın.
          </li>
          <li>Hizmeti hukuka aykırı bir amaçla veya başkalarının haklarını ihlal ederek kullanmazsın.</li>
        </ul>
      </>
    ),
  },
  {
    id: 'takvim',
    title: 'Takvimin nasıl yönetilir',
    content: (
      <>
        <ul style={LIST}>
          <li>
            Google hesabında <em>Sirkadiyen</em> adlı ayrı bir takvim oluşturulur. Diğer
            takvimlerine dokunulmaz.
          </li>
          <li>
            Bu takvimdeki etkinlikleri <strong style={STRONG}>elle değiştirirsen veya silersen</strong>,
            bir sonraki eşitlemede yayımlanmış programa göre geri getirilebilir ya da yeniden
            düzeltilebilir. Kişisel notlarını bu takvimde değil kendi takvimlerinde tutmanı öneririz.
          </li>
          <li>
            Dönemini ya da grubunu ilgilendiren yönetici duyuruları bu takvime etkinlik olarak
            eklenebilir.
          </li>
          <li>
            Profilini değiştirirsen artık sana ait olmayan etkinlikler takviminden kaldırılabilir,
            yenileri yazılır.
          </li>
          <li>
            Google iznini geri alırsan eşitleme durur; takvim ve içindeki etkinlikler Google
            hesabında kalır ve dilediğinde silebilirsin.
          </li>
        </ul>
      </>
    ),
  },
  {
    id: 'kesinti',
    title: 'Süreklilik, gecikme ve bakım',
    content: (
      <>
        <p style={P}>
          Eşitleme <strong style={STRONG}>anlık değildir</strong>. Kaynak belgedeki bir değişikliğin
          takvimine yansıması, kaynağın kontrol edilme sıklığına ve inceleme gerekip gerekmediğine
          bağlı olarak zaman alabilir.
        </p>
        <p style={P}>
          Hizmet; bakım, altyapı sorunları, Google servislerindeki kesintiler veya fakültenin
          kaynak yayınını değiştirmesi nedeniyle geçici olarak durabilir. Planlı bakımları önceden
          duyurmaya çalışırız. Hizmeti kesintisiz sunma taahhüdümüz yoktur.
        </p>
        <p style={P}>
          Özellikleri geliştirebilir, değiştirebilir veya kaldırabiliriz. Hizmetin tamamını
          sonlandırmaya karar verirsek makul bir süre önce bildiririz.
        </p>
      </>
    ),
  },
  {
    id: 'askiya-alma',
    title: 'Askıya alma ve sona erme',
    content: (
      <>
        <p style={P}>
          Bu koşulların ihlali, kod paylaşımı, sahte bilgi veya sistemin güvenliğini tehdit eden
          kullanım hâlinde hesabını askıya alabilir ya da kapatabiliriz. İşlem gerekçesiyle birlikte
          kayıt altına alınır ve sana bildirilir.
        </p>
        <p style={P}>
          Sen de dilediğin an hesabının silinmesini isteyebilir, Google iznini geri alarak eşitlemeyi
          durdurabilirsin. Hesap silme sürecinin ayrıntıları{' '}
          <Link href="/privacy#hesap-silme" style={STRONG}>
            Gizlilik Politikası’nda
          </Link>{' '}
          açıklanmıştır.
        </p>
      </>
    ),
  },
  {
    id: 'sorumluluk',
    title: 'Sorumluluğun sınırı',
    content: (
      <>
        <p style={P}>
          Hizmet “olduğu gibi” sunulur. Kaynak belgelerdeki hatalardan, fakültenin programı
          değiştirmesinden, Google servislerindeki kesinti veya değişikliklerden, senin girdiğin
          hatalı profil bilgisinden ya da internet erişiminden kaynaklanan sonuçlardan sorumlu
          değiliz.
        </p>
        <p style={P}>
          Bir derse, sınava veya uygulamaya katılmakla ilgili nihai sorumluluk sana aittir;
          takvimdeki bir kaydın eksikliği veya hatası bu sorumluluğu ortadan kaldırmaz.
        </p>
        <p style={P}>
          Kastımızdan ve ağır ihmalimizden doğan sorumluluğumuz ile tüketici mevzuatından ve emredici
          hukuk kurallarından doğan sorumluluğumuz saklıdır; bu bölüm onları sınırlamaz.
        </p>
      </>
    ),
  },
  {
    id: 'fikri-mulkiyet',
    title: 'Fikri mülkiyet',
    content: (
      <p style={P}>
        Sirkadiyen’in adı, arayüzü, tasarımı ve yazılımı bize aittir. Ders programlarının içeriği
        fakülteye aittir; biz onu yalnızca senin kullanımın için işleriz. Hizmeti kullanman sana
        yazılım üzerinde bir mülkiyet hakkı vermez.
      </p>
    ),
  },
  {
    id: 'kisisel-veriler',
    title: 'Kişisel veriler',
    content: (
      <p style={P}>
        Hangi verileri neden işlediğimiz, ne kadar sakladığımız ve haklarını nasıl kullanacağın{' '}
        <Link href="/privacy" style={STRONG}>
          Gizlilik Politikası’nda
        </Link>{' '}
        açıklanır. O metin bu koşulların ayrılmaz parçasıdır.
      </p>
    ),
  },
  {
    id: 'degisiklikler',
    title: 'Koşullardaki değişiklikler',
    content: (
      <p style={P}>
        Bu koşulları güncelleyebiliriz. Esaslı bir değişiklikte, yürürlüğe girmeden önce e-posta ile
        veya uygulama içinde bilgilendirme yapılır. Değişikliği kabul etmiyorsan hesabını
        kapatabilirsin; bildirimden sonra hizmeti kullanmaya devam etmen yeni koşulların kabulü
        anlamına gelir.
      </p>
    ),
  },
  {
    id: 'uygulanacak-hukuk',
    title: 'Uygulanacak hukuk ve uyuşmazlıklar',
    content: (
      <p style={P}>
        Bu koşullara Türk hukuku uygulanır. Uyuşmazlıklarda İstanbul (Çağlayan) mahkemeleri ve icra
        daireleri yetkilidir. Tüketici sıfatını taşıyorsan, parasal sınırlara göre tüketici hakem
        heyetlerine veya tüketici mahkemelerine başvurma hakkın saklıdır.
      </p>
    ),
  },
  {
    id: 'iletisim',
    title: 'İletişim',
    content: (
      <>
        <p style={P}>Sorular ve talepler için:</p>
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
          Dilersen{' '}
          <Link href="/iletisim" style={STRONG}>
            iletişim sayfasını
          </Link>{' '}
          da kullanabilirsin.
        </p>
      </>
    ),
  },
];

export default function TermsPage() {
  return (
    <LegalDocument
      title="Kullanım Koşulları"
      updated="19 Ağustos 2026"
      effective="19 Ağustos 2026"
      sections={SECTIONS}
    />
  );
}
