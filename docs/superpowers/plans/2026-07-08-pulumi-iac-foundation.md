# Pulumi IaC Foundation + chat-library-files Bucket Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Stand up a C#/.NET Pulumi program (isolated from the app build) that provisions one private, encrypted S3 bucket `chat-library-files`, using Pulumi Cloud as backend and Pulumi ESC as the AWS-credential source.

**Architecture:** A standalone `infra/Nova.Infra` Pulumi (dotnet runtime) project, deliberately shadowed off the repo-root MSBuild props and Central Package Management so it inherits none of the app's analyzers or version pins. The Pulumi program declares a private S3 bucket hardened with a public-access block, bucket-owner-enforced ownership, and SSE-S3 encryption. AWS credentials are never on disk: a Pulumi ESC environment (`nova/aws-dev`) holds them and injects them into the Pulumi run; region is plain stack config.

**Tech Stack:** Pulumi (dotnet), Pulumi.Aws provider, .NET 10, Pulumi Cloud backend, Pulumi ESC, AWS S3, `pulumi` + `esc` CLIs.

## Global Constraints

- **Language / framework:** C#, `net10.0`, `OutputType=Exe`. Copy verbatim.
- **Isolation:** `infra/Nova.Infra` is **NOT** added to `Nova.slnx` and is **NOT** referenced by `Nova.AppHost`. It must inherit **neither** the root `Directory.Build.props` (analyzers, `TreatWarningsAsErrors=true`, `GenerateDocumentationFile=true`, `ErrorOr`/`SonarAnalyzer` injection) **nor** the root `Directory.Packages.props` Central Package Management.
- **Backend:** Pulumi Cloud (app.pulumi.com). Stack: `<your-org>/nova-infra/dev`.
- **Secret management:** AWS access key + secret live **only** in Pulumi ESC environment `nova/aws-dev`. No AWS credentials in the repo, in `Pulumi.*.yaml`, or on disk.
- **Region:** `eu-central-1` (set as `aws:region` stack config, not in ESC).
- **Bucket:** logical name `chat-library-files`, **physical name auto-suffixed by Pulumi** (do not set an explicit `Bucket`/`BucketName`). Private: all four public-access-block flags `true`, `ObjectOwnership = BucketOwnerEnforced`, SSE-S3 (`AES256`). Versioning off. Stack outputs `bucketName` and `bucketArn`.
- **No automated tests** (per `AGENTS.md`): verification is `dotnet build` (agent-run) plus `pulumi`/`aws` CLI checks (user-run). Do not add xunit/test projects.
- **`AGENTS.md` note:** running `dotnet` commands may require elevated permissions in some harnesses — request them via the escalation flow if `dotnet build` is blocked.

**Who runs what:** Tasks 1–3 are **agent-run** (create/commit in-repo files; only local `dotnet build` needed). Tasks 4–5 are **run by you (the user)** — they require your Pulumi Cloud login and AWS account and cannot be executed from the coding session.

## File Structure

| Path | Create/Modify | Responsibility |
| --- | --- | --- |
| `infra/Directory.Build.props` | Create | Empty stopper — halts MSBuild walk-up so the Pulumi project inherits none of the root props/analyzers. |
| `infra/Directory.Packages.props` | Create | Disables Central Package Management for the `infra/` subtree so the csproj pins its own versions. |
| `infra/Nova.Infra/Nova.Infra.csproj` | Create | The Pulumi dotnet project (Exe, net10.0) + `Pulumi` / `Pulumi.Aws` package refs. |
| `infra/Nova.Infra/Program.cs` | Create | Declares the bucket + hardening resources and stack outputs. |
| `infra/Nova.Infra/Pulumi.yaml` | Create | Pulumi project manifest (name, `runtime: dotnet`). |
| `infra/Nova.Infra/Pulumi.dev.yaml` | Create | `dev` stack config: ESC environment import + `aws:region`. |
| `infra/Nova.Infra/esc/aws-dev.yaml` | Create | Checked-in **reference** definition of the ESC environment (placeholder creds; real values applied out-of-band). |
| `infra/Nova.Infra/README.md` | Create | Prerequisites + the exact preview/up/destroy workflow. |

---

### Task 1: Scaffold the isolated Pulumi .NET project (build-green, no cloud)

**Files:**
- Create: `infra/Directory.Build.props`
- Create: `infra/Directory.Packages.props`
- Create: `infra/Nova.Infra/Nova.Infra.csproj`
- Create: `infra/Nova.Infra/Program.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: a buildable `infra/Nova.Infra` project with `Pulumi` + `Pulumi.Aws` referenced and **no** inherited analyzers/CPM. Later tasks add resources to `Program.cs` and Pulumi config beside the csproj.

- [ ] **Step 1: Create `infra/Directory.Build.props`** (empty stopper — its mere presence prevents MSBuild from importing the repo-root `Directory.Build.props`)

```xml
<Project>
  <!--
    Intentionally empty. Its presence stops MSBuild from walking up to the
    repo-root Directory.Build.props, so the Pulumi infra project does NOT
    inherit the app's SonarAnalyzer/ErrorOr injection, TreatWarningsAsErrors,
    AnalysisMode=All, or GenerateDocumentationFile. Infra has its own lifecycle.
  -->
</Project>
```

- [ ] **Step 2: Create `infra/Directory.Packages.props`** (shadows the root file; turns Central Package Management OFF for `infra/`)

```xml
<Project>
  <PropertyGroup>
    <!--
      Shadows the repo-root Directory.Packages.props (the nearest file wins).
      CPM is disabled here so infra/Nova.Infra pins its own package versions
      directly in the csproj, decoupled from the app's version set.
    -->
    <ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>
  </PropertyGroup>
</Project>
```

- [ ] **Step 3: Create `infra/Nova.Infra/Nova.Infra.csproj`** (no package refs yet — Step 5 adds them via the CLI so versions are pinned to the latest resolved stable)

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <RootNamespace>Nova.Infra</RootNamespace>
  </PropertyGroup>

</Project>
```

- [ ] **Step 4: Create `infra/Nova.Infra/Program.cs`** (minimal empty stack — proves the toolchain compiles before any resources exist)

```csharp
using System.Collections.Generic;
using Pulumi;

return await Deployment.RunAsync(() =>
{
    return new Dictionary<string, object?>();
});
```

- [ ] **Step 5: Add the Pulumi packages (pins latest stable into the csproj)**

Run:
```bash
cd infra/Nova.Infra
dotnet add package Pulumi
dotnet add package Pulumi.Aws
cd -
```
Expected: two `info : PackageReference for package 'Pulumi' ... added` messages; `Nova.Infra.csproj` now contains `<PackageReference Include="Pulumi" Version="3.x.x" />` and `<PackageReference Include="Pulumi.Aws" Version="6.x.x" />` (exact versions are whatever is current).

- [ ] **Step 6: Restore + build to confirm the toolchain and isolation compile**

Run: `dotnet build infra/Nova.Infra/Nova.Infra.csproj`
Expected: `Build succeeded.` with `0 Error(s)`. (If your harness blocks `dotnet`, request elevated permission per `AGENTS.md`.)

- [ ] **Step 7: Verify the project is isolated from the app's packages/analyzers**

Run: `dotnet list infra/Nova.Infra/Nova.Infra.csproj package`
Expected: the listing shows `Pulumi` and `Pulumi.Aws` (plus their transitives) and does **NOT** list `ErrorOr` or `SonarAnalyzer.CSharp`. This confirms the root props were not inherited.

- [ ] **Step 8: Commit**

```bash
git add infra/Directory.Build.props infra/Directory.Packages.props infra/Nova.Infra/Nova.Infra.csproj infra/Nova.Infra/Program.cs
git commit -m "feat(infra): scaffold isolated Pulumi .NET project"
```

---

### Task 2: Declare the private encrypted bucket + Pulumi project/stack config

**Files:**
- Modify: `infra/Nova.Infra/Program.cs`
- Create: `infra/Nova.Infra/Pulumi.yaml`
- Create: `infra/Nova.Infra/Pulumi.dev.yaml`

