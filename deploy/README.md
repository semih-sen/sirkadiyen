# Deployment

`.github/workflows/deploy.yml` builds every component on a GitHub-hosted runner
and pushes only the compiled output to the Ubuntu server over SSH. The server
holds no source, no `.git` directory, no GitHub credential and no build
toolchain; it never reaches out to GitHub.

```
GitHub runner                                   Ubuntu server
─────────────                                   ─────────────
dotnet publish  --self-contained ──rsync──▶ /srv/sirkadiyen/api/releases/<sha>
dotnet publish  --self-contained ──rsync──▶ /srv/sirkadiyen/worker/releases/<sha>
pip wheel       (wheelhouse)     ──rsync──▶ /srv/sirkadiyen/parser/releases/<sha>
next build      (standalone)     ──rsync──▶ /srv/sirkadiyen/web/releases/<sha>
dotnet ef migrations script      ──rsync──▶ /srv/sirkadiyen/migrations/<sha>.sql
                                    │
                                    └──ssh──▶ sudo sirkadiyen-activate <component> <sha>
                                              (symlink swap + systemctl restart)
```

Deployment order is migrations → parser → worker → API → frontend.

## What the server does need

"No build tools" is not "no runtimes". The host needs exactly this much:

| Requirement | Why | Avoidable? |
| --- | --- | --- |
| none for .NET | the API and worker are published `--self-contained`, so the runtime ships inside the artifact | already avoided |
| `python3.13` + `python3.13-venv` | a virtualenv hard-codes the absolute path of the interpreter that created it and cannot be rsynced; the venv is built on the host from pushed wheels, offline (`pip install --no-index`) | no |
| `nodejs` (22.x) | the Next.js standalone bundle is `server.js`, which needs a Node runtime; no npm, no lockfile, no `node_modules` install | no |
| `postgresql-client` | applies the idempotent migration script with `psql`, instead of putting the EF tooling on the host | no |
| `rsync`, `curl` | transfer and health probes | no |

## 1. One-time server preparation

Run as a sudo-capable user on the Ubuntu host.

```bash
sudo apt-get update
sudo apt-get install -y rsync curl ca-certificates gnupg postgresql-client
```

```bash
# Node 22 for the Next.js standalone server.
curl -fsSL https://deb.nodesource.com/setup_22.x | sudo -E bash -
sudo apt-get install -y nodejs
```

```bash
# Python 3.13 for the parser (pyproject pins >=3.13,<3.14). Ubuntu 24.04 ships
# 3.12, so the deadsnakes PPA supplies the interpreter. Ubuntu 25.10 or later
# can skip the PPA and install python3.13 directly.
sudo add-apt-repository -y ppa:deadsnakes/ppa
sudo apt-get update
sudo apt-get install -y python3.13 python3.13-venv
```

```bash
# Service account: no login shell, no home directory of its own.
sudo useradd --system --create-home --home-dir /srv/sirkadiyen --shell /usr/sbin/nologin sirkadiyen

# Deploy account: the identity GitHub authenticates as. It owns the release
# directories so rsync can write them, and it is the only account in the
# sirkadiyen group that a human key can reach.
sudo useradd --create-home --shell /bin/bash deploy
sudo usermod -aG sirkadiyen deploy
```

```bash
# Directory layout. `current` is a symlink into releases/, swapped atomically.
sudo mkdir -p /srv/sirkadiyen/{api,worker,parser,web}/releases
sudo mkdir -p /srv/sirkadiyen/migrations
sudo mkdir -p /srv/sirkadiyen/shared/env
sudo mkdir -p /srv/sirkadiyen/shared/secrets
sudo mkdir -p /srv/sirkadiyen/shared/dataprotection-keys

# The schedule source catalog the administration panel edits (ADR-114). It sits
# outside every release directory on purpose: an administrative edit must not be
# reverted by the next deployment. sirkadiyen-activate seeds the file from the
# worker artifact when it does not exist yet, and never overwrites it.
sudo mkdir -p /srv/sirkadiyen/shared/config

# The published student lists the profile lookup searches (ADR-132) live in the
# same directory. Nothing writes this one - it holds locations and column
# layouts, not people - but it sits here so a corrected layout can be installed
# without a deployment. Copy config/student-rosters.json from the repository.

# The deploy account writes releases and migration scripts; nothing else.
sudo chown -R deploy:sirkadiyen /srv/sirkadiyen/{api,worker,parser,web} /srv/sirkadiyen/migrations
sudo chmod -R 2775 /srv/sirkadiyen/{api,worker,parser,web} /srv/sirkadiyen/migrations

# The API and the worker must share one Data Protection key ring (ADR-058):
# the worker decrypts what the API encrypted.
sudo chown -R sirkadiyen:sirkadiyen /srv/sirkadiyen/shared
sudo chmod 700 /srv/sirkadiyen/shared/dataprotection-keys
sudo chmod 750 /srv/sirkadiyen/shared/env /srv/sirkadiyen/shared/secrets

# The catalog is written by the API service account, not by the deploy account:
# it changes at runtime through an audited admin action, never through rsync.
sudo chown -R sirkadiyen:sirkadiyen /srv/sirkadiyen/shared/config
sudo chmod 750 /srv/sirkadiyen/shared/config
```

