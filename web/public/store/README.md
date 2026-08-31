# Mağaza rozetleri

Panelde ve ilk senkronizasyon ekranında kullanılan resmî mağaza rozetleri
(`GoogleCalendarAppLinks`). Rozetler değiştirilmeden kullanılıyor; yalnızca
ölçekleniyorlar. Uzaktan bağlanmak (hotlink) yerine burada barındırılıyorlar:
rozet, üçüncü bir sunucunun erişilebilirliğine bağlı olmamalı.

| Dosya | Kaynak |
| --- | --- |
| `app-store-badge.svg` | Apple'ın kendi dağıttığı dosya: `https://developer.apple.com/assets/elements/badges/download-on-the-app-store.svg` (US/UK, siyah). |
| `google-play-badge.svg` | Google Play rozetinin İngilizce ("GET IT ON / Google Play") tek dilli hâli. |

**`google-play-badge.svg` hakkında bilinmesi gereken:** bu dosya, Google'ın
`play.google.com/intl/en_us/badges/...` adresindeki PNG'sinin **kendisi değildir**;
o adres bu ortamın ağ politikasından ulaşılamadığı için rozet, MIT lisanslı
`localized-responsive-google-play-badge` (1.0.2) paketinin çok dilli SVG'sinden
İngilizce varyant ayıklanarak üretildi (dil anahtarlama `<switch>`'i ve script'i
çıkarıldı, kullanılmayan semboller silindi). Tarayıcıda resmî rozetle aynı görünüyor
(headless Chromium'da doğrulandı), ancak byte düzeyinde Google'ın dosyası değil.
Google'ın rozet üretecinden alınan resmî dosya elde edilirse bu dosyanın üzerine
yazılması yeterlidir; bileşende ya da CSS'te değişiklik gerekmez.

Rozetlerin kendi boşlukları farklı: Play rozetinin zorunlu boşluğu görselin içinde,
Apple rozetininki değil. Bu yüzden siyah gövdeler `store-badge--play img { height: 52px }`
ve `store-badge--apple img { height: 40px }` ile eşitleniyor (oranlar tarayıcıda ölçüldü).