**Interfaces:**
- Consumes: the buildable project from Task 1.
- Produces: a Pulumi program whose `dev` stack, once deployed, exposes outputs `bucketName` (`Output<string>`) and `bucketArn` (`Output<string>`), and a stack config that imports ESC environment `nova/aws-dev` and sets `aws:region = eu-central-1`.

- [ ] **Step 1: Replace `infra/Nova.Infra/Program.cs` with the bucket declaration**

```csharp
using System.Collections.Generic;
using Pulumi;
using Pulumi.Aws.S3;
using Pulumi.Aws.S3.Inputs;

return await Deployment.RunAsync(() =>
{
    // Logical name "chat-library-files"; Pulumi auto-suffixes the physical S3
    // name (e.g. chat-library-files-a1b2c3d) for global uniqueness because no
    // explicit Bucket/BucketName is set.
    var bucket = new BucketV2("chat-library-files");

    // Block every avenue of public access.
    _ = new BucketPublicAccessBlock("chat-library-files-public-access-block", new BucketPublicAccessBlockArgs
    {
        Bucket = bucket.Id,
        BlockPublicAcls = true,
        BlockPublicPolicy = true,
        IgnorePublicAcls = true,
        RestrictPublicBuckets = true,
    });

    // Disable ACLs entirely; the bucket owner owns every object.
    _ = new BucketOwnershipControls("chat-library-files-ownership", new BucketOwnershipControlsArgs
    {
        Bucket = bucket.Id,
        Rule = new BucketOwnershipControlsRuleArgs
        {
            ObjectOwnership = "BucketOwnerEnforced",
        },
    });

    // Server-side encryption at rest (SSE-S3 / AES256).
    _ = new BucketServerSideEncryptionConfigurationV2("chat-library-files-encryption", new BucketServerSideEncryptionConfigurationV2Args
    {
        Bucket = bucket.Id,
        Rules =
        {
            new BucketServerSideEncryptionConfigurationV2RuleArgs
            {
                ApplyServerSideEncryptionByDefault = new BucketServerSideEncryptionConfigurationV2RuleApplyServerSideEncryptionByDefaultArgs
                {
                    SseAlgorithm = "AES256",
                },
            },
        },
    });

    return new Dictionary<string, object?>
    {
        ["bucketName"] = bucket.Bucket,
        ["bucketArn"] = bucket.Arn,
    };
});
```

Note: the resource shapes above are the Pulumi.Aws v6 S3 API. If `dotnet add package` resolved a different major version and a type/property name differs, let the build error guide the exact rename — the resource set (BucketV2 + public-access-block + ownership-controls + SSE-config-V2) is unchanged.

- [ ] **Step 2: Create `infra/Nova.Infra/Pulumi.yaml`**

```yaml
name: nova-infra
runtime: dotnet
description: Nova cloud infrastructure managed with Pulumi.
```

- [ ] **Step 3: Create `infra/Nova.Infra/Pulumi.dev.yaml`** (imports the ESC environment for creds; region is plain, non-secret config)

```yaml
environment:
  - nova/aws-dev
config:
  aws:region: eu-central-1
```

- [ ] **Step 4: Build to confirm the resource graph compiles**

Run: `dotnet build infra/Nova.Infra/Nova.Infra.csproj`
Expected: `Build succeeded.` with `0 Error(s)`. (Compilation validates the Pulumi.Aws types offline; `pulumi preview` against AWS happens in Task 5.)

- [ ] **Step 5: Commit**

```bash
git add infra/Nova.Infra/Program.cs infra/Nova.Infra/Pulumi.yaml infra/Nova.Infra/Pulumi.dev.yaml
git commit -m "feat(infra): declare private encrypted chat-library-files bucket"
```

---

### Task 3: Add the ESC environment reference file + infra README

**Files:**
- Create: `infra/Nova.Infra/esc/aws-dev.yaml`
- Create: `infra/Nova.Infra/README.md`

**Interfaces:**
- Consumes: the stack config from Task 2 (which imports `nova/aws-dev`).
- Produces: an in-repo, non-secret **reference** for the ESC environment that Task 4 applies to Pulumi Cloud, and operator docs for Tasks 4–5.

- [ ] **Step 1: Create `infra/Nova.Infra/esc/aws-dev.yaml`** (the desired ESC definition; the `REPLACE_WITH_...` values are user-supplied credentials applied in Task 4, never committed with real values)

```yaml
# Reference definition for the Pulumi ESC environment `nova/aws-dev`.
# This file documents the environment's shape and is copied into Pulumi Cloud
# in Task 4. Real credential values are set there, NOT stored in this repo.
values:
  aws:
    accessKeyId:
      fn::secret: "REPLACE_WITH_AWS_ACCESS_KEY_ID"
    secretAccessKey:
      fn::secret: "REPLACE_WITH_AWS_SECRET_ACCESS_KEY"
  environmentVariables:
    AWS_ACCESS_KEY_ID: ${aws.accessKeyId}
    AWS_SECRET_ACCESS_KEY: ${aws.secretAccessKey}
```

- [ ] **Step 2: Create `infra/Nova.Infra/README.md`**

````markdown
# Nova Infrastructure (Pulumi)

C#/.NET Pulumi program for Nova's cloud resources. Isolated from the app build:
not in `Nova.slnx`, and it shadows the repo-root `Directory.Build.props` /
`Directory.Packages.props` so it inherits none of the app's analyzers or version
pins.

## Current resources

- **`chat-library-files`** — private S3 bucket (`eu-central-1`), physical name
  auto-suffixed. All public access blocked, `BucketOwnerEnforced`, SSE-S3
  (AES256). Intended to hold user-uploaded "Library" files later.

## Prerequisites (one-time)

- `pulumi` and `esc` CLIs installed; .NET 10 SDK.
- A Pulumi Cloud account and organization: `pulumi login`.
- An AWS account and an IAM user with S3 permissions; its access key + secret
  are stored in the ESC environment below — never on disk or in this repo.

## Secret management (Pulumi ESC)

AWS credentials live only in the ESC environment `nova/aws-dev`. `Pulumi.dev.yaml`
imports it, so `pulumi up` injects the credentials at deploy time.

```bash
# Create the environment and paste the reference definition, then replace the
# REPLACE_WITH_... credential placeholders with real values.
esc env init nova/aws-dev
esc env edit nova/aws-dev            # paste contents of ./esc/aws-dev.yaml, set real creds
esc env open nova/aws-dev            # verify AWS_ACCESS_KEY_ID / AWS_SECRET_ACCESS_KEY resolve (redacted)
```

## Deploy

```bash
cd infra/Nova.Infra
pulumi stack init <your-org>/nova-infra/dev   # first time only
pulumi preview                                # review planned bucket + hardening
pulumi up                                     # create it
pulumi stack output                           # bucketName, bucketArn
```

## Verify private + encrypted

```bash
BUCKET=$(pulumi stack output bucketName)
aws s3api get-public-access-block --bucket "$BUCKET"   # all four flags true
aws s3api get-bucket-encryption --bucket "$BUCKET"     # AES256
```

## Tear down

```bash
pulumi destroy
```
````

- [ ] **Step 3: Commit**

```bash
git add infra/Nova.Infra/esc/aws-dev.yaml infra/Nova.Infra/README.md
git commit -m "docs(infra): add ESC environment reference and infra README"
```

---

### Task 4: Provision the Pulumi ESC environment — **run by you**

> Requires your Pulumi Cloud login and AWS credentials. Not executable from the coding session.

**Interfaces:**
- Consumes: `infra/Nova.Infra/esc/aws-dev.yaml` (Task 3), your AWS IAM access key/secret.
- Produces: ESC environment `nova/aws-dev` in Pulumi Cloud exposing `AWS_ACCESS_KEY_ID` / `AWS_SECRET_ACCESS_KEY`.

- [ ] **Step 1: Log in to Pulumi Cloud**

Run: `pulumi login`
Expected: `Logged in to pulumi.com as <user>`.

- [ ] **Step 2: Create the ESC environment**

Run: `esc env init nova/aws-dev`
Expected: `Environment created: nova/aws-dev`. (If your org uses a different ESC project name, adjust the `nova/` prefix here **and** the `environment:` entry in `Pulumi.dev.yaml` to match `esc env ls`.)

- [ ] **Step 3: Apply the definition with real credentials**

Run: `esc env edit nova/aws-dev`
Action: paste the contents of `infra/Nova.Infra/esc/aws-dev.yaml`, replacing `REPLACE_WITH_AWS_ACCESS_KEY_ID` and `REPLACE_WITH_AWS_SECRET_ACCESS_KEY` with your IAM user's real values, then save.

- [ ] **Step 4: Verify the credentials resolve**

Run: `esc env open nova/aws-dev`
Expected: output includes an `environmentVariables` block with `AWS_ACCESS_KEY_ID` and `AWS_SECRET_ACCESS_KEY` present (values shown as `[secret]`).

---

### Task 5: Initialize the stack, deploy, and verify — **run by you**

> Requires the ESC environment from Task 4 and your AWS account.

**Interfaces:**
- Consumes: the Pulumi program + stack config (Tasks 1–2), ESC env `nova/aws-dev` (Task 4).
- Produces: the deployed `chat-library-files-<suffix>` bucket and stack outputs `bucketName` / `bucketArn`.

- [ ] **Step 1: Initialize the dev stack**

Run:
```bash
cd infra/Nova.Infra
pulumi stack init <your-org>/nova-infra/dev
```
Expected: `Created stack '<your-org>/nova-infra/dev'`.

- [ ] **Step 2: Preview (confirms creds come from ESC, not disk)**

Run: `pulumi preview`
Expected: a plan to **create** 4 resources — the `BucketV2`, the public-access-block, the ownership-controls, and the SSE configuration. No AWS credential prompt (they resolve from ESC). If it errors with "no credentials", re-check Task 4.

- [ ] **Step 3: Deploy**

Run: `pulumi up` (confirm `yes`)
Expected: `Resources: 4 created`; the `Outputs:` section prints `bucketName` and `bucketArn`.

- [ ] **Step 4: Verify the bucket is private and encrypted**

Run:
```bash
BUCKET=$(pulumi stack output bucketName)
aws s3api get-public-access-block --bucket "$BUCKET" --region eu-central-1
aws s3api get-bucket-encryption --bucket "$BUCKET" --region eu-central-1
```
Expected: `get-public-access-block` shows all four flags `true`; `get-bucket-encryption` shows `SSEAlgorithm: AES256`.

- [ ] **Step 5 (optional): Confirm clean teardown works**

Run: `pulumi destroy` (confirm `yes`)
Expected: `Resources: 4 deleted`. Re-run `pulumi up` afterward if you want to keep the bucket.

---

## Self-Review

**Spec coverage** (against `2026-07-08-pulumi-iac-foundation-design.md`):
- C#/.NET Pulumi program → Tasks 1–2. ✅
- Isolated from `Nova.slnx` + root props/CPM → Task 1 (shadow files, no slnx edit) + Global Constraints. ✅
- Pulumi Cloud backend → Task 5 (`pulumi login`, stack `<org>/nova-infra/dev`). ✅
- Pulumi ESC holding static AWS keys, none on disk → Tasks 3–4, `Pulumi.dev.yaml` import. ✅
- Private, SSE-S3, all-public-access-blocked, `BucketOwnerEnforced`, auto-suffixed `chat-library-files` in `eu-central-1` → Task 2 + region in `Pulumi.dev.yaml`. ✅
- Outputs `bucketName`/`bucketArn` → Task 2 Step 1. ✅
- Verification via `dotnet build` + CLI, no tests → Tasks 1/2 builds, Task 5 CLI checks. ✅
- README with workflow → Task 3. ✅

**Placeholder scan:** The only `REPLACE_WITH_...` tokens are intentional user-supplied credential fields in a template file, with explicit instructions (Task 4 Step 3) — not plan gaps. `<your-org>` is a user-specific value, flagged as such. No `TBD`/`TODO`/"handle edge cases".

**Type consistency:** `bucket.Id` used as `Bucket` input across all three child resources; outputs `bucketName`/`bucketArn` named identically in Task 2 and Tasks 3/5 verification. ESC env id `nova/aws-dev` identical in `Pulumi.dev.yaml`, `esc/aws-dev.yaml` header, and Tasks 3–4.
