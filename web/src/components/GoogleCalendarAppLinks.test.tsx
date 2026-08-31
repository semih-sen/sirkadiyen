import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { GoogleCalendarAppLinks } from './GoogleCalendarAppLinks';

describe('GoogleCalendarAppLinks', () => {
  it('states the Google Takvim recommendation and links both official stores', () => {
    render(<GoogleCalendarAppLinks />);

    expect(screen.getByText(/Google Takvim uygulaması üzerinden takip etmeni öneririz/)).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Get it on Google Play' })).toHaveAttribute(
      'href',
      'https://play.google.com/store/apps/details?id=com.google.android.calendar',
    );
    expect(screen.getByRole('link', { name: 'Download on the App Store' })).toHaveAttribute(
      'href',
      'https://apps.apple.com/app/google-calendar/id909319292',
    );
  });

  it('uses the self-hosted official store badge artwork', () => {
    render(<GoogleCalendarAppLinks />);
    // Görseller mağazaların resmî rozetleri, `public/store/` altında barındırılıyor:
    // uzaktan bağlanmak (hotlink) rozeti üçüncü bir sunucunun erişilebilirliğine bağlar.
    expect(screen.getByAltText('Get it on Google Play').getAttribute('src')).toContain('/store/google-play-badge.svg');
    expect(screen.getByAltText('Download on the App Store').getAttribute('src')).toContain('/store/app-store-badge.svg');
  });

  it('opens store links safely in a new tab', () => {
    render(<GoogleCalendarAppLinks variant="plain" />);
    for (const link of screen.getAllByRole('link')) {
      expect(link).toHaveAttribute('target', '_blank');
      expect(link.getAttribute('rel')).toContain('noopener');
    }
  });

  it('renders no card heading in the plain variant', () => {
    const { container } = render(<GoogleCalendarAppLinks variant="plain" />);
    expect(container.querySelector('h3')).toBeNull();
  });
});
