'use client';

import Link from 'next/link';
import { useEffect, useId, useState } from 'react';

// Public-site menu for narrow viewports. `.nav-links` is hidden below 900px, so
// without this the section anchors and the secondary call to action are simply
// unreachable on a phone — which is where nearly all of the traffic is. The
// drawer is a client island; the surrounding <SiteNav> stays a server component.

export function MobileNav({
  items,
}: {
  items: { href: string; label: string }[];
}) {
  const [open, setOpen] = useState(false);
  const panelId = useId();

  useEffect(() => {
    if (!open) {
      return;
    }
    function onKeyDown(event: KeyboardEvent) {
      if (event.key === 'Escape') {
        setOpen(false);
      }
    }
    document.addEventListener('keydown', onKeyDown);
    // Keeps the page behind the drawer from scrolling under the user's thumb.
    document.body.classList.add('has-drawer');
    return () => {
      document.removeEventListener('keydown', onKeyDown);
      document.body.classList.remove('has-drawer');
    };
  }, [open]);

  return (
    <div className="mobile-nav">
      <button
        className="nav-toggle"
        type="button"
        aria-expanded={open}
        aria-controls={panelId}
        onClick={() => setOpen((value) => !value)}
      >
        <span className="nav-toggle__bars" aria-hidden="true" />
        <span className="sr-only">{open ? 'Menüyü kapat' : 'Menüyü aç'}</span>
      </button>

      {open && (
        <>
          <button
            className="drawer-backdrop"
            type="button"
            tabIndex={-1}
            aria-hidden="true"
            onClick={() => setOpen(false)}
          />
          <nav className="nav-drawer" id={panelId} aria-label="Mobil gezinme">
            <ul className="nav-drawer__links">
              {items.map((item) => (
                <li key={item.href}>
                  <Link href={item.href} onClick={() => setOpen(false)}>
                    {item.label}
                  </Link>
                </li>
              ))}
            </ul>
            <div className="nav-drawer__actions">
              <Link className="btn btn-primary btn-block" href="/sign-in" onClick={() => setOpen(false)}>
                Google ile giriş yap
              </Link>
              <Link
                className="btn btn-secondary btn-block"
                href="/sign-in?intent=lisans"
                onClick={() => setOpen(false)}
              >
                Lisansımı etkinleştir
              </Link>
            </div>
          </nav>
        </>
      )}
    </div>
  );
}
