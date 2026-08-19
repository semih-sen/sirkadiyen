/**
 * The people who operate Sirkadiyen, and the channels users can reach them on.
 *
 * Kept in one place because these values appear in the privacy notice, the terms, the contact
 * page and the licence step at once. A phone number or an address that is right on three of those
 * and stale on the fourth is worse than one that is wrong everywhere: nobody notices the odd one
 * out, and the legal notices are the copies that must not drift.
 */
export interface Operator {
  name: string;
  email: string;
  /** Human-readable, as it should be shown. */
  phone: string;
  /** Digits only, international format — what `tel:` and `wa.me` links need. */
  phoneDigits: string;
}

export const OPERATORS: readonly Operator[] = [
  {
    name: 'Halil Semih Şen',
    email: 'halil.semih.sen@gmail.com',
    phone: '+90 551 056 6754',
    phoneDigits: '905510566754',
  },
  {
    name: 'Abdullah Ceylan',
    email: 'ceylanabdullah711@gmail.com',
    phone: '+90 551 026 6718',
    phoneDigits: '905510266718',
  },
];

/** Every operator address, for a `mailto:` that reaches both. */
export const CONTACT_EMAILS = OPERATORS.map((operator) => operator.email).join(',');

/** The message a licence request opens WhatsApp with, already written for the user. */
export const LICENSE_REQUEST_MESSAGE =
  'Merhaba, Sirkadiyen için lisans kodu istiyorum.';

export function whatsappLink(operator: Operator, message: string): string {
  return `https://wa.me/${operator.phoneDigits}?text=${encodeURIComponent(message)}`;
}
