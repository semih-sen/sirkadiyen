# Sirkadiyen — Web Arayüzü Tasarım Planı

**Belge türü:** Prototip tasarım brifi (PRD tarzı)
**Durum:** Taslak — inceleme bekliyor
**Kaynak analiz:** `E:\dev\projects\sirkadiyen` · AI_GUIDELINE.md, memory_bank (7 dosya), `web/` (Next.js App Router), `src/Sirkadiyen.Api` uç noktaları
**Tasarım sistemi:** Sirkadiyen-Wise-Inspired-Design-System
**Üretim koduna dokunulmadı.** Bu faz yalnızca okuma + Open Design içinde prototip üretimi.

---

## Prototype Delivery Boundary

This phase produces a standalone interactive HTML/CSS/JavaScript prototype
inside the Open Design project workspace.

It does not modify or generate files under the production Next.js application
at `E:\dev\projects\sirkadiyen\web`.

The prototype may simulate navigation, progress, filtering, forms, tables,
charts, confirmations, and state transitions, but it must not connect to
production APIs, Google authentication, Google Calendar, or production data.

After visual and interaction approval, the accepted design will be implemented
as a separate development phase using:

- Next.js App Router routes
- reusable React components
- typed backend contracts
- the existing authoritative onboarding-state mapping
- real API integrations
- automated frontend tests

Prototype HTML is a design reference, not production code and not a source to
copy verbatim into the Next.js application.

## 1. Amaç

Sirkadiyen'in tamamlanmış web deneyimini — public sayfalar, kimlik/onboarding, öğrenci paneli ve
SuperAdmin uygulaması — Open Design içinde yüksek çözünürlüklü, responsive ve etkileşimli bir
prototip olarak kurmak; üretim koduna geçmeden önce bilgi mimarisini, durum modelini ve yüksek
riskli işlem kalıplarını gözle doğrulanabilir hale getirmek.

Prototipin tek bir tasarım tezi var:

> **Arka uç otoritedir. Arayüz hiçbir zaman onaylanmamış bir başarıyı göstermez.**

Bu tez görsel bir slogan değil, ekranların yapısını belirleyen kural: her durum bir arka uç
durumundan türer, hiçbir ekran "muhtemelen tamamlandı" demez, ve her yıkıcı işlem gerekçe + güçlü
onay + denetim kaydı üçlüsünden geçer.

---

## 2. Mevcut ürün analizi

### 2.1 Sistem sınırları (AI_GUIDELINE §5 — tasarımı doğrudan bağlar)

| Katman | Sorumluluk | Arayüz için anlamı |
| --- | --- | --- |
| Frontend (Next.js) | Giriş, onboarding, lisans, profil, senkron durumu, admin arayüzleri | **Program ayrıştırmaz, Google Calendar'ı doğrudan değiştirmez** |
| ASP.NET Core API | Kimlik, yetki, lisans, profil, OAuth token yaşam döngüsü, denetim | Tüm yetki ve durum kararları burada |
| .NET Worker | Kaynak yoklama, snapshot, parse, revizyon, diff, senkron işleri, mutabakat | Takvim yazımını yapan taraf **budur** |
| Python parser | Yalnızca snapshot → kanonik aday kayıt | Arayüzde hiç görünmez, yalnızca uyarı/metrik olarak |

**Tasarım sonucu:** Öğrenci "Takvimime ekle" demez; **"İlk senkronizasyonu başlat"** der ve worker'ın
ilerlemesini izler. Admin "Etkinlik oluştur" düğmesine bastığında etkinlik oluşmaz; **kuyruğa alınır**
ve teslim ilerlemesi ayrı izlenir. Bu ayrımı arayüz dilinin her yerinde korumak zorunludur.

### 2.2 Bugün gerçekten var olan yetenekler

`memory_bank/progress.md` ve API uç noktalarından doğrulanan mevcut durum:

**Tamamlanmış (arayüz gerçek veriye bağlanabilir):**

- Google ile giriş (GIS ID token) → `POST /api/auth/google`, `GET /api/auth/me`, `GET /api/auth/csrf`
- Tek kullanımlık lisans kullanımı → `POST /api/licenses/redeem`
- Lisans oluşturma / iptal / manuel etkinleştirme → `POST /api/admin/licenses`, `/{id}/revoke`, `/api/admin/users/{id}/activate`
- Dinamik akademik profil → `GET /api/profile/options`, `GET|PUT /api/profile`
- Google Calendar yetkilendirme (popup code akışı) → `GET /api/calendar/authorization/options`, `GET|POST /api/calendar/authorization`
- İlk senkronizasyon başlatma + durum → `GET|POST /api/calendar/sync`
- Türetilmiş onboarding durumu → `GET /api/onboarding`
- Operasyonel dondurma (freeze) → `GET|POST /api/operations/freeze`
- Revizyon inceleme kuyruğu → `GET /api/revisions`, `/{id}`, `/{id}/approve`
- Diff listeleme ve serbest bırakma → `GET /api/diffs`, `/{id}`, `/{id}/release`
- Yönetsel doküman yükleme → `GET /api/sources/uploadable`, `POST /api/sources/{id}/document`, `GET .../uploads`

**Henüz backend'de olmayan, prototipte tasarlanacak ama "hedef arayüz" olarak işaretlenecek alanlar:**

- Kaynak durum panosu, snapshot inceleme, parser uyarı incelemesi, diff görüntüleyici
- Kullanıcı listesi / kullanıcı detay / kullanıcı senkron durumu (admin)
- Denetim kaydı görüntüleyici, oturum açma kayıtları
- Sağlık kontrolleri, metrikler, kuyruk derinliği, sunucu izleme
- Gelir / gider / kâr dağıtımı modülü (**tamamen yeni ürün alanı**)
- Toplu takvim etkinliği oluşturma ve tek kullanıcı uyarısı (**tamamen yeni ürün alanı**)
- Bildirimler, makale/podcast alanı ("Yakında")

> **TODO (karar gerekli):** Prototipte bu iki grubu görsel olarak ayırmak istiyor musun?
> Öneri: ayırmayalım — prototip hedef ürünü gösterir; bunun yerine her ekranın altındaki
> "Uygulama notu" şeridinde hangi API'nin var/yok olduğu yazsın. Böylece prototip hem tasarım
> hem de yol haritası belgesi olur.

