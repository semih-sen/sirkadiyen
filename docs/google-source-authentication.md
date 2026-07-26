# Google source authentication

The schedule worker reads sources with one credential and two read-only scopes:

```text
https://www.googleapis.com/auth/spreadsheets.readonly
https://www.googleapis.com/auth/drive.readonly
```

One credential, because a program may be published as a sheet one year and as a
Drive file the next; two grants would mean two things to keep alive and two ways
for polling to half-work. Neither scope can write, so nothing in the acquisition
path can modify a source document.

`drive.readonly` is broader than it looks: it covers every file the credential
can see. Drive has no narrower scope that can download a file somebody else
shared — `drive.file` reaches only files the application itself created — so
service-account mode is the least-privilege mode in practice, because the account
sees exactly the documents that were shared with it (ADR-083).

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
for offline access and **both** scopes above. A grant is fixed at the moment it
is issued: a refresh token minted before Drive acquisition existed carries the
Sheets scope alone, and Drive answers 403 for every file until the grant is
re-issued. That failure is reported as an access-denied acquisition naming the
missing scope, not as a missing file.

Never print or commit the token. The future administration/OAuth callback flow
should encrypt it before database storage and must handle revocation.

## Service-account mode

Set only:

```text
SIRKADIYEN_GOOGLE__SERVICE_ACCOUNT_CREDENTIAL_PATH
```

The credential file must stay outside source control. Share private source
spreadsheets **and Drive documents** with the service-account email address:
scopes authorize the kind of access, and sharing authorizes the file. A document
that was never shared is reported as not found, because a credential cannot tell
"no such file" from "not yours to see".

Public fixtures do not need to be made private merely to use this mode.

A service account asserts its own scopes, so adding `drive.readonly` needs no
re-consent — unlike the refresh-token mode above. It does need the **Drive API
enabled** on the Cloud project the credential belongs to; while it is not, every
Drive request is refused with 403 and reported as access denied.

## Current local state

The repository's `.env` is ignored and is never read by tests or snapshot tools.
The source catalog contains public identifiers and URLs only. Until a refresh
token or service-account credential is configured, use the local XLSX and DOCX
snapshot tools for parser fixture development; do not weaken the production
Sheets or Drive adapters, and do not log secrets to bypass authorization. The
access token is attached by a delegating handler and is never held by the client
that builds the request, so it cannot reach a log line or an exception message.
