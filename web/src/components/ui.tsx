// Shared presentational building blocks ported from the prototype design system
// (web-design/assets/sirkadiyen.css). These carry no backend logic; every page
// composes them and owns its own data fetching. Pure markup + design-system
// classes, so they render equally in server and client trees.

import Image from 'next/image';
import Link from 'next/link';
import type { ReactNode } from 'react';
import { MobileNav } from '@/components/MobileNav';

// --- Brand ------------------------------------------------------------------

export function Brand({ href = '/', suffix }: { href?: string; suffix?: string }) {
  return (
    <Link className="brand" href={href}>
      <Image
        className="brand__mark"
        src="/sirkadiyen-mark.png"
        alt=""
        width={34}
        height={34}
        priority
      />{' '}
      Sirkadiyen{suffix ? ` ${suffix}` : ''}
    </Link>
  );
}

// --- Public site navigation -------------------------------------------------

const PUBLIC_NAV: { href: string; label: string }[] = [
  { href: '/#nasil-calisir', label: 'Nasıl çalışır' },
  { href: '/#kapsam', label: 'Kapsam' },
  { href: '/#sss', label: 'SSS' },
  { href: '/iletisim', label: 'İletişim' },
];

export function SiteNav() {
  return (
    <header className="site-nav">
      <div className="container site-nav__row">
        <Brand />
        <nav aria-label="Birincil gezinme">
          <ul className="nav-links">
            {PUBLIC_NAV.map((item) => (
              <li key={item.href}>
                <Link href={item.href}>{item.label}</Link>
              </li>
            ))}
          </ul>
        </nav>
        <div className="nav-actions">
          <Link className="btn btn-tertiary" href="/sign-in">
            Giriş yap
          </Link>
          {/* Below 900px only the primary action stays in the bar; the rest of
              the navigation moves into <MobileNav>'s drawer. */}
          <Link className="btn btn-primary nav-actions__wide" href="/sign-in?intent=lisans">
            Lisansımı etkinleştir
          </Link>
          <MobileNav items={PUBLIC_NAV} />
        </div>
      </div>
    </header>
  );
}

// --- Public footer ----------------------------------------------------------

export function SiteFooter() {
  return (
    <footer className="site-footer">
      <div className="container">
        <div className="footer-cols">
          <div>
            <Brand />
            <p className="muted" style={{ marginTop: 14, maxWidth: '32ch' }}>
              İstanbul Tıp Fakültesi öğrencileri için akademik takvim eşitleme servisi.
            </p>
          </div>
          <div>
            <h4>Ürün</h4>
            <ul>
              <li>
                <Link href="/#nasil-calisir">Nasıl çalışır</Link>
              </li>
              <li>
                <Link href="/#kapsam">Desteklenen kapsam</Link>
              </li>
              <li>
                <Link href="/sign-in">Giriş yap</Link>
              </li>
            </ul>
          </div>
          <div>
            <h4>Yasal</h4>
            <ul>
              <li>
                <Link href="/privacy">Gizlilik Politikası</Link>
              </li>
              <li>
                <Link href="/terms">Kullanım Koşulları</Link>
              </li>
            </ul>
          </div>
          <div>
            <h4>Destek</h4>
            <ul>
              <li>
                <Link href="/iletisim">İletişim</Link>
              </li>
              <li>
                <Link href="/#sss">Sık sorulan sorular</Link>
              </li>
            </ul>
          </div>
        </div>
        <div className="footer-bottom">
          <span>© {new Date().getFullYear()} Sirkadiyen. Tüm hakları saklıdır.</span>
          <span>Arka uç otoritedir; arayüz onaylanmamış bir başarıyı göstermez.</span>
        </div>
      </div>
    </footer>
  );
}

// --- Student top bar (signed-in, single-task surfaces) ----------------------

export function StudentTopbar({
  subtitle,
  onSignOut,
}: {
  subtitle?: string;
  onSignOut?: () => void;
}) {
  return (
    <header className="student-topbar">
      <div className="container">
        <Brand />
        <div className="cluster student-topbar__actions" style={{ gap: 14 }}>
          {subtitle && (
            <span className="muted student-topbar__subtitle" style={{ fontSize: 13.5 }}>
              {subtitle}
            </span>
          )}
          {onSignOut && (
            <button className="btn btn-tertiary btn-sm" type="button" onClick={onSignOut}>
              Çıkış yap
            </button>
          )}
        </div>
      </div>
    </header>
  );
}

// --- Centered single-card shell (sign-in + onboarding steps) ----------------

export function AuthShell({
  children,
  wide = false,
}: {
  children: ReactNode;
  wide?: boolean;
}) {
  return (
    <main className="auth-shell">
      <div className={`card${wide ? ' auth-card auth-card--wide' : ' auth-card'}`}>{children}</div>
    </main>
  );
}

// --- Onboarding stepper -----------------------------------------------------

const STEP_LABELS = ['Lisans', 'Profil', 'Takvim izni', 'Senkronizasyon', 'Hazır'];

/** `activeIndex` is 0-based; steps before it render as done, after it as pending. */
export function Stepper({ activeIndex }: { activeIndex: number }) {
  return (
    <ol className="stepper" aria-label="Kurulum adımları">
      {STEP_LABELS.map((label, index) => {
        const status = index < activeIndex ? 'done' : index === activeIndex ? 'current' : 'pending';
        return (
          <li key={label} data-status={status} aria-current={status === 'current' ? 'step' : undefined}>
            <span>
              <span className="step-num">{index + 1}</span>
              {/* On phones every label but the current step's is hidden (CSS), so
                  five steps still fit without wrapping or shrinking the text. */}
              <span className="step-label">{label}</span>
            </span>
          </li>
        );
      })}
    </ol>
  );
}

// --- Banner -----------------------------------------------------------------

type BannerTone = 'info' | 'warning' | 'danger' | 'neutral';

const BANNER_CLASS: Record<BannerTone, string> = {
  info: 'banner banner-info',
  warning: 'banner banner-warning',
  danger: 'banner banner-danger',
  neutral: 'banner',
};

const BANNER_ICON: Record<BannerTone, string> = {
  info: 'ℹ',
  warning: '⚠',
  danger: '⨯',
  neutral: 'ℹ',
};

export function Banner({
  tone = 'info',
  icon,
  children,
}: {
  tone?: BannerTone;
  icon?: ReactNode;
  children: ReactNode;
}) {
  return (
    <div className={BANNER_CLASS[tone]} role={tone === 'danger' ? 'alert' : 'status'}>
      <span className="banner-icon" aria-hidden="true">
        {icon ?? BANNER_ICON[tone]}
      </span>
      <div>{children}</div>
    </div>
  );
}