### 2.3 Mevcut frontend'in durumu

`web/src` yalnızca 20 dosya. Tek bir `.card` sınıfı ve `globals.css` ile yürüyen, işlevsel ama
**tasarım sistemi olmayan** bir iskelet. `progress.md` bunu açıkça "Component system / design system —
[ ] yapılmadı" diye kaydediyor. Yani bu prototip boş alana değil, **bilinçli olarak ertelenmiş bir
boşluğa** giriyor.

Korunacak mevcut kararlar:

- `src/lib/onboarding.ts` içindeki durum → rota eşlemesi (aynen korunacak, rotalar bununla hizalanacak)
- SuperAdmin'in öğrenci onboarding'ine sokulmaması (ADR-067)
- `ActionRequired` durumunun takvim adımına yönlendirilmesi
- Arayüz dilinin Türkçe olması

### 2.4 Kısıtlar (pazarlık edilemez)

| # | Kısıt | Kaynak |
| --- | --- | --- |
| K1 | Yalnızca Google ile giriş; şifre alanı yok | AI_GUIDELINE §6 |
| K2 | Google erişim/yenileme token'ı, düz metin lisans kodu, Authorization başlığı, gizli değerler **hiçbir ekranda gösterilmez** | AI_GUIDELINE §15, §22 |
| K3 | Lisans kodu düz metin geçmişi tutulmaz; kod yalnızca oluşturma anındaki yanıtta görünür | AI_GUIDELINE §7 |
| K4 | Frontend yetki çıkarımı yapmaz; rol/lisans/durum arka uçtan gelir | AI_GUIDELINE §6, §16 |
| K5 | Arka uç onayı gelmeden başarı gösterilmez | AI_GUIDELINE §16 |
| K6 | Silme/dağıtım/askıya alma gibi işlemler gerekçe + sonuç özeti + güçlü onay + denetim ister | AI_GUIDELINE §19 |
| K7 | IP adresleri varsayılan olarak maskeli; maskeyi kaldırmak ayrı yetkili ve denetlenen bir eylem | Brief §13 |
| K8 | Sahte üretim metriği üretilmez; tüm sayısal veri "prototip verisi" olarak etiketlenir | Charter (anti-slop) + Brief §12 |
| K9 | Muhasebe/vergi/hukuki uyumluluk iddiası yok; hukuki metinler "hukuki inceleme gerekir" etiketli | Brief §2, §3, §9 |
| K10 | Zaman: UTC saklanır, `Europe/Istanbul` gösterilir; arayüzde saat dilimi açıkça yazılır | systemPatterns §18 |

---

## 3. Bilgi mimarisi

### 3.1 Rota haritası

```text
PUBLIC (oturum gerekmez)
  /                          landing.html                Ana sayfa
  /gizlilik                  privacy-policy.html         Gizlilik Politikası
  /kosullar                  terms-of-service.html       Kullanım Koşulları
  /iletisim                  contact.html                İletişim + destek formu

KİMLİK
  /giris                     sign-in.html                Google ile giriş (5 durum)

ONBOARDING (arka uç durumuna göre kapılanır, devam ettirilebilir)
  /kurulum                   onboarding.html             5 adımlı stepper
    ├─ 1 Lisans              (LicenseRequired)
    ├─ 2 Akademik profil     (ProfileRequired)
    ├─ 3 Takvim izni         (CalendarAuthorizationRequired)
    ├─ 4 İlk senkronizasyon  (ReadyForInitialSync → InitialSyncInProgress)
    └─ 5 Tamamlandı          (Active)
  /kurulum/senkronizasyon    sync-progress.html          Senkron ilerlemesi (10 durum)
  /kurulum/askida            → onboarding.html?state=Suspended

ÖĞRENCİ
  /panel                     student-dashboard.html      Öğrenci paneli

ADMIN (SuperAdmin, kalıcı sidebar, yoğun düzen)
  /yonetim                   admin-dashboard.html        Özet + trend + kritik aktivite
  /yonetim/finans            admin-finance.html          Gelir, gider, kâr dağıtımı
  /yonetim/kullanicilar      admin-users.html            Liste + detay
  /yonetim/toplu-etkinlik    admin-bulk-event.html       Toplu takvim etkinliği
  /yonetim/kullanici-uyarisi admin-user-warning.html     Tek kullanıcı takvim uyarısı
  /yonetim/kaynaklar         admin-sources.html          Kaynak + senkronizasyon operasyonları
  /yonetim/sunucu            admin-server.html           Sunucu ve servis izleme
  /yonetim/erisim-kayitlari  admin-access-logs.html      Kimlik doğrulama / erişim kayıtları

LAUNCHER
  index.html                 Tüm ekranların + her ekranın durum varyantlarının dizini
```

### 3.2 Üç yüzey, tek temel

| Yüzey | Yoğunluk | Karakter | Navigasyon |
| --- | --- | --- | --- |
| Public | Ferah, editoryal | Sakin, açıklayıcı, güven verici | Üst yatay nav + footer |
| Öğrenci | Ferah–orta | Açıklayıcı, tek görev odaklı, ilerlemeci açığa çıkarma | Sade üst bar, geri dönüş yok |
| Admin | Yoğun, operasyonel | Tarama, filtreleme, doğrulama, onaylama | Kalıcı sol sidebar + üst bağlam çubuğu |

Aynı token seti, farklı ölçek: admin'de tipografi bir kademe küçük, satır yüksekliği sıkı,
tablo satırı 40px, öğrencide kart iç boşluğu geniş, satır yüksekliği rahat.

---

## 4. Kullanıcı akışları

### 4.1 Öğrenci ana akışı (devam ettirilebilir)

```text
Ziyaretçi → landing → "Google ile giriş"
   → [arka uç oturumu kurar, onboardingState döner]
   → LicenseRequired              → Lisans kodu gir
   → ProfileRequired              → Akademik profil (dinamik alanlar)
   → CalendarAuthorizationRequired→ Google Calendar izni (popup code akışı)
   → ReadyForInitialSync          → "İlk senkronizasyonu başlat"
   → InitialSyncInProgress        → Senkron ilerlemesi (worker çalışıyor)
   → Active                       → Öğrenci paneli
```

