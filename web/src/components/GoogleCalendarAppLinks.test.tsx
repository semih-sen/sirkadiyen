import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { GoogleCalendarAppLinks } from './GoogleCalendarAppLinks';

describe('GoogleCalendarAppLinks', () => {
  it('states the Google Takvim recommendation and links both official stores', () => {
    render(<GoogleCalendarAppLinks />);

    expect(screen.getByText(/Google Takvim uygulaması üzerinden takip etmeni öneririz/)).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /Google Play/ })).toHaveAttribute(
      'href',
      'https://play.google.com/store/apps/details?id=com.google.android.calendar',
    );
    expect(screen.getByRole('link', { name: /App Store/ })).toHaveAttribute(
      'href',
      'https://apps.apple.com/app/google-calendar/id909319292',
    );
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
