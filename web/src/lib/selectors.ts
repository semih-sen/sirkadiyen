import type { SupportedProfileDimension } from '@/lib/types';

/**
 * The schema names dimensions in the contract's language; every screen is Turkish.
 *
 * An unlabelled key falls back to itself rather than being hidden, so a dimension added to the
 * schema before this map is updated shows up visibly unlabelled instead of silently
 * unselectable. This lives here rather than beside one form because several screens pick a
 * cohort, and a second copy of the map is how two of them came to disagree about which
 * dimensions have Turkish names at all.
 */
export const DIMENSION_LABELS: Record<string, string> = {
  practiceGroup: 'Uygulama grubu',
  practiceSubgroup: 'Uygulama alt grubu',
  anatomyGroup: 'Anatomi grubu',
  curriculumGroup: 'Müfredat grubu',
  facultyPracticeGroup: 'Öğretim üyesi uygulama grubu',
  microPathologyGroup: 'Mikrobiyoloji-Patoloji uygulama grubu',
};

export function dimensionLabel(key: string): string {
  return DIMENSION_LABELS[key] ?? key;
}

/**
 * The values a dimension currently offers, given what is already chosen.
 *
 * A dependent dimension can only be judged once its parent is known: a Grade 3 faculty-practice
 * cohort means a different rotation in the A programme than in the B one, so offering all
 * sixteen would let someone state a cohort that does not exist (ADR-099).
 */
export function selectorValues(
  dimension: Pick<SupportedProfileDimension, 'values' | 'dependsOn' | 'valuesByParent'>,
  selectors: Record<string, string>,
): string[] {
  if (!dimension.dependsOn) return dimension.values ?? [];
  const parent = selectors[dimension.dependsOn];
  if (!parent || !dimension.valuesByParent) return [];
  return dimension.valuesByParent[parent] ?? [];
}

/**
 * The selectors that survive changing one dimension: the change itself, minus anything that
 * depended on it.
 *
 * Leaving a child behind is not cosmetic. A stale `facultyPracticeGroup` of `A3` under a
 * curriculum group changed to `3-B` is a cohort no student can be in, and the server would
 * rightly refuse it while the dropdown still showed a plausible-looking pair.
 */
export function applySelector(
  dimensions: SupportedProfileDimension[],
  selectors: Record<string, string>,
  key: string,
  value: string,
): Record<string, string> {
  const next: Record<string, string> = { ...selectors };
  if (value) {
    next[key] = value;
  } else {
    delete next[key];
  }

  // Cleared transitively rather than one level deep. The schema has no grandchildren today, but
  // a rule that quietly depends on that would fail the first time one is added, and the failure
  // would look like a server that refuses a cohort the form says is fine.
  const cleared = new Set([key]);
  let changed = true;
  while (changed) {
    changed = false;
    for (const dimension of dimensions) {
      if (dimension.dependsOn && cleared.has(dimension.dependsOn) && !cleared.has(dimension.key)) {
        cleared.add(dimension.key);
        delete next[dimension.key];
        changed = true;
      }
    }
  }

  return next;
}
