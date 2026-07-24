// Thin wrappers over Google Identity Services (GIS). Two distinct flows:
//   1. Sign-in: returns a short-lived ID token to the browser (ADR-052). We post
//      it to /api/auth/google. No client secret, no redirect URI.
//   2. Calendar: the popup "code" flow (initCodeClient) returns a one-time
//      authorization code we post to /api/calendar/authorization (ADR-057). The
//      backend exchanges it with redirect_uri=postmessage.
//
// Both flows require the page origin (e.g. https://localhost:3000) to be listed
// under "Authorized JavaScript origins" for the OAuth client in Google Cloud
// Console. Neither needs a redirect URI, because both are popup/postMessage.

const GIS_SRC = 'https://accounts.google.com/gsi/client';

let gisPromise: Promise<void> | null = null;

/** Loads the GIS script once and resolves when `google.accounts` is available. */
export function loadGoogleIdentityServices(): Promise<void> {
  if (typeof window === 'undefined') {
    return Promise.reject(new Error('Google Identity Services can only load in the browser.'));
  }
  if (window.google?.accounts) {
    return Promise.resolve();
  }
  if (gisPromise) {
    return gisPromise;
  }

  gisPromise = new Promise<void>((resolve, reject) => {
    const existing = document.querySelector<HTMLScriptElement>(`script[src="${GIS_SRC}"]`);
    if (existing) {
      existing.addEventListener('load', () => resolve());
      existing.addEventListener('error', () => reject(new Error('Failed to load Google Identity Services.')));
      return;
    }
    const script = document.createElement('script');
    script.src = GIS_SRC;
    script.async = true;
    script.defer = true;
    script.onload = () => resolve();
    script.onerror = () => reject(new Error('Failed to load Google Identity Services.'));
    document.head.appendChild(script);
  });
  return gisPromise;
}

/**
 * Renders the official Google sign-in button into `parent` and calls `onCredential`
 * with the returned ID token.
 */
export async function renderGoogleSignInButton(
  parent: HTMLElement,
  clientId: string,
  onCredential: (credential: string) => void,
): Promise<void> {
  await loadGoogleIdentityServices();
  window.google!.accounts.id.initialize({
    client_id: clientId,
    callback: (response: { credential?: string }) => {
      if (response.credential) {
        onCredential(response.credential);
      }
    },
    ux_mode: 'popup',
    auto_select: false,
  });
  window.google!.accounts.id.renderButton(parent, {
    theme: 'outline',
    size: 'large',
    text: 'continue_with',
    shape: 'pill',
    logo_alignment: 'left',
  });
}

/**
 * Runs the Calendar consent popup and resolves with the one-time authorization
 * code. `scope` and `clientId` come from GET /api/calendar/authorization/options.
 */
export async function requestCalendarAuthorizationCode(
  clientId: string,
  scope: string,
): Promise<string> {
  await loadGoogleIdentityServices();
  return new Promise<string>((resolve, reject) => {
    const codeClient = window.google!.accounts.oauth2.initCodeClient({
      client_id: clientId,
      scope,
      ux_mode: 'popup',
      callback: (response: { code?: string; error?: string }) => {
        if (response.error) {
          reject(new Error(`Google authorization was not completed: ${response.error}`));
          return;
        }
        if (!response.code) {
          reject(new Error('Google did not return an authorization code.'));
          return;
        }
        resolve(response.code);
      },
    });
    codeClient.requestCode();
  });
}

// Minimal typings for the parts of GIS we use.
declare global {
  interface Window {
    google?: {
      accounts: {
        id: {
          initialize: (config: {
            client_id: string;
            callback: (response: { credential?: string }) => void;
            ux_mode?: 'popup' | 'redirect';
            auto_select?: boolean;
          }) => void;
          renderButton: (parent: HTMLElement, options: Record<string, unknown>) => void;
          prompt: () => void;
        };
        oauth2: {
          initCodeClient: (config: {
            client_id: string;
            scope: string;
            ux_mode?: 'popup' | 'redirect';
            callback: (response: { code?: string; error?: string }) => void;
          }) => { requestCode: () => void };
        };
      };
    };
  }
}
