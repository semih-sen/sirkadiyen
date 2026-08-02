'use client';

import { useEffect, useState } from 'react';
import type { FormEvent } from 'react';
import Link from 'next/link';
import { Banner, Brand, ImplNote, SiteFooter } from '@/components/ui';

const CATEGORIES: { value: string; label: string }[] = [
  { value: 'lisans', label: 'Lisans / etkinleştirme' },
  { value: 'senkron', label: 'Takvim senkronizasyonu' },
  { value: 'profil', label: 'Akademik profil / grup' },
  { value: 'hata', label: 'Program hatası' },
  { value: 'gizlilik', label: 'Gizlilik / veri talebi' },
  { value: 'diger', label: 'Diğer' },
];

interface Errors {
  category?: string;
  subject?: string;
  description?: string;
  email?: string;
}

export default function ContactPage() {
  const [category, setCategory] = useState('');
  const [subject, setSubject] = useState('');
  const [description, setDescription] = useState('');
  const [studentNumber, setStudentNumber] = useState('');
  const [email, setEmail] = useState('');
  const [errors, setErrors] = useState<Errors>({});

  // Prefill the category from ?kategori= without pulling in useSearchParams (which
  // would force a Suspense boundary); the query is read once on the client.
  useEffect(() => {
    const value = new URLSearchParams(window.location.search).get('kategori');
    if (value && CATEGORIES.some((item) => item.value === value)) {
      setCategory(value);
    }
  }, []);

  function validate(): Errors {
    const next: Errors = {};
    if (!category) next.category = 'Bir kategori seç.';
    if (!subject.trim()) next.subject = 'Konu alanı zorunludur.';
    if (!description.trim()) next.description = 'Açıklama alanı zorunludur.';
    if (!/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email.trim())) next.email = 'Geçerli bir e-posta adresi gir.';
    return next;
  }

  function onSubmit(event: FormEvent) {
    event.preventDefault();
    const found = validate();
    setErrors(found);
    if (Object.keys(found).length > 0) {
      return;
    }
    // No contact endpoint exists yet (see GAPS.md). Rather than fake a ticket, open
    // the user's mail client with the form contents prefilled — a real action.
    const categoryLabel = CATEGORIES.find((item) => item.value === category)?.label ?? category;
    const body = [
      `Kategori: ${categoryLabel}`,
      studentNumber ? `Öğrenci numarası: ${studentNumber}` : null,
      `E-posta: ${email.trim()}`,
      '',
      description.trim(),
    ]
      .filter(Boolean)
      .join('\n');
    const href = `mailto:destek@sirkadiyen.app?subject=${encodeURIComponent(
      `[${categoryLabel}] ${subject.trim()}`,
    )}&body=${encodeURIComponent(body)}`;
    window.location.href = href;
  }

  return (
    <>
      <a className="skip-link" href="#main">
        İçeriğe geç
      </a>
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

      <main id="main" style={{ padding: '48px 0 80px' }}>
        <div className="container">
          <span className="eyebrow">İletişim</span>
          <h1 style={{ marginTop: 10 }}>Destek ekibiyle iletişime geç</h1>
          <p className="lede" style={{ marginTop: 12 }}>
            Lisans, senkronizasyon veya profil ile ilgili bir sorunun mu var? Aşağıdaki formu doldur,
            en kısa sürede dönüş yapalım.
          </p>

          <div
            style={{
              display: 'grid',
              gridTemplateColumns: 'minmax(0, 1.3fr) minmax(0, 0.9fr)',
              gap: 40,
              alignItems: 'start',
              marginTop: 36,
            }}
          >
            <div className="card card-content">
              <div style={{ marginBottom: 18 }}>
                <Banner tone="info">
                  Bu form şu an bir destek uç noktasına bağlı değildir. Gönder’e bastığında bilgiler
                  e-posta uygulamanda hazırlanır. (Bkz. <code>GAPS.md</code>)
                </Banner>
              </div>

              <form onSubmit={onSubmit} noValidate>
                <div className="field">
                  <label htmlFor="c-category">Talep kategorisi</label>
                  <select
                    className="select-input"
                    id="c-category"
                    value={category}
                    onChange={(event) => setCategory(event.target.value)}
                    aria-invalid={errors.category ? true : undefined}
                  >
                    <option value="">Seçiniz…</option>
                    {CATEGORIES.map((item) => (
                      <option key={item.value} value={item.value}>
                        {item.label}
                      </option>
                    ))}
                  </select>
                  {errors.category && <p className="field-error">{errors.category}</p>}
                </div>

                <div className="field">
                  <label htmlFor="c-subject">Konu</label>
                  <input
                    className="text-input"
                    id="c-subject"
                    value={subject}
                    onChange={(event) => setSubject(event.target.value)}
                    aria-invalid={errors.subject ? true : undefined}
                  />
                  {errors.subject && <p className="field-error">{errors.subject}</p>}
                </div>

                <div className="field">
                  <label htmlFor="c-desc">Açıklama</label>
                  <textarea
                    className="text-input"
                    id="c-desc"
                    value={description}
                    onChange={(event) => setDescription(event.target.value)}
                    placeholder="Sorunu mümkün olduğunca ayrıntılı anlat…"
                    aria-invalid={errors.description ? true : undefined}
                  />
                  {errors.description && <p className="field-error">{errors.description}</p>}
                </div>

                <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '0 16px' }}>
                  <div className="field">
                    <label htmlFor="c-student-no">
                      Öğrenci numarası <span className="muted" style={{ fontWeight: 400 }}>(opsiyonel)</span>
                    </label>
                    <input
                      className="text-input mono"
                      id="c-student-no"
                      inputMode="numeric"
                      maxLength={10}
                      value={studentNumber}
                      onChange={(event) => setStudentNumber(event.target.value.replace(/\D/g, '').slice(0, 10))}
                    />
                  </div>
                  <div className="field">
                    <label htmlFor="c-email">E-posta</label>
                    <input
                      className="text-input"
                      id="c-email"
                      type="email"
                      value={email}
                      onChange={(event) => setEmail(event.target.value)}
                      aria-invalid={errors.email ? true : undefined}
                    />
                    {errors.email && <p className="field-error">{errors.email}</p>}
                  </div>
                </div>

                <button className="btn btn-primary btn-block" type="submit">
                  Talebi e-posta ile hazırla
                </button>
              </form>

              <ImplNote>
                Hedef arayüz: <code>POST /api/contact</code> (henüz yok). Kategoriler ve alanlar plan
                §5.4’e göre hazırdır; bağlanınca yalnızca gönderim değişir.
              </ImplNote>
            </div>

            <div className="stack" style={{ gap: 20 }}>
              <div className="card card-content">
                <h3 style={{ fontSize: 16 }}>Doğrudan iletişim</h3>
                <div className="summary-row" style={{ marginTop: 8 }}>
                  <span className="muted">E-posta</span>
                  <strong>destek@sirkadiyen.app</strong>
                </div>
                <div className="summary-row">
                  <span className="muted">Beklenen yanıt</span>
                  <strong>1–2 iş günü</strong>
                </div>
              </div>
              <div className="card card-content">
                <h3 style={{ fontSize: 16 }}>Önce buraya bakmak ister misin?</h3>
                <p className="muted" style={{ marginTop: 8, fontSize: 13.5 }}>
                  Sık karşılaşılan sorunların çoğu SSS bölümünde yanıtlanıyor.
                </p>
                <Link className="btn btn-secondary btn-sm" href="/#sss" style={{ marginTop: 12 }}>
                  SSS’ye git
                </Link>
              </div>
            </div>
          </div>
        </div>
      </main>

      <SiteFooter />
    </>
  );
}
