'use client';

import { useEffect, useState } from 'react';
import Link from 'next/link';
import type { ReactNode } from 'react';
import { Brand, SiteFooter } from '@/components/ui';

export interface LegalSection {
  id: string;
  title: string;
  content: ReactNode;
}

/**
 * Shared layout for the privacy and terms pages (plan §5.2, §5.3): a persistent
 * legal-review banner, sticky in-page navigation with scroll-spy, and a series of
 * sections. Legal copy is prototype text and carries the review warning until a
 * lawyer has cleared it.
 */
export function LegalDocument({
  title,
  updated,
  bannerText,
  sections,
}: {
  title: string;
  updated: string;
  bannerText: string;
  sections: LegalSection[];
}) {
  const [activeId, setActiveId] = useState(sections[0]?.id);

  useEffect(() => {
    function onScroll() {
      const pos = window.scrollY + 120;
      let current = sections[0]?.id;
      for (const section of sections) {
        const element = document.getElementById(section.id);
        if (element && element.offsetTop <= pos) {
          current = section.id;
        }
      }
      setActiveId(current);
    }
    onScroll();
    window.addEventListener('scroll', onScroll, { passive: true });
    return () => window.removeEventListener('scroll', onScroll);
  }, [sections]);

  return (
    <>
      <a className="skip-link" href="#legal-main">
        İçeriğe geç
      </a>
      <div className="banner-legal" role="note">
        ⚠ {bannerText}
      </div>

      <header className="site-nav">
        <div className="container site-nav__row">
          <Brand />
          <div className="nav-actions">
            <Link className="btn btn-tertiary" href="/">
              ← Ana sayfa
            </Link>
          </div>
        </div>
      </header>

      <main id="legal-main" style={{ padding: '40px 0 80px' }}>
        <div className="container">
          <span className="eyebrow">Yasal</span>
          <h1 style={{ marginTop: 10 }}>{title}</h1>
          <p className="muted" style={{ marginTop: 10 }}>
            Son güncelleme: {updated}
          </p>

          <div className="legal-layout" style={{ marginTop: 40 }}>
            <nav aria-label="Bölüm gezinme">
              <ul className="legal-toc">
                {sections.map((section) => (
                  <li key={section.id}>
                    <a
                      href={`#${section.id}`}
                      className={activeId === section.id ? 'active' : undefined}
                      aria-current={activeId === section.id ? 'true' : undefined}
                    >
                      {section.title}
                    </a>
                  </li>
                ))}
              </ul>
            </nav>

            <div>
              {sections.map((section) => (
                <section className="legal-section" id={section.id} key={section.id}>
                  <h3>{section.title}</h3>
                  {section.content}
                </section>
              ))}
            </div>
          </div>
        </div>
      </main>

      <SiteFooter />
    </>
  );
}
