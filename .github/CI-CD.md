# CI/CD conventions

The client and server use the same workflow structure and access policy. Keep their common sections and
`.github/scripts/test-dotnet.sh` aligned when updating CI. Platform-specific build and publication steps
remain in their respective repositories.

| Workflow | Triggers | Purpose |
|---|---|---|
| Tests | Every push and pull request, manual, reusable call | Validate the code; also required before Release builds |
| CodeQL | Push and pull request to `main` or `*/main`, weekly, manual | Scan sources and workflows without building |
| Build | Manual, reusable call from Release | Package the application and upload artifacts |
| Release | Manual | Run Tests, build, then publish a release; defaults to pre-release |

Tests and CodeQL cancel superseded runs for the same workflow and ref. Build queues runs for that workflow
and ref; Release serializes publication across branches. Including the caller workflow in reusable workflow
concurrency groups keeps a manual run separate from a Release run.

Workflows default to `contents: read`. Publication jobs request write access only where needed. Bash is the
default run shell; Windows packaging explicitly uses PowerShell. The shared .NET test runner uses Release
configuration, writes TRX results and a run summary, continues through project failures, and returns failure
if any test command fails. Failed jobs upload available test results.

## Repository settings

These settings are managed in GitHub, separately from workflow YAML:

- Actions enabled, all actions allowed, mandatory action SHA pinning disabled.
- Default workflow token permissions: read; Actions cannot approve pull request reviews.
- Fork pull request approval: **all external contributors** require approval by someone with write access.
- Manual runs require write access; currently the only collaborator is the owner, `puffer-bit`.
- The default branch disallows deletion and force pushes and requires a pull request with passing tests.
  Admins can bypass; required check names follow each repository's actual jobs.

## Client-specific behavior

Tests run on Linux and Windows and build only the test projects, avoiding the desktop media runtime and
Android workload. The client pins .NET SDK 10.0.402 because of the documented 10.0.403 download issue.
CodeQL scans C# and Actions. NativeAOT/JIT selection, GStreamer caching, the Linux glibc baseline, Windows
libwebrtc verification, and Velopack release/development channels remain client-specific.