**Kesinti kuralı:** Her adımın girişinde arayüz `GET /api/onboarding` cevabını okur ve kullanıcıyı
o duruma ait adıma yerleştirir. Stepper hiçbir zaman istemci tarafında ilerlemez. Kullanıcı tarayıcıyı
kapatıp döndüğünde stepper'ın üstünde şu bant görünür:

> *"Kurulumun 3. adımından devam ediyorsun. Önceki adımlar sunucuda kayıtlı."*

Bu bant, prototipte "resumable" iddiasını görünür kılan tek unsur — atlanmamalı.

**Yan durumlar:**

- `ActionRequired` → takvim adımına döner, üstte ne yapılması gerektiğini söyleyen bir eylem kartı
- `Suspended` → terminal ekran, destek yönlendirmesi, yeniden deneme düğmesi yok

### 4.2 Senkronizasyon iş akışı aşamaları

Dekoratif ilerleme çubuğu yok. Gerçek aşamalar (worker hattıyla birebir):

```text
1 Kuyrukta                (Queued)
2 Kaynaklar okunuyor      (source acquisition)
3 Kişisel dersler çözülüyor (audience resolution)
4 Özel takvim hazırlanıyor (managed calendar create/reattach)
5 Etkinlikler oluşturuluyor (insert)
6 Etkinlikler güncelleniyor (patch)
7 Doğrulanıyor            (inventory / verification)
8 Tamamlandı              (Completed)
```

Her aşama için gösterilecek veri: aşama adı, aşama durumu (bekliyor / çalışıyor / bitti / atlandı),
genel yüzde, **oluşturulan / güncellenen / değişmeyen / başarısız** sayaçları, geçen süre, son durum
güncellemesi zaman damgası, uyarılar.

> **Not:** Sayaçlar `UserCalendarEventMapping` defterinden gelir. Prototipte bu sayıların
> "tahmin" değil "defter" olduğu, aşama başlığının yanındaki küçük "defterden" etiketiyle belirtilir.

### 4.3 Yüksek riskli işlem kalıbı (tek bir tekrarlanabilir desen)

Toplu etkinlik, tek kullanıcı uyarısı, kullanıcı askıya alma, lisans iptali, kâr dağıtımı, IP maskesi
kaldırma — **hepsi aynı 6 adımlı deseni kullanır**:

```text
1 Kapsam seç        → kimi/neyi etkiliyor
2 Etki hesapla      → tahmini alıcı / etkilenecek kayıt sayısı (sunucudan)
3 Dahil/hariç incele→ hariç bırakılanlar ve GEREKÇESİ satır satır
4 Önizle            → tam olarak ne yazılacak
5 Güçlü onay        → gerekçe metni (zorunlu) + sonuç özeti + doğrulama yazımı
6 Kuyruğa al + izle → "gönderildi" değil "kuyruğa alındı"; teslim durumu ayrı izlenir
```

Adım 5'in "güçlü onay" bileşeni: kullanıcı, işlemi tanımlayan kısa bir dizgeyi (ör. alıcı sayısı
veya kullanıcı e-postası) elle yazar. Onay düğmesi o ana kadar pasif. Modal'ın üstünde geri
alınamazlık uyarısı ve etkilenecek kayıt sayısı büyük punto ile durur.

### 4.4 Toplu takvim etkinliği akışı

```text
Kitle seç (10 boyut) → Etkinlik detayları → Tahmini alıcı hesapla
→ Dahil/hariç listesi → Takvim önizlemesi → Güçlü onay
→ Kuyruğa al → Teslim izleme (oluşturulan / atlanan / başarısız / bekleyen)
```

**Yinelenme önleme:** Akış, daha önce teslim edilmiş etkinlikleri körü körüne yeniden oluşturmaz.
Arayüz her toplu işleme deterministik bir **kampanya anahtarı** gösterir; aynı anahtarla ikinci bir
teslim denendiğinde ekran "bu kitlenin X kullanıcısı bu etkinliğe zaten sahip — atlanacak" der.
Güncelleme ve iptal, yeni oluşturma değil, mevcut kaydın **yamalanması/kaldırılması** olarak sunulur.

### 4.5 Tek kullanıcı takvim uyarısı akışı

```text
Kullanıcı seç → Lisans + takvim uygunluğunu incele → Şablon seç/düzenle
→ Tarih + hatırlatıcı → Takvim önizlemesi → Onay → Teslim izleme
```

İdempotans göstergesi: her uyarı bir `warning-key` taşır (kullanıcı + şablon + tarih). Aynı anahtarla
ikinci gönderim ekranda "zaten teslim edildi" olarak görünür, yeni etkinlik üretmez.

---

## 5. Ekran envanteri ve gereksinimleri

### 5.1 Landing — `landing.html`

**Bölümler (sırayla):** Hero → Değer önermesi → Google Calendar entegrasyonu açıklaması →
Nasıl çalışır (4 adım) → Desteklenen yıllar/programlar/gruplar → Güvenlik ve gizlilik ilkeleri →
Öğrenci yorumları → SSS → CTA (giriş + lisans etkinleştirme) → Footer.

- **Ana mesaj:** *"Ders programın değişse bile takvimin güncel kalır."*
- **Ton:** Abartılı SaaS dili yok. "Devrim", "10x", "sihir" gibi ifadeler yasak. Sayısal iddia yok
  (kullanıcı sayısı, memnuniyet oranı vb. **uydurulmaz**).
- **Desteklenen kapsam bölümü** gerçek veriyle: Dönem 1 (TR/EN), Dönem 2 (TR tam, EN *audience
  tamamlanınca*), Dönem 3 (*hazırlık aşamasında*). Bu dürüstlük ürünün güven tezinin parçası.
- **Yorumlar:** Prototip metni olduğu görünür şekilde işaretlenir (ör. "Örnek öğrenci geri bildirimi").
- **Görsel:** Hero'da gerçek bir görsel üretilecek — soyut takvim/ritim temalı, insan çizimi değil,
  mor gradyan değil. Ayrıntı §8.4.

