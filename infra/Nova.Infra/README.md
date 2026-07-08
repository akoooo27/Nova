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