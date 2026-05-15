# AGENTS.md

## Dotnet Build Commands

If Codex ever decides to run any dotnet build-related command, it must first ask the user for elevated permissions. This includes commands such as `dotnet build`, `dotnet test`, `dotnet restore`, `dotnet publish`, and any command that invokes MSBuild.

Reason: running these without elevated permissions can produce stale build results in this workspace.