**TODO:** Yorumlar gerçek öğrencilerden mi alınacak? Gerçek isim/foto kullanılacaksa izin gerekir.

### 5.2 Gizlilik Politikası — `privacy-policy.html`

Bölümler: Toplanan veriler · Google kimlik ve Calendar verisi kullanımı · Oturum açma kayıtları
(IP, tarayıcı, işletim sistemi, cihaz) · İşleme amacı ve hukuki dayanağı · Saklama süreleri ·
Güvenlik uygulamaları · Erişim, düzeltme, silme hakları · Üçüncü taraf servisler · İletişim ·
Son güncelleme tarihi.

- Sol tarafta yapışkan iç sayfa navigasyonu (aktif bölüm vurgulu)
- Sayfanın en üstünde kalıcı uyarı bandı: **"Prototip metni — yayına almadan önce hukuki inceleme gerekir."**
- Veri tablosu: her veri türü için *ne · neden · ne kadar süre · hukuki dayanak* dört sütun

### 5.3 Kullanım Koşulları — `terms-of-service.html`

Bölümler: Hizmetin kapsamı · Resmî fakülte kaynaklarına bağımlılık · Kullanıcı sorumlulukları ·
Lisans ve deneme koşulları · Erişilebilirlik ve kesintiler · Hesap askıya alma ve sonlandırma ·
Sorumluluk sınırları · Takvim senkronizasyonu sınırlamaları · İletişim.

Aynı prototip uyarı bandı + iç navigasyon. "Kaynağa bağımlılık" bölümü, fakülte kaynağı hatalıysa
takvimin de hatalı olacağını açıkça yazar — bu, ürünün dürüstlük duruşunun hukuki karşılığı.

### 5.4 İletişim — `contact.html`

- Destek formu: kategori (seçim), konu, açıklama, öğrenci numarası (opsiyonel), e-posta
- Kategoriler: Lisans/etkinleştirme · Takvim senkronizasyonu · Akademik profil/grup · Program hatası ·
  Gizlilik/veri talebi · Diğer
- E-posta adresi, SSS yönlendirmesi, beklenen yanıt süresi bilgisi
- Durumlar: boş · doğrulama hatası (alan bazlı) · gönderiliyor · başarılı gönderim · servis hatası

### 5.5 Giriş — `sign-in.html`

- Yalnızca Google düğmesi. **Şifre alanı yok, e-posta alanı yok.**
- Neden Google erişimi gerektiğinin 2 cümlelik açıklaması + "Takvim izni ayrı bir adımda istenir" notu
- Gizlilik ve Koşullar bağlantıları
- Durumlar: `varsayılan` · `yükleniyor` · `kimlik doğrulama başarısız` · `hesap askıya alınmış` ·
  `oturum süresi doldu`
- Erişilebilirlik: görünür odak halkası, klavye ile tam gezinilebilirlik, hata mesajları
  `aria-live="polite"`, düğme 44px+ dokunma hedefi

### 5.6 Onboarding — `onboarding.html`

Tek dosya, `?step=` ve `?state=` parametreleriyle her adım/durum doğrudan adreslenebilir.

**Stepper:** yatayda 5 adım, tamamlananlar işaretli, mevcut vurgulu, gelecekler pasif.
Adım başlıkları: Lisans · Profil · Takvim izni · Senkronizasyon · Hazır.

**Adım 1 — Lisans etkinleştirme**

- `SRK-XXXXX-XXXXX` biçiminde maskelenmiş giriş, otomatik büyük harf, otomatik tire, yapıştırma desteği
- Doğrulama durumları: `boş` · `biçim hatalı` · `doğrulanıyor` · `geçerli` · `süresi dolmuş` ·
  `zaten kullanılmış` · `iptal edilmiş` · `bulunamadı` · `deneme sınırı aşıldı (rate limit)`
- Her hata için ne yapılacağını söyleyen destek yönlendirmesi
- **Düz metin kod geçmişi yok** — girilen kod ekranda saklanmaz, hata sonrası alan temizlenir

**Adım 2 — Akademik profil**

- Alanlar `GET /api/profile/options` şemasından **dinamik** üretilir; sabit liste yazılmaz
- Boyutlar: akademik yıl · dönem (sınıf) · program dili · müfredat grubu · uygulama grubu ·
  uygulama alt grubu · anatomi grubu · dikey koridor grubu · hasta başı grubu ·
  öğretim üyesi uygulama grubu · öğrenci numarası
