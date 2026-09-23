# Sirkadiyen API: MinIO Destekli Claude Code Entegrasyon Spesifikasyonu

Bu doküman, Sirkadiyen API ve Anthropic'in CLI tabanlı yapay zeka kodlama asistanı Claude Code (Agent) arasındaki entegrasyonun mimarisini, veri akışını ve teknik gereksinimlerini detaylandırır.

**Önemli Not:** Bu yapı, son kullanıcı hesabının (Claude Pro) kişisel, düşük hacimli ve otomasyon amaçlı olarak, mevcut bir MinIO (S3) vault'u ile senkronize edilmesini sağlar. Bu mimari hesabı dışarıya açık ticari bir API'ye dönüştürmez; şahsi "İkinci Beyin" (Zettelkasten) iş akışını asiste etmek için tasarlanmıştır.

## 1. Mimari ve Temel İşleyiş

Sirkadiyen sistemi, not üretimini ve Obsidian vault'u içindeki atıf ağını (graph) otomatik olarak yönetmeyi amaçlar. Bu yapı ana bileşenlerden oluşur:

1. **Sirkadiyen API (.NET):** Süreci başlatan, MinIO ile konuşan, dosya listelerini yöneten ve Claude'u tetikleyen merkezi servis.
2. **Claude Code (Agent):** Sunucuda çalışan, kendisine verilen bağlam ve kurallara (`CLAUDE.md`) göre Markdown üreten yapay zeka CLI aracı.
3. **MinIO (Object Storage):** Obsidian vault'unun fiili olarak tutulduğu nihai S3 depolama alanı.

### 1.1 İş Akışı Özeti

1. Kullanıcı (örn. iPad üzerinden), Sirkadiyen API'ye yeni bir not oluşturma isteği (prompt) gönderir.
2. **Kataloglama:** Sirkadiyen API, MinIO'ya bağlanır ve mevcut vault'taki tüm dosyaların isimlerini içeren bir JSON listesi oluşturur.
3. Sirkadiyen API, kullanıcının prompt'una bu JSON listesini ekleyerek Claude Agent'ı tetikler. (Böylece Agent, atıf yapabileceği `[[Dosya Adı]]` hedeflerini tam olarak bilir).
4. Claude Agent çalışır ve ürettiği içeriği sunucuda **geçici bir Markdown dosyasına (temp file)** kaydeder.
5. **MinIO'ya Aktarım:** Sirkadiyen API, bu geçici dosyayı okur ve MinIO'daki vault'a doğrudan yükler.
6. **Atıf (Backlink) Döngüsü:** Eğer yeni oluşturulan dosyaya, vault'taki *eski* dosyalardan da atıf yapılması gerekiyorsa; Claude bu talebi API'ye bildirir. API ilgili eski dosyaları MinIO'dan çeker, Agent'a sunar, Agent içine linkleri ekler ve API bu güncellenmiş dosyaları tekrar MinIO'ya yazar.
7. **Temizlik:** İşlem tamamlandığında, sunucuda oluşturulan tüm geçici dosyalar API tarafından silinir.

## 2. API ve Agent Arasındaki İletişim (İç Mimari)

### 2.1 İş Tetikleme ve Prompt Enjeksiyonu

Sirkadiyen API, Claude'u çalıştırmadan önce bağlamı hazırlar. Agent'a verilecek komut şu bilgileri içermelidir:

```json
{
  "user_prompt": "Bana farmakoloji dersi için Beta Blokerler hakkında detaylı bir özet çıkar.",
  "existing_vault_files": [
    "Kardiyoloji_Giris.md",
    "Hipertansiyon_Tedavi_Protokolleri.md",
    "Aritmi_Tipleri.md"
  ]
}
```

*API, bu JSON listesini Agent'ın prompt'una şu şekilde yedirir:* "Mevcut notlarım şunlardır: [Liste]. Lütfen metni yazarken uygun olanlara `[[Not Adı]]` formatında atıf yap."

### 2.2 Claude Code Çalıştırma ve Geçici Dosya

Sirkadiyen API, `System.Diagnostics.Process` kullanarak CLI'yi tetikler.

