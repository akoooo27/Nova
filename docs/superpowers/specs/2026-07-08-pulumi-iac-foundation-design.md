# Pulumi IaC Foundation + chat-library-files Bucket — Design

**Date:** 2026-07-08
**Status:** Approved (pending spec review)

## Context

Nova is a .NET 10 Aspire microservices monorepo. All infrastructure today is
**local-only**: `Nova.AppHost` orchestrates Postgres, Redis, and RabbitMQ as
containers, and secrets are Aspire `AddParameter(..., secret: true)` values
backed by .NET user-secrets. There is **no cloud footprint and no
Infrastructure-as-Code**.

This is the first step toward managing real cloud resources declaratively. The
goal is to stand up a Pulumi foundation that future work grows into (more
buckets, and later a chat/Library microservice), while proving the
secret-management story end-to-end. To keep the first step small and verifiable,
the only resource provisioned is a single private S3 bucket.

An earlier idea to provision a placeholder Lambda was **dropped** — the first
resource is the bucket only.

## Goals

- A C#/.NET Pulumi program in the repo, isolated from the .NET app build.
- **Pulumi Cloud** as the state + secret backend.
- **Pulumi ESC** as the secure key-management layer, holding the AWS credentials
  Pulumi uses to deploy. No AWS keys on disk or in the repo.
- One private, encrypted S3 bucket named `chat-library-files` (Pulumi
  auto-suffixed), intended to later hold user-uploaded "Library" files.
- Documented CLI workflow so the user (or CI) can `preview` / `up` / `destroy`.

## Non-goals

- No Lambda / compute resources.
- No API Gateway, CloudFront, or networking.
- No migration of Nova's *existing* app secrets (Auth0, API keys, DB creds) into
  ESC — that is a future step this foundation enables.
- No CI/CD pipeline wiring — deploys are run manually for now.
- No automated tests (per `AGENTS.md`, tests are not added without an explicit
  request; verification is manual/CLI).

## Decisions

| Decision | Choice | Rationale |
| --- | --- | --- |
| Pulumi language | **C# / .NET** | Matches the repo; team already knows it; future chat Lambda can be .NET too. |
| Backend | **Pulumi Cloud** (app.pulumi.com) | Managed state + server-side secret encryption; baseline for team use. |
| Secret management | **Pulumi ESC** environment holding AWS creds | Pulumi's dedicated key-management product; scales to all Nova secrets later. |
| ESC → AWS auth | **Static AWS access keys stored in ESC** | Simplest start; keys live only in ESC, never on disk. OIDC is a documented future upgrade. |
| Cloud / region | **AWS `eu-central-1`** | User-specified. |
| Bucket name | `chat-library-files`, **Pulumi auto-suffixed** | S3 names are globally unique; auto-suffix avoids collisions. |
| Solution membership | **Excluded from `Nova.slnx`** | Infra has its own build/deploy lifecycle; keeps app `dotnet build`/CI free of Pulumi packages. |

## Design

### 1. Repository layout

```
infra/
  Nova.Infra/
    Nova.Infra.csproj      # dotnet Pulumi program; refs Pulumi + Pulumi.Aws
    Program.cs             # declares the bucket + hardening + outputs
    Pulumi.yaml            # project name, runtime: dotnet, ESC env reference
    Pulumi.dev.yaml        # dev stack config (aws:region etc.)
    README.md              # prerequisites + preview/up/destroy workflow
    .gitignore             # ignore bin/ obj/ (state lives in Pulumi Cloud)
```

`infra/Nova.Infra` is a standalone Pulumi program. It is **not** added to
`Nova.slnx` and is **not** referenced by `Nova.AppHost`, so the application build
is unchanged.

### 2. State & secret backend

- **Pulumi Cloud** is the backend. Stack: `<org>/nova-infra/dev`. Stack state and
  any `--secret` config are encrypted server-side.
- **Pulumi ESC** environment (proposed name `nova/aws-dev`) holds:
  - AWS credentials as **secrets** (`aws_access_key_id`, `aws_secret_access_key`).
  - Region and provider config exposed to Pulumi via the `pulumiConfig` /
    `environmentVariables` blocks (`aws:region = eu-central-1`,
    `AWS_ACCESS_KEY_ID`, `AWS_SECRET_ACCESS_KEY`, `AWS_REGION`).
- `Pulumi.yaml` references the ESC environment so `pulumi up` pulls credentials
  from ESC at deploy time. Credentials never appear in the repo or on disk.

### 3. Control / data flow

```
ESC env nova/aws-dev ──(AWS creds + aws:region)──► pulumi up  (reads Pulumi.yaml env ref)
                                                       │
                                                       ▼
                                      aws.s3 private bucket: chat-library-files-<suffix>
```

### 4. The bucket (`Program.cs`)

Provisioned resources, all in `eu-central-1`:

- **`aws.s3.BucketV2`** — logical name `chat-library-files`; physical name
  auto-suffixed by Pulumi for global uniqueness.
- **`aws.s3.BucketPublicAccessBlock`** — `blockPublicAcls`,
  `blockPublicPolicy`, `ignorePublicAcls`, `restrictPublicBuckets` all `true`.
- **`aws.s3.BucketServerSideEncryptionConfigurationV2`** — SSE-S3 (`AES256`).
- **`aws.s3.BucketOwnershipControls`** — `BucketOwnerEnforced` (ACLs disabled;
  modern private default).
- Versioning is **off** initially (a one-liner to enable later if the Library
  feature needs object history).

**Stack outputs:** `bucketName`, `bucketArn`.

### 5. Verification

Manual, via CLI (no automated tests):

1. `pulumi preview` — shows exactly one bucket (+ the public-access-block,
   encryption, ownership resources) to be created.
2. `pulumi up` — creates them.
3. Confirm private + encrypted:
   - `aws s3api get-public-access-block --bucket <name>` → all four `true`.
   - `aws s3api get-bucket-encryption --bucket <name>` → `AES256`.
4. `pulumi destroy` — clean teardown.

### 6. Prerequisites (who runs what)

The implementation delivers the Pulumi program, `Pulumi.yaml`/stack config, the
ESC environment definition, and a README with exact commands. **Running the
deploy requires the user's own accounts** and cannot be done from the
implementation session:

- Pulumi Cloud account + `pulumi login`; a Pulumi organization for ESC.
- `pulumi` and `esc` CLIs installed; .NET 10 SDK (already present).
- An AWS account and an IAM user with S3 permissions; its access key/secret
  stored in the ESC environment (done once via `esc env set --secret`).

## Future extensions (out of scope, enabled by this foundation)

- Additional buckets and, later, the chat/Library **Lambda** microservice.
- Upgrade ESC → AWS auth from static keys to **OIDC** dynamic login.
- Additional stacks (`staging`, `prod`) and CI-driven `pulumi up`.
- Migrating Nova's existing app secrets into ESC.

## Open questions

None outstanding. ESC environment name (`nova/aws-dev`) and the exact IAM policy
scope will be finalized during implementation planning.
