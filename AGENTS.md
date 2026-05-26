# Agent Instructions

## Project Conventions

- This project uses `Mediator.SourceGenerator` / `Mediator.Abstractions`.
- Do not replace it with MediatR.
- Do not suggest or introduce paid MediatR packages.
- When working with mediator pipeline behaviors, requests, handlers, or notifications, follow the APIs from the `Mediator` package family already used in the solution.
- This project uses FastEndpoints for HTTP endpoints.
- When designing request and response contracts, endpoint base classes, validation, route handling, or result handling, consider FastEndpoints capabilities and conventions first.
- Do not assume ASP.NET Core controllers are the default HTTP style unless the user explicitly asks for controllers.

## MassTransit Versioning

- Do not spend time upgrading MassTransit.
- The MassTransit version is intentionally pinned to the latest open-source version available for this project.
- If a package audit or dependency review mentions MassTransit, treat the current pin as deliberate unless the user explicitly asks otherwise.

## Codex-Only Instructions

This section is only for Codex agents. Other agents should ignore it.

- If you need to run `dotnet build`, `dotnet test`, `dotnet restore`, `dotnet run`, or similar .NET commands, ask for elevated permissions first.
- Use the escalation flow instead of trying to work around sandbox or permission limitations.
- Explain briefly why the command is needed when asking for elevated permissions.