- **Bağımlı boyutlar**: alt grup yalnızca üst grup seçilince açılır; seçilmeyen üst grup alt grubu kilitler
- Seçilen programa uygulanmayan boyutlar **hiç gösterilmez** (gizlenmez — DOM'a girmez)
- Öğrenci numarası: 10 hane, sadece rakam, baştaki sıfır korunur; fakülte ve program dili
  hanelerinin seçilen programla tutarsızlığı alan altında açıklanır
- Durumlar: yükleniyor · hazır · alan doğrulama hatası · desteklenmeyen kombinasyon · kaydediliyor · kayıtlı

**Adım 3 — Google Calendar yetkilendirme**

- İstenen izin açıkça: *"Sirkadiyen'in kendi oluşturduğu takvim ve etkinlikler"* — mevcut takvimlerin
  okunmadığı/değiştirilmediği net yazılır
- "Özel Sirkadiyen takvimi" stratejisi kutusu: neden ayrı takvim, ne kazandırır (kapatabilirsin,
  renk verebilirsin, kişisel etkinliklerin karışmaz)
- **Erişebilir / Erişemez** iki sütunlu tablo
- Durumlar: `izin bekleniyor` · `popup açık` · `izin reddedildi` · `eksik kapsam verildi` ·
  `izin iptal edilmiş (NeedsReauthorization)` · `yetkili`
- **Dil kuralı:** "Takvimini bağladık" değil, "Yetki verildi; takvimi senkronizasyon sırasında
  sunucu oluşturacak."

**Adım 4 — İlk senkronizasyon** → `sync-progress.html`'e devreder

**Adım 5 — Tamamlandı**

- Ne oluşturulduğunun özeti (etkinlik sayısı, tarih aralığı, takvim adı)
- "Takvimimi aç" (Google Calendar bağlantısı) + "Panele git"

### 5.7 Senkronizasyon ilerlemesi — `sync-progress.html`

§4.2'deki 8 aşama dikey bir zaman çizelgesi olarak. Sağda kalıcı bir özet paneli:
genel yüzde · mevcut aşama · sayaçlar (oluşturulan/güncellenen/değişmeyen/başarısız) · geçen süre ·
son güncelleme · uyarı sayısı.

**Tasarlanacak 10 ayrı durum:**

| Durum | Görsel karakter | Birincil eylem |
| --- | --- | --- |
| Kuyrukta | Nötr, "sıra bekleniyor" | Yok (bilgilendirme) |
| Devam ediyor | Etkin aşama vurgulu, sayaçlar canlı | Yok |
| Tamamlandı | Onaylı, sakin | Takvimi aç / Panele git |
| Uyarılarla tamamlandı | Onaylı + uyarı listesi açılabilir | Uyarıları incele |
| Geçici olarak başarısız | Yeniden deneme zamanı görünür | Tekrar dene |
| Kalıcı olarak başarısız | Durur, gerekçe kategorisi | Destekle iletişime geç |
| Google yetkisi iptal edilmiş | Ayrı ve baskın | Yeniden yetkilendir |
| Kaynak revizyonu incelemede | "Takvim bilerek beklemede" | Bilgi + bekleme açıklaması |
| Desteklenmeyen akademik profil | Profil adımına yönlendirme | Profili düzenle |
| Eylem gerekli | Ne gerektiği net | Duruma özel |

**Kritik kural:** hiçbir durumda "tamamlandı" görseli arka uç `Completed` döndürmeden gösterilmez.
Devam eden durumda yüzde %99'da bekleyebilir; %100 yalnızca onayla gelir.

**"Kaynak revizyonu incelemede"** durumu bu ürünün karakterini gösteren ekran: takvimin
*bilerek* güncellenmediğini, çünkü kaynak veride anomali tespit edildiğini anlatır. Yeşil değil,
nötr-bilgilendirici. Bu ekran yanlış tasarlanırsa ürünün güven tezi çöker.

### 5.8 Öğrenci paneli — `student-dashboard.html`

**İlerlemeci açığa çıkarma:** Sağlıklı durumda ekran sakin ve kısa. Uyarı varsa üstte bant belirir,
teknik ayrıntılar `<details>` içinde açılabilir kalır.

Modüller:

1. **Senkronizasyon sağlığı** — tek satırlık durum + son başarılı senkronizasyon zamanı
2. **Sıradaki dersler** — sonraki 3–5 ders, tarih/saat/konum/eğitmen
3. **Son program değişiklikleri** — hangi ders değişti, ne değişti, ne zaman uygulandı
4. **Akademik profil özeti** — grupların özeti + "düzenle"
5. **Lisans / deneme durumu** — kalan süre, yenileme yönlendirmesi
6. **Google Calendar bağlantısı** — durum + yönetilen takvim bağlantısı
7. **Senkronizasyon geçmişi** — tablo (zaman, tetikleyici, sonuç, sayaçlar)
8. **Onarım / mutabakat talebi** — açıkça "bu, denetlenen bir işlemdir" notuyla
9. **Bildirimler** — kullanıcıya gönderilenlerin listesi
10. **Makaleler ve podcast'ler** — "Yakında" durumu, sahte içerik yok

### 5.9 Admin panosu — `admin-dashboard.html`

Kalıcı sidebar + üstte bağlam çubuğu (ortam etiketi, freeze durumu, aktif operatör).

Özet kartları (her biri drill-down bağlantılı): Toplam kullanıcı · Aktif kullanıcı · Süresi yaklaşan
deneme · Günlük ve aylık giriş · Senkronizasyon başarı oranı · Başarısız senkron işleri ·
Dikkat isteyen kaynaklar · İncelemedeki revizyonlar · Aylık gelir · Aylık gider · Net sonuç ·
Sunucu sağlığı · Kuyruk derinliği.

Grafikler: senkronizasyon başarı oranı (zaman serisi) · günlük girişler (sütun) · kuyruk derinliği
(alan) · gelir–gider (gruplu sütun). Tümü dolgu ile kodlanır, salt kontur değil.
Alt kısımda son kritik aktivite akışı (zaman, aktör, işlem, hedef, sonuç).

**Freeze göstergesi** sidebar'ın üstünde kalıcı: donmuşsa tüm ekranda görünür bir bant, çünkü
donmuş bir sistemde her operasyonel ekranın anlamı değişir.

### 5.10 Finans — `admin-finance.html`

Gelir kategorileri: Kullanıcı lisans geliri · Sponsorluklar · Bağışlar · Diğer gelir.
Gider kategorileri: Sunucular · Alan adları · Dış servisler · Yazılım lisansları · Pazarlama ·
Operasyonel giderler · Diğer giderler.

Bileşenler: Aylık gelir–gider grafiği · Nakit akışı özeti · Kategori dağılımı · İşlem tablosu ·
Gelir ekle akışı · Gider ekle akışı · Belge/fiş referansı · Tarih + kategori filtreleri · Arama ·
Dışa aktarma · **Kâr dağıtım hesaplayıcısı** · Dağıtım geçmişi · Dağıtım öncesi inceleme ·
Güçlü onay · Denetim bilgisi (kim, neyi, ne zaman değiştirdi).

Kâr dağıtımı §4.3'teki 6 adımlı riskli işlem desenini kullanır. Sayfa başında kalıcı bant:
**"Prototip verisi. Muhasebe, vergi veya hukuki uyumluluk sağlamaz."**

### 5.11 Toplu takvim etkinliği — `admin-bulk-event.html`

§4.4 akışı, 7 adımlı bir sihirbaz olarak. Kitle boyutları: akademik yıl · dönem · program dili ·
müfredat grubu · uygulama grubu · uygulama alt grubu · anatomi grubu · hesap durumu · lisans durumu ·
senkronizasyon uygunluğu.

Etkinlik alanları: başlık · açıklama · tarih · başlangıç/bitiş saati **veya açık "tüm gün" seçimi** ·
konum · kategori/renk · hatırlatıcı · hedef kitle · dahili yönetici notu · güncelleme/iptal stratejisi.

Gösterilecekler: tahmini alıcı sayısı · dahil edilen kitle özeti · **hariç bırakılanlar ve
gerekçeleri** (takvim bağlı değil, lisans yok, senkron tamamlanmamış, yetki iptal edilmiş) ·
yinelenme önleme bilgisi · teslim durumu · oluşturulan/atlanan/başarısız/bekleyen sayaçları ·
denetim geçmişi · güvenli güncelleme ve iptal kontrolleri.

Ekranın tonu: bu bir "gönder" düğmesi değil, bir **dağıtım işlemi**. Onay modalında etkilenecek
kullanıcı sayısı en büyük tipografik unsur olmalı.

### 5.12 Tek kullanıcı uyarısı — `admin-user-warning.html`

§4.5 akışı. Kullanıcı kimlik özeti · lisans/deneme durumu · takvim bağlantı durumu ·
mesaj şablonları (deneme bitiyor, yeniden yetkilendirme gerekli, profil eksik) · etkinlik önizlemesi ·
teslim sonucu · güncelleme/iptal · denetim kaydı.

### 5.13 Sunucu izleme — `admin-server.html`

Metrikler: CPU · RAM · Disk · Çalışma süresi · Son yeniden başlatma · API · Worker · Parser ·
PostgreSQL · Redis · Kuyruk derinliği · Senkronizasyon gecikmesi · Son hata oranı ·
Google API hata oranı · Zaman aralığı seçimi · Eşik uyarıları.

Ayrıca tasarlanacak durumlar: **veri yok** · **veri gecikmeli** · **bileşen durumu bilinmiyor** ·
**hizmet bozulmuş (degraded)** · **tam kesinti**. "Bilinmiyor" durumu yeşil değildir — bu, izleme
tasarımının en sık yapılan hatası.

Tüm değerler `PROTOTİP VERİSİ` rozeti taşır. Gerçekçi görünen uydurma üretim değeri yazılmaz.

### 5.14 Erişim kayıtları — `admin-access-logs.html`

Sütunlar: kullanıcı · zaman damgası · **maskeli IP** (`85.104.***.***`) · tarayıcı · işletim sistemi ·
cihaz türü · başarılı/başarısız · yaklaşık konum (varsa).

- Yaklaşık konumun yanında kalıcı uyarı: *"Yaklaşık konum hatalı olabilir; kimlik kanıtı değildir."*
- Filtreler (tarih, kullanıcı, sonuç, cihaz), detay çekmecesi, saklama süresi bilgisi, dışa aktarma
- **IP maskesini kaldırma:** ayrı bir yetki ister, gerekçe ister, denetim kaydı yazar, ve ekranda
  "bu görüntüleme kaydedildi" geri bildirimi verir. Varsayılan asla açık IP değildir.
- Sayfa başında sınıflandırma etiketi: **Hassas kişisel veri**

### 5.15 Kullanıcı yönetimi — `admin-users.html`

**Liste sütunları:** ad · e-posta · öğrenci no · dönem · program dili · akademik gruplar ·
lisans durumu · onboarding durumu · son giriş · son başarılı senkronizasyon · takvim bağlantısı ·
uyarı/hata durumu.

**Detay ekranı:** kimlik özeti · akademik profil · lisans geçmişi · giriş geçmişi ·
senkronizasyon geçmişi · takvim bağlantı durumu · yönetilen etkinlik sayısı · denetim geçmişi ·
eylemler (tek kullanıcı uyarısı gönder · manuel etkinleştirme · askıya alma · yeniden yetkilendirme
zorunluluğu · onarım/mutabakat talebi).

Tehlikeli eylemler §4.3 desenini kullanır. **Hiçbir yerde** Google token'ı, düz metin lisans kodu,
Authorization başlığı veya gizli değer gösterilmez — lisans geçmişi yalnızca durum, tarih, aktör ve
gerekçe gösterir.

### 5.16 Kaynak ve senkronizasyon operasyonları — `admin-sources.html`

Kaynak durumu · son yoklama zamanı · kaynak edinim sonucu (`unchanged` / `changed` / `unavailable` /
`unauthorized` / `malformed` / `rate-limited` / `UnsupportedTransport` / `UnsupportedDocumentFormat`) ·
snapshot inceleme · parser uyarıları ve metrikleri · revizyon doğrulama bulguları ·
karantinaya alınmış revizyon.

Ayrıca mevcut API'lerle eşleşen iki modül: **revizyon inceleme kuyruğu** (onaylayan + gerekçe) ve
**tutulan diff serbest bırakma** (belirsizlik kaynaklı tutmanın serbest bırakılamayacağı açıkça
gösterilir) ve **yönetsel doküman yükleme** (paylaşılan doküman grubu, hedef başına sonuç).

> **⚠ Eksik girdi:** Brief'in 15. maddesinden sonrası aktarım sırasında kesildi (~4.900 karakter).
> 16. madde ve sonrası bu plana dahil edilemedi. Devam eden maddeleri gönderirsen bu bölümü
> genişletirim. Şimdilik plan 1–15 arası maddeleri tam kapsıyor.

---

## 6. Durum matrisi (tek referans)

| Alan | Durumlar |
| --- | --- |
| Onboarding | LicenseRequired · ProfileRequired · CalendarAuthorizationRequired · ReadyForInitialSync · InitialSyncInProgress · Active · ActionRequired · Suspended |
| Lisans kullanımı | Redeemed · AlreadyRedeemedByCurrentUser · UserAlreadyActivated · Invalid · Expired · Revoked · RateLimited |
| Takvim bağlantısı | Authorized · NeedsReauthorization · Denied · InsufficientScope · CalendarUnavailable |
| İlk senkronizasyon | Pending · InProgress · Completed |
| Senkron ekranı | 10 durum (§5.7) |
| Revizyon | Received · Parsing · Parsed · Validating · ReviewRequired · Published · Rejected · Failed · Superseded |
| Diff dağıtımı | Ready · Held · Released · Pending · Dispatched · Failed |
| Sunucu bileşeni | Healthy · Degraded · Down · Unknown · NoData · Stale |
| Form | boş · doğrulanıyor · geçerli · alan hatası · servis hatası · gönderiliyor · başarılı |

Her ekran, ilgili sütundaki **her** durumu prototipte adreslenebilir biçimde taşır
(`?state=NeedsReauthorization` gibi). `index.html` bu kombinasyonların dizinidir.

---

## 7. Tasarım sistemi bağlama

### 7.1 Token'lar

```
--bg      #f7f6f1   Canvas warm — sayfa zemini
--fg      #0b6b69   Primary petrol — metin, başlık, birincil eylem
--accent  #f2765b   Coral — editoryal vurgu (CTA DEĞİL)
--surface #eef0ec   Kart ve panel yüzeyi
--muted   #8db7b4   İkincil metin, meta veri
--border  #d6e0de   Çizgi ve ayırıcılar
```

Tipografi: **Display — Manrope** (400/700), **Body — Inter** (400/700), her ikisi de
`system-ui, -apple-system, Segoe UI, Helvetica Neue, Arial, sans-serif` yedekleriyle.

Kurallar:

- Petrol mavi **tek birincil eylem rengidir**. Coral bir CTA rengi değildir; editoryal ve
  illüstratif vurgudur, ekran başına en fazla iki kez.
- Yüzey kontrastı yüksekliği taşır; gölgeler ölçülü.
- Sıcak nötr zemin ürünü klinik/bürokratik olmaktan çıkarır.

### 7.2 Çözülmesi gereken iki sistem çelişkisi

**⚠ Çelişki 1 — Köşe yarıçapı.** DESIGN.md'nin `Layout` bölümü `Radius: 0px` diyor; aynı belgenin
"Key Characteristics" bölümü ise *"Student cards use a canonical 22px radius / Admin controls use
8–16px radii"* diyor.

> **Varsayımım:** Prose kasıtlı, `0px` ise doldurulmamış varsayılan alan. Prototipte
> **öğrenci kartları 22px, admin kontrolleri 8–16px** kullanacağım.
> **Onayına ihtiyacım var** — "hayır, her şey 0px olsun" dersen tüm sistem sert/brutalist bir
> karaktere kayar ki bu Wise-esinli sakin tona ters düşer.

**⚠ Çelişki 2 — Semantik renkler.** DESIGN.md *"Semantic states are independent from the brand
palette"* diyor, ancak kayıtlı palet doğrulaması yalnızca altı marka rengine izin veriyor. Senkron
sağlığı, sunucu durumu, başarısız iş ve finans göstergeleri için semantik renk **zorunlu**.

> **Önerim:** Marka paletinin dışında dört semantik rol tanımlayalım — `success` (petrolden
> türetilmiş koyu yeşil), `warning` (amber), `danger` (koyu kırmızı), `info` (petrol tonu) —
> hepsi OKLch ile türetilmiş, doygunluğu marka tonuna göre kısılmış. Coral bunlardan biri **değil**.
> **Onayına ihtiyacım var.**

### 7.3 İki yoğunluk

| | Öğrenci / public | Admin |
| --- | --- | --- |
| Gövde punto | 16px | 14px |
| Kart iç boşluk | 24–32px | 12–16px |
| Tablo satır yüksekliği | — | 40px |
| Köşe yarıçapı | 22px (kart) | 8–16px |
| Bilgi yoğunluğu | Tek görev, geniş nefes | Tarama, filtreleme, karşılaştırma |
| Renk kullanımı | Ölçülü, çoğu nötr | Durum renkleri daha sık ama disiplinli |

---

## 8. Prototip teknik yaklaşımı

### 8.1 Dosya planı

```
index.html                    Launcher — ekran + durum dizini
assets/sirkadiyen.css         Paylaşılan token ve bileşen katmanı
assets/prototype.js           Durum yönlendirme (?state=), tablo/filtre etkileşimleri
assets/img/…                  Üretilen görseller

landing.html  privacy-policy.html  terms-of-service.html  contact.html
sign-in.html  onboarding.html      sync-progress.html     student-dashboard.html
admin-dashboard.html  admin-finance.html   admin-users.html
admin-bulk-event.html admin-user-warning.html
admin-sources.html    admin-server.html    admin-access-logs.html
```

Her dosya ≤ ~1000 satır. `admin-*` sayfaları ortak sidebar bileşenini paylaşır.

### 8.2 Durum yönlendirmesi

Durumlar `?state=` sorgu parametresiyle sürülür; `index.html` her ekranın tüm durum varyantlarını
doğrudan bağlantı olarak listeler. Böylece ürün yüzeyinde demo kontrolü bulunmaz, ama her durum
tek tıkla incelenebilir.

Ek olarak her ekranın altında ince bir **"Uygulama notu"** şeridi: bu ekranın hangi API'ye karşılık
geldiği, hangi ADR'nin geçerli olduğu, hangi backend parçasının henüz mevcut olmadığı. Bu şerit
`data-od-id="impl-note"` taşır ve üretime geçerken tek seferde kaldırılabilir.

### 8.3 Etkileşim kapsamı (gerçek olacaklar)

- Onboarding stepper adım geçişleri ve devam ettirme bandı
- Lisans kodu maskeleme + biçim doğrulama
- Profil formunda bağımlı boyut açılımı (üst grup → alt grup)
- Senkron ilerlemesinin canlı simülasyonu (aşama aşama, sayaç artışı)
- Admin tablolarında filtreleme, arama, sıralama, detay çekmecesi
- Toplu etkinlik sihirbazının 7 adımı + tahmini alıcı hesabı + güçlü onay modalı
- Kâr dağıtım hesaplayıcısı (girdi → dağıtım tablosu → onay)
- IP maskesi kaldırma akışı (gerekçe + denetim geri bildirimi)
- Grafikler: gerçek SVG, dolgulu, erişilebilir etiketli

### 8.4 Görseller

Landing hero için gerçek bir görsel üretilecek: soyut, ritim/döngü temalı (ürün adı "sirkadiyen
ritim"e gönderme yapıyor), marka paletinde, insan çizimi ve mor gradyan içermeyen. İkinci bir
görsel "Nasıl çalışır" bölümü için opsiyonel.

### 8.5 Responsive

Kırılım noktaları: 360 · 390 · 430 · 600 · 768 · 820 · 1024 · 1366 · 1440 · 1920.
Hiçbirinde yatay kaydırma yok. Mobil, masaüstünün sıkıştırılmışı değil yeniden tasarımı:
admin tabloları mobilde kart listesine dönüşür, sidebar alt gezinme çubuğuna iner.

### 8.6 Erişilebilirlik

Görünür odak halkaları · klavye ile tam gezinme · `aria-live` ile durum bildirimi ·
44px+ dokunma hedefleri · renk tek başına anlam taşımaz (durum rozetleri ikon + metin taşır) ·
form hataları alanla programatik olarak ilişkili.

---

## 9. Kabul kriterleri

Prototip şu maddelerin **tamamı** doğrulanınca teslim edilmiş sayılır:

- [ ] 15 ekran dosyası + launcher mevcut, hiçbiri taslak bölüm içermiyor
- [ ] §6 durum matrisindeki her durum en az bir adreslenebilir varyantla temsil ediliyor
- [ ] Hiçbir ekranda token, düz metin lisans kodu, Authorization başlığı veya gizli değer yok
- [ ] Tüm IP adresleri varsayılan maskeli; maske kaldırma gerekçe + denetim geri bildirimi istiyor
- [ ] Hiçbir ekran arka uç onayı olmadan başarı göstermiyor (senkron ekranı %100'ü yalnızca `Completed` ile veriyor)
- [ ] Her yıkıcı işlem §4.3 desenini eksiksiz uyguluyor
- [ ] Tüm sayısal veriler "prototip verisi" olarak etiketli; uydurma üretim metriği yok
- [ ] Hukuki sayfalarda inceleme uyarı bandı kalıcı
- [ ] Marka paleti dışına çıkılmamış (onaylanan semantik renkler hariç)
- [ ] Manrope + Inter yedek yığınlarıyla birlikte bağlı
- [ ] 360–1920 arası hiçbir kırılımda yatay kaydırma yok
- [ ] Tüm etkileşimler klavyeyle erişilebilir, odak görünür
- [ ] Grafikler dolguyla kodlanıyor, hiçbir metin kutusunu taşırmıyor
- [ ] Arayüz dili tamamen Türkçe

---

## 10. Açık sorular

| # | Soru | Neden önemli | Varsayımım |
| --- | --- | --- | --- |
| 1 | **Brief'in 16. maddesi ve sonrası** aktarımda kesildi. Kalan maddeleri gönderir misin? | Kapsam eksik kalıyor | Şimdilik 1–15 kapsandı |
| 2 | Köşe yarıçapı: `0px` mi, prose'daki 22px/8–16px mi? (§7.2) | Tüm sistemin karakterini belirler | Prose kazanır: 22px / 8–16px |
| 3 | Semantik renk seti onaylanıyor mu? (§7.2) | Durum ekranları renksiz çalışmaz | 4 semantik rol ekleniyor |
| 4 | Landing yorumları gerçek öğrencilerden mi, örnek metin mi? | İzin ve dürüstlük meselesi | "Örnek geri bildirim" etiketli |
| 5 | Deneme (trial) süresi ürünün gerçek bir özelliği mi, yoksa yalnızca lisans mı var? Brief hem "deneme" hem "lisans" diyor | Panel ve admin kartları buna göre değişir | İkisi de var: lisans = kalıcı, deneme = süreli lisans türü |
| 6 | Admin uygulaması ayrı bir alt alan adı mı (`yonetim.sirkadiyen.com`) yoksa aynı origin'de `/yonetim` mi? | Navigasyon ve oturum tasarımını etkiler | Aynı origin, `/yonetim` |
| 7 | Bildirim kanalı e-posta mı, takvim etkinliği mi, ikisi de mi? | Panel "Bildirimler" modülünün içeriği | Şimdilik takvim + e-posta karışık gösterilecek |
| 8 | Makaleler/podcast "Yakında" mı kalacak yoksa ilk sürümde içerik olacak mı? | Panel yerleşimi | "Yakında" |

---

## 11. Yapım sırası (önerilen)

1. `assets/sirkadiyen.css` — token'lar, tipografi ölçeği, iki yoğunluk katmanı, bileşen temeli
2. `landing.html` — sistemin görsel tezini kuran ilk ekran + hero görseli
3. `sign-in.html` → `onboarding.html` → `sync-progress.html` — kritik akış, tüm durumlarıyla
4. `student-dashboard.html` — ilerlemeci açığa çıkarma kalıbının referansı
5. `privacy-policy.html`, `terms-of-service.html`, `contact.html` — public seti tamamla
6. `admin-dashboard.html` + sidebar bileşeni — admin yoğunluğunun referansı
7. `admin-users.html`, `admin-sources.html`, `admin-access-logs.html`, `admin-server.html`
8. `admin-bulk-event.html`, `admin-user-warning.html` — riskli işlem deseninin en zor iki uygulaması
9. `admin-finance.html` — en fazla yeni ürün alanı içeren modül
10. `index.html` — launcher, tüm durum varyantlarının dizini
11. Doğrulama geçişi — §9 kabul kriterleri

Adım 1–4 birinci teslim paketi olarak ayrı verilebilir; bu, geri kalanına geçmeden önce görsel
yönü onaylaman için doğal bir durak.

---

## 12. Sonraki adım

Bu belgeyi incele ve doğrudan düzenle. Özellikle şunlara bakmanı isterim:

1. **§10'daki 8 açık soru** — hiç değilse 1, 2 ve 3 numaralı olanlar (kesilen brief maddeleri,
   köşe yarıçapı çelişkisi, semantik renk seti). Bunlar yapıma başlamadan netleşmeli.
2. **§5 ekran envanteri** — eksik bir ekran veya modül var mı?
3. **§11 yapım sırası** — tamamını tek seferde mi istiyorsun, yoksa 1–4. adımları ilk paket olarak mı?

Onayladıktan sonra **Design moduna geçip** bu belgeden üretime başlayabiliriz. Belgeyi değiştirirsen
değişiklikler doğrudan prototipe yansır — plan, üretimin girdisi.