Copy the environment files into place. These hold every application secret, and
they are the reason no application secret has to exist in GitHub:

```bash
sudo install -o sirkadiyen -g sirkadiyen -m 0640 /dev/null /srv/sirkadiyen/shared/env/common.env
sudo install -o sirkadiyen -g sirkadiyen -m 0640 /dev/null /srv/sirkadiyen/shared/env/api.env
sudo install -o sirkadiyen -g sirkadiyen -m 0640 /dev/null /srv/sirkadiyen/shared/env/worker.env
sudo install -o sirkadiyen -g sirkadiyen -m 0640 /dev/null /srv/sirkadiyen/shared/env/parser.env
sudo install -o sirkadiyen -g sirkadiyen -m 0640 /dev/null /srv/sirkadiyen/shared/env/web.env
# Read by sirkadiyen-migrate, which runs as root; the service account has no
# business holding a schema-changing credential.
sudo install -o root -g root -m 0600 /dev/null /srv/sirkadiyen/shared/env/migrations.env
```

Populate them from `.env.example` in the repository root, split by consumer:

- `common.env` — everything both hosts read: `SIRKADIYEN_DATABASE__CONNECTION_STRING`,
  `SIRKADIYEN_REDIS__CONNECTION_STRING`, `SIRKADIYEN_SECURITY__TOKEN_ENCRYPTION_KEY`,
  `SIRKADIYEN_DATAPROTECTION__KEY_RING_PATH=/srv/sirkadiyen/shared/dataprotection-keys`,
  the `SIRKADIYEN_GOOGLE__*` values and the `Logging__LogLevel__*` overrides.
- `api.env` — `ASPNETCORE_URLS=http://127.0.0.1:5080`, `SIRKADIYEN_WORKER__BASE_URL=http://127.0.0.1:5081`,
  `SIRKADIYEN_LICENSING__HASH_KEY`.
- `worker.env` — `SIRKADIYEN_WORKER__HEALTH_URL=http://127.0.0.1:5081`,
  `SIRKADIYEN_PARSER__BASE_URL=http://127.0.0.1:8000`, and the polling,
  validation, diff, sync and retention values.
- `parser.env` — may be empty; the parser reads no secrets today.
- `web.env` — may be empty. `BACKEND_ORIGIN` deliberately does **not** belong
  here: Next evaluates `rewrites()` at build time, so it is set by the workflow.
- `migrations.env` — a single line,
  `SIRKADIYEN_MIGRATION_DSN=postgresql://user:password@127.0.0.1:5432/sirkadiyen`.

Install the scripts, the units and the sudo rule (from a checkout on your
workstation, or by pasting the file contents):

```bash
sudo install -o root -g root -m 0755 deploy/bin/sirkadiyen-activate /usr/local/bin/sirkadiyen-activate
sudo install -o root -g root -m 0755 deploy/bin/sirkadiyen-migrate  /usr/local/bin/sirkadiyen-migrate
sudo install -o root -g root -m 0755 deploy/bin/sirkadiyen-health   /usr/local/bin/sirkadiyen-health

sudo install -o root -g root -m 0644 deploy/systemd/sirkadiyen-*.service /etc/systemd/system/
sudo systemctl daemon-reload
sudo systemctl enable sirkadiyen-parser sirkadiyen-worker sirkadiyen-api sirkadiyen-web
```

```bash
# Validate the sudo rule BEFORE installing it. A malformed sudoers file can lock
# every account out of sudo.
sudo visudo -c -f deploy/sudoers.d/sirkadiyen-deploy
sudo install -o root -g root -m 0440 deploy/sudoers.d/sirkadiyen-deploy /etc/sudoers.d/sirkadiyen-deploy
```

The services will not start until the first deployment has created the `current`
symlinks, which is expected — run the workflow once with **Deploy → Run workflow
→ deploy everything**.

## 2. SSH key setup

Generate the key **on your workstation**, not on the server and not in CI: the
private half should exist in exactly two places, your machine (briefly) and the
GitHub secret.

