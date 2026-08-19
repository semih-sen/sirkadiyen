import type { Metadata, Viewport } from 'next';
import type { ReactNode } from 'react';
import { SessionProvider } from '@/components/SessionProvider';
import './globals.css';

export const metadata: Metadata = {
  title: 'Sirkadiyen — Akademik takvim eşitleme',
  description:
    'Fakültenin resmî ders programını okur, akademik grubuna göre kişiselleştirir ve Google Takvim’inde ayrı bir takvimde güncel tutar.',
  icons: {
    icon: '/sirkadiyen-mark.png',
    apple: '/sirkadiyen-logo.png',
  },
};

// The site is used overwhelmingly on phones, so the viewport is declared
// explicitly rather than relying on the framework default: `viewportFit: cover`
// lets the layout paint under the notch/home indicator, and the safe-area insets
// consumed in globals.css keep sticky bars clear of them. Zoom is left enabled
// (no `maximumScale`) because clamping it breaks pinch-zoom accessibility.
export const viewport: Viewport = {
  width: 'device-width',
  initialScale: 1,
  viewportFit: 'cover',
  themeColor: '#0b6b69',
};

// Manrope (display) + Inter (body) are loaded via a runtime stylesheet link with a
// system-ui fallback stack in globals.css, so the build has no font-fetch step and
// text stays readable before the webfont arrives.
export default function RootLayout({ children }: { children: ReactNode }) {
  return (
    <html lang="tr">
      <head>
        <link rel="preconnect" href="https://fonts.googleapis.com" />
        <link rel="preconnect" href="https://fonts.gstatic.com" crossOrigin="anonymous" />
        <link
          rel="stylesheet"
          href="https://fonts.googleapis.com/css2?family=Inter:wght@400;600;700&family=Manrope:wght@400;700&display=swap"
        />
      </head>
      <body>
        <SessionProvider>{children}</SessionProvider>
      </body>
    </html>
  );
}
