# Sheets Fixtures

This directory contains real or sanitized schedule source files used to design and test parser profiles.

## Safety

Before committing a file:

- remove personal student data
- remove private email addresses
- remove access tokens or credentials
- remove unrelated private comments
- verify that sharing the file is permitted

Faculty names and public course information may remain when required for accurate parsing, subject to repository privacy policy.

## Purpose

Fixtures are used for:

- parser development
- golden-file tests
- structural regression tests
- source inventory
- change simulation
- documentation of source quirks

A fixture is test evidence. Do not edit an original fixture merely to make the parser pass.

When a corrected or changed source is needed, add a new version.

## Recommended directory structure

```text
sheets/
├── README.md
├── source-manifest.md
├── grade-1/
│   ├── tr/
│   │   ├── annual/
│   │   └── practice/
│   ├── en/
│   │   ├── annual/
│   │   └── practice/
│   └── anatomy/
├── grade-2/
│   ├── tr/
│   │   ├── annual/
│   │   └── practice/
│   ├── en/
│   │   ├── annual/
│   │   └── practice/
│   ├── anatomy/
│   │   ├── autumn/
│   │   └── spring/
│   └── vertical-corridor/
├── grade-3/
│   ├── tr/
│   │   ├── group-a/
│   │   │   ├── annual/
│   │   │   ├── bedside/
│   │   │   └── faculty-practice/
│   │   └── group-b/
│   └── en/
└── shared/
    └── amphitheatre/
```

## Naming convention

Use:

```text
{academic-year}_{grade}_{language}_{source-type}_{group}_{captured-date}_v{n}.xlsx
```

Example:

```text
2026-2027_g2_tr_anatomy-autumn_all_2026-09-14_v1.xlsx
2026-2027_g3_tr_bedside_a_2026-10-03_v2.xlsx
```

For raw API snapshots:

```text
2026-2027_g2_tr_annual_all_2026-09-14_v1.snapshot.json
```

## Never use row numbers as lesson identity

Rows and columns may move between source versions.

Cell addresses are evidence, not permanent identifiers.

## Source note file

Each structurally distinct source family should have a nearby note file:

```text
SOURCE_NOTES.md
```

Suggested contents:

```markdown
# Source notes

## Source family

## Parser profile

## Meaningful sheets

## Header detection

## Date rules

## Time rules

## Group rules

## Merge behavior

## Color or formatting semantics

## Known anomalies

## Expected ignored regions

## Example lesson evidence

## Open questions
```

## Golden files

Expected canonical outputs should live under parser tests, not beside production fixtures.

Recommended structure:

```text
tests/parser/fixtures/...
tests/parser/golden/...
```

Each golden output must record:

- fixture name
- parser profile
- parser version
- expected records
- expected warnings
- expected metrics

## Fixture mutations

Do not only test pristine files. Add controlled mutations:

- inserted blank row
- moved header
- changed time separator
- removed merge
- repeated date
- omitted date requiring propagation
- instructor split across lines
- title spelling variation
- cancellation marker
- room change
- group label variation
- accidental mass deletion

Every discovered production parsing bug should become a regression fixture.