```bash
ssh-keygen -t ed25519 -a 100 -C "github-actions-deploy@sirkadiyen" -f ~/.ssh/sirkadiyen_deploy -N ""
```

`-N ""` leaves it without a passphrase, which is required: a non-interactive
runner cannot type one. That is precisely why the key is scoped to the `deploy`
account and to three sudo commands.

Install the public half on the server:

```bash
ssh-copy-id -i ~/.ssh/sirkadiyen_deploy.pub deploy@YOUR_SERVER
```

Then restrict it. Edit `/home/deploy/.ssh/authorized_keys` on the server and
prefix the line with source and capability limits:

```
from="140.82.112.0/20,143.55.64.0/20,192.30.252.0/22,185.199.108.0/22",no-agent-forwarding,no-port-forwarding,no-X11-forwarding,no-pty ssh-ed25519 AAAA... github-actions-deploy@sirkadiyen
```

The `from=` list is GitHub's published runner range and is optional — hosted
runners also use wide Azure ranges, so verify against
`https://api.github.com/meta` (the `actions` array) or drop `from=` if you use
the standard hosted pool. The `no-*` options are not optional; they cost nothing
and remove the key's usefulness for tunnelling into the network.

Capture the server's host key so the pipeline pins it instead of trusting blindly:

```bash
ssh-keyscan -t ed25519 -p 22 YOUR_SERVER
```

Verify the fingerprint it prints against what the server reports locally
(`ssh-keygen -lf /etc/ssh/ssh_host_ed25519_key.pub`) before you trust it.

## 3. GitHub configuration

Create a **production environment** first (Settings → Environments → New
environment → `production`), and add required reviewers if you want a manual
gate on every deployment. Put the secrets on the environment rather than the
repository, so no other workflow can read them.

Secrets (Settings → Environments → production → Add secret):

| Secret | Value |
| --- | --- |
| `DEPLOY_HOST` | the server hostname or IP |
| `DEPLOY_USER` | `deploy` |
| `DEPLOY_SSH_KEY` | the entire contents of `~/.ssh/sirkadiyen_deploy`, including the `BEGIN`/`END` lines |
| `DEPLOY_KNOWN_HOSTS` | the `ssh-keyscan` output from above |

Variables (Settings → Secrets and variables → Actions → Variables):

| Variable | Value |
| --- | --- |
| `BACKEND_ORIGIN` | `http://127.0.0.1:5080` — baked into the frontend's rewrite table at build time |
| `DEPLOY_PORT` | only if SSH is not on 22 |

Then delete the private key from your workstation, or move it to a password
manager:

```bash
shred -u ~/.ssh/sirkadiyen_deploy
```

## Rollback

Releases are kept three deep. To go back:

```bash
ls -1t /srv/sirkadiyen/api/releases
sudo sirkadiyen-activate api <previous-sha>
```

`sirkadiyen-activate` also rolls back on its own when a service fails to stay up
for ten seconds after the restart. Migrations do not roll back — an idempotent
forward script has no inverse, so a schema change that must be undone needs a
new migration.

## The admin panel says the source catalog is read-only

The symptom is the catalog editor on `/admin/sources` reporting that
`/srv/sirkadiyen/shared/config/schedule-sources.json` is not writable. It is
almost never a file mode or an owner: the API runs as `sirkadiyen`, and the
file is installed owned by that account.

Under `ProtectSystem=strict` systemd mounts the entire file system read-only for
the unit except the paths it lists in `ReadWritePaths`, and no ownership on the
directory changes that. Two things must both be true:

```bash
# 1. The directory exists and belongs to the service account.
sudo install -d -o sirkadiyen -g sirkadiyen -m 0750 /srv/sirkadiyen/shared/config

# 2. The running unit grants it. Check what the unit actually has, not what the
#    repository says - a unit file is installed by hand, not by the pipeline.
systemctl show sirkadiyen-api -p ReadWritePaths
```

If the second command does not list `/srv/sirkadiyen/shared/config`, reinstall
`deploy/systemd/sirkadiyen-api.service` (or add a drop-in), then:

```bash
sudo systemctl daemon-reload
sudo systemctl restart sirkadiyen-api
```

A `ReadWritePaths` entry pointing at a directory that does not exist keeps the
unit from starting at all, so create the directory before restarting. Both hosts
must also agree on `SIRKADIYEN_SOURCES__CATALOG_PATH`: the worker reads the file
the API writes, and a mismatch means the panel edits a document the worker never
loads. `systemctl show <unit> -p Environment` is the authority, because a value
in `/srv/sirkadiyen/shared/env/*.env` and one in the unit's own `Environment=`
can disagree.