1. **İzolasyon:** İşlem, geçici bir çalışma dizininde (`/tmp/sirkadiyen-agent-workspace`) yürütülür.
2. **Komut İcrası:** Claude'a geçici bir dosya oluşturması söylenir.

**Örnek Tetikleme:**
```bash
claude "Kullanıcı isteği: Beta Blokerler... Mevcut dosyalar: [...]. Sonucu 'beta_blokerler_temp.md' adında geçici bir dosyaya kaydet."
```

### 2.3 Dosya İletimi, MinIO'ya Aktarım ve Temizlik

Dosya üretildikten sonra sürecin yönetimi tamamen API'dedir:

1. **Dosyanın Alınması:** Sirkadiyen API, `/tmp/sirkadiyen-agent-workspace/beta_blokerler_temp.md` dosyasını okur.
2. **MinIO'ya Yükleme:** API, S3 SDK kullanarak dosyayı MinIO'daki vault'a (`/Kardiyoloji/Beta_Blokerler.md` vb. bir isimle) yükler.
3. **Silme İşlemi:** Yükleme başarılı olduktan sonra API, yerel sunucudaki geçici dosyayı anında siler (`File.Delete(tempPath)`). Agent'ın MinIO ile doğrudan hiçbir teması olmaz.

### 2.4 Atıf Yönetimi ve Geri Bağlantı (Backlink) Döngüsü

Bu entegrasyonun en kritik noktası, Zettelkasten ağını canlı tutmaktır.

1. **Gereksinim Tespiti:** Claude Agent, yeni dosyayı oluştururken (veya hemen sonrasında), sistem prompt'unda belirtilen kural gereği API'ye bir JSON çıktısı/mesajı bırakır. Örn: *"Bu yeni 'Beta_Blokerler.md' dosyası oluşturuldu. Ancak eski 'Hipertansiyon_Tedavi_Protokolleri.md' dosyasında da bu yeni nota bir atıf eklenmelidir."*
2. **Dosyaların Çekilmesi:** Sirkadiyen API bu mesajı parse eder. MinIO'dan `Hipertansiyon_Tedavi_Protokolleri.md` dosyasını indirir ve geçici bir alana koyar.
3. **Düzeltme Aşaması:** API, Claude'u tekrar tetikler: *"İşte 'Hipertansiyon_Tedavi_Protokolleri.md' dosyasının içeriği. Lütfen ilgili bölüme '[[Beta_Blokerler]]' linkini bağlamı bozmadan ekle ve dosyayı kaydet."*
4. **Geri Yükleme:** Claude dosyayı günceller. API, güncellenmiş bu geçici dosyayı alır, MinIO'daki eski dosyanın üzerine yazar (update eder) ve geçici dosyayı siler.

## 3. MinIO (S3) Entegrasyonu

* **Bucket Adı:** Obsidian vault'unun tutulduğu mevcut bucket.
* **İzinler:** MinIO üzerindeki tüm Read/Write işlemleri sadece Sirkadiyen API tarafından kendi Credentials'ları ile yapılır. Agent (Claude Code) MinIO'yu bilmez, göremez ve erişemez. Bu, "Data Access Restriction" prensibini sağlar.

## 4. Güvenlik ve Hata Yönetimi

* **Timeout (Zaman Aşımı):** Claude Code'un çalışması uzun sürebilir. Subprocess için belirli bir zaman aşımı (örn. 90 saniye) belirlenmeli, aşılırsa işlem zorla durdurulmalı ve hata kaydedilmelidir.
* **Yetkilendirme:** API'ye gelen her dış istek (iPad'den vb.), `appsettings.json` yerine sunucudaki **Environment Variables (Ortam Değişkenleri)** üzerinden kontrol edilen bir `secret` anahtarı ile doğrulanmalıdır (`401 Unauthorized`).
* **Hata Yakalama:** API limitleri dolduğunda veya Agent çöktüğünde oluşan hatalar yakalanmalı, o ana kadar oluşturulan geçici dosyalar (varsa) temizlenmelidir (Garbage Collection).