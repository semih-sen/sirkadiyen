# Google source authentication

The schedule worker uses the least-privilege Google Sheets read-only scope:

```text
https://www.googleapis.com/auth/spreadsheets.readonly
```

Client ID and client secret identify the OAuth application but do not authorize
unattended source polling by themselves. Configure exactly one of these modes.

## OAuth refresh-token mode

Required environment variables:

```text
SIRKADIYEN_GOOGLE__CLIENT_ID
SIRKADIYEN_GOOGLE__CLIENT_SECRET
SIRKADIYEN_GOOGLE__SOURCE_REFRESH_TOKEN
```

The refresh token must come from an explicit authorization-code flow that asks
for offline access and the Sheets read-only scope. Never print or commit the
token. The future administration/OAuth callback flow should encrypt it before
database storage and must handle revocation.

## Service-account mode

Set only:

```text
SIRKADIYEN_GOOGLE__SERVICE_ACCOUNT_CREDENTIAL_PATH
```

The credential file must stay outside source control. Share private source
spreadsheets with the service-account email address. Public fixtures do not need
to be made private merely to use this mode.

## Current local state

The repository's `.env` is ignored and is never read by tests or snapshot tools.
The source catalog contains public identifiers and URLs only. Until a refresh
token or service-account credential is configured, use the local XLSX snapshot
tool for parser fixture development; do not weaken the production Sheets adapter
or log secrets to bypass authorization.
