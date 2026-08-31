import Image from 'next/image';

// Sirkadiyen yalnızca Google Calendar'a yazar (ADR-024: kullanıcıya ait yönetilen
// bir takvim). Bu yüzden programı okumanın önerilen yolu Google Takvim'dir; iOS'un
// yerleşik Takvim uygulaması aboneliği üzerinden okuyan bir öğrenci hatırlatmaları
// ve renkleri eksik görür. Bu bileşen o tavsiyeyi ve resmî mağaza rozetlerini tek
// yerde tutar; hem panelde hem ilk senkronizasyon ekranında kullanılır.
//
// Rozetler mağazaların kendi resmî görselleri (`public/store/`), değiştirilmeden
// kullanılıyor — marka kılavuzlarının istediği budur. Google Play rozetinin kendi
// boşluğu görselin içinde olduğu için iki rozet aynı CSS yüksekliğiyle
// hizalanmıyor; siyah gövdeler eşitlensin diye Play 52px, App Store 40px yükseklikte
// veriliyor (oranlar tarayıcıda ölçüldü).

const PLAY_STORE_URL = 'https://play.google.com/store/apps/details?id=com.google.android.calendar';
const APP_STORE_URL = 'https://apps.apple.com/app/google-calendar/id909319292';

const RECOMMENDATION =
  'Dersler doğrudan Google Takvim’e yazılır. Programını Google Takvim uygulaması üzerinden takip etmeni öneririz: hatırlatmalar, ders renkleri ve çevrimdışı erişim yalnızca orada eksiksiz çalışır.';

/** Mağaza rozetleri; bağlantılar yeni sekmede açılır (kullanıcı senkronizasyon
 *  ekranındaysa sayfadan ayrılmamalı). */
export function GoogleCalendarStoreBadges() {
  return (
    <div className="store-badges">
      <a
        className="store-badge store-badge--play"
        href={PLAY_STORE_URL}
        target="_blank"
        rel="noreferrer noopener"
      >
        <Image
          src="/store/google-play-badge.svg"
          alt="Get it on Google Play"
          width={134}
          height={52}
          unoptimized
        />
      </a>
      <a
        className="store-badge store-badge--apple"
        href={APP_STORE_URL}
        target="_blank"
        rel="noreferrer noopener"
      >
        <Image
          src="/store/app-store-badge.svg"
          alt="Download on the App Store"
          width={120}
          height={40}
          unoptimized
        />
      </a>
    </div>
  );
}

/**
 * Tavsiye metni + mağaza rozetleri.
 * `variant="card"` panelde bir kart olarak, `variant="plain"` ise bir kabın
 * (örneğin senkronizasyon ekranındaki kartın) içinde kullanılır.
 */
export function GoogleCalendarAppLinks({
  variant = 'card',
  title = 'Google Takvim uygulaması',
}: {
  variant?: 'card' | 'plain';
  title?: string;
}) {
  const body = (
    <>
      <p className="muted" style={{ marginTop: variant === 'card' ? 8 : 4, fontSize: 13.5 }}>
        {RECOMMENDATION}
      </p>
      <GoogleCalendarStoreBadges />
    </>
  );

  if (variant === 'plain') {
    return <div className="calendar-app-links">{body}</div>;
  }

  return (
    <section className="card card-content calendar-app-links">
      <h3 style={{ fontSize: 15 }}>{title}</h3>
      {body}
    </section>
  );
}
