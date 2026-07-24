import type { OnboardingState } from './types';

// Backend is the single source of truth for onboarding state (AI_GUIDELINE §16:
// "Treat backend state as authoritative"). The frontend only maps each state to
// the route that lets the user take the next step; it never decides activation.

export const ROUTES = {
  signIn: '/sign-in',
  license: '/onboarding/license',
  profile: '/onboarding/profile',
  calendar: '/onboarding/calendar',
  sync: '/onboarding/sync',
  dashboard: '/dashboard',
  suspended: '/onboarding/suspended',
} as const;

/** The page a signed-in user in the given onboarding state belongs on. */
export function routeForOnboardingState(state: OnboardingState): string {
  switch (state) {
    case 'LicenseRequired':
      return ROUTES.license;
    case 'ProfileRequired':
      return ROUTES.profile;
    case 'CalendarAuthorizationRequired':
      return ROUTES.calendar;
    case 'ReadyForInitialSync':
    case 'InitialSyncInProgress':
      return ROUTES.sync;
    case 'Active':
      return ROUTES.dashboard;
    case 'Suspended':
      return ROUTES.suspended;
    case 'ActionRequired':
    default:
      // ActionRequired means a managed-calendar problem needs re-authorization;
      // route back to the calendar step where re-consent lives.
      return ROUTES.calendar;
  }
}

export const ONBOARDING_STEPS: { state: OnboardingState; label: string }[] = [
  { state: 'LicenseRequired', label: 'Lisans' },
  { state: 'ProfileRequired', label: 'Profil' },
  { state: 'CalendarAuthorizationRequired', label: 'Takvim izni' },
  { state: 'ReadyForInitialSync', label: 'Senkronizasyon' },
  { state: 'Active', label: 'Hazır' },
];
