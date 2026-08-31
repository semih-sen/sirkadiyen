// Sirkadiyen yalnızca Google Calendar'a yazar (ADR-024: kullanıcıya ait yönetilen
// bir takvim). Bu yüzden programı okumanın önerilen yolu Google Takvim'dir; iOS'un
// yerleşik Takvim uygulaması aboneliği üzerinden okuyan bir öğrenci hatırlatmaları
// ve renkleri eksik görür. Bu bileşen o tavsiyeyi ve resmî mağaza bağlantılarını
// tek yerde tutar; hem panelde hem ilk senkronizasyon ekranında kullanılır.

const PLAY_STORE_URL = 'https://play.google.com/store/apps/details?id=com.google.android.calendar';
const APP_STORE_URL = 'https://apps.apple.com/app/google-calendar/id909319292';

const RECOMMENDATION =
  'Dersler doğrudan Google Takvim’e yazılır. Programını Google Takvim uygulaması üzerinden takip etmeni öneririz: hatırlatmalar, ders renkleri ve çevrimdışı erişim yalnızca orada eksiksiz çalışır.';

function PlayIcon() {
  return (
    <svg width="18" height="20" viewBox="0 0 24 27" aria-hidden="true" focusable="false">
      <path d="M1.6.7A2 2 0 0 0 1 2.1v22.8a2 2 0 0 0 .6 1.4l12-12.2Z" fill="#34a853" />
      <path d="M18.3 9.2 14.6 7 1.6.7 13.6 14.1Z" fill="#ea4335" />
      <path d="m1.6 26.3 12-12.2 4.7 4.6-3.7 2.2Z" fill="#fbbc04" />
      <path d="m18.3 9.2 4.6 2.6a2 2 0 0 1 0 3.5l-4.6 2.6-4.7-4.6Z" fill="#4285f4" />
    </svg>
  );
}

function AppleIcon() {
  return (
    <svg width="17" height="20" viewBox="0 0 17 20" aria-hidden="true" focusable="false">
      <path
        d="M13.9 10.6c0-2.3 1.9-3.4 2-3.5-1.1-1.6-2.8-1.8-3.4-1.8-1.4-.2-2.8.8-3.5.8-.7 0-1.9-.8-3.1-.8-1.6 0-3 .9-3.8 2.4-1.6 2.8-.4 7 1.2 9.3.8 1.1 1.7 2.4 2.9 2.3 1.2 0 1.6-.7 3.1-.7 1.4 0 1.8.7 3.1.7 1.3 0 2.1-1.1 2.8-2.3.9-1.3 1.3-2.6 1.3-2.7 0 0-2.6-1-2.6-3.7ZM11.6 3.8c.6-.8 1.1-1.9 1-3-.9 0-2.1.6-2.8 1.4-.6.7-1.2 1.8-1 2.9 1 0 2.1-.5 2.8-1.3Z"
        fill="currentColor"
      />
    </svg>
  );
}

/** Mağaza rozetleri; bağlantılar yeni sekmede açılır (kullanıcı senkronizasyon
 *  ekranındaysa sayfadan ayrılmamalı). */
export function GoogleCalendarStoreBadges() {
  return (
    <div className="store-badges">
      <a className="store-badge" href={PLAY_STORE_URL} target="_blank" rel="noreferrer noopener">
        <PlayIcon />
        <span>
          <small>Android için</small>
          <strong>Google Play</strong>
        </span>
      </a>
      <a className="store-badge" href={APP_STORE_URL} target="_blank" rel="noreferrer noopener">
        <AppleIcon />
        <span>
          <small>iPhone ve iPad için</small>
          <strong>App Store</strong>
        </span>
      </a>
    </div>
  );
}

/**
 * Tavsiye metni + mağaza bağlantıları.
 * `variant="card"` panelde bir kart olarak, `variant="plain"` ise bir kabın
 * (örneğin senkronizasyon ekranındaki bilgi kutusunun) içinde kullanılır.
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
