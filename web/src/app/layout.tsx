import type { Metadata } from 'next';
import type { ReactNode } from 'react';
import { SessionProvider } from '@/components/SessionProvider';
import './globals.css';

export const metadata: Metadata = {
  title: 'Sirkadiyen',
  description: 'Akademik ders programı senkronizasyonu',
};

export default function RootLayout({ children }: { children: ReactNode }) {
  return (
    <html lang="tr">
      <body>
        <SessionProvider>
          <div className="shell">{children}</div>
        </SessionProvider>
      </body>
    </html>
  );
}
