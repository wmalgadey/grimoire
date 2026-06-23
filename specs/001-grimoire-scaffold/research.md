# Research: Grimoire Project Skeleton Setup

**Feature**: `001-grimoire-scaffold` | **Phase**: 0 | **Date**: 2026-06-23

All technology choices are locked by ADRs 001–007. This document resolves implementation-level details for scaffold commands, package versions, and CI configuration patterns.

---

## R-001: NetArchTest.Rules — Version and .NET 9 Compatibility

**Decision**: Use `NetArchTest.Rules` 3.x (latest stable as of 2026-06). Package is compatible with .NET 9 and xUnit 2.x.

**Rationale**: NetArchTest.Rules 3.x introduced fluent API improvements and full .NET 6+ compatibility. It is the de-facto standard for layered architecture enforcement in .NET projects without requiring Roslyn analyzers.

**Key API patterns used in `ArchitectureTests.cs`**:
```csharp
// Verify Core has no Infrastructure references
Types.InAssembly(coreAssembly)
    .Should().NotHaveDependencyOn("Microsoft.EntityFrameworkCore")
    .GetResult().IsSuccessful

// Verify channels implement IChannel
Types.InAssembly(infraAssembly)
    .That().ResideInNamespace("Grimoire.Infrastructure.Channels")
    .Should().ImplementInterface(typeof(IChannel))
    .GetResult().IsSuccessful
```

**Alternatives considered**: Roslyn Analyzers (more powerful but high setup overhead for a scaffold), ArchUnit.NET (less mature .NET ecosystem).

---

## R-002: .NET 9 Multi-Project Solution Scaffold Commands

**Decision**: Use `dotnet new` commands with a `Directory.Build.props` for shared MSBuild settings.

**Rationale**: `Directory.Build.props` is the idiomatic way to share MSBuild properties across all projects in a solution without repeating them per `.csproj`. Placing it at `src/backend/` scopes it to backend only, not the frontend.

**Scaffold sequence**:
```bash
# From repo root
mkdir -p src/backend src/frontend src/agents

cd src/backend
dotnet new sln -n Grimoire
dotnet new webapi -minimal -n Grimoire.Api -o Grimoire.Api
dotnet new classlib -n Grimoire.Core -o Grimoire.Core
dotnet new xunit -n Grimoire.ArchTests -o Grimoire.ArchTests

dotnet sln add Grimoire.Api/Grimoire.Api.csproj
dotnet sln add Grimoire.Core/Grimoire.Core.csproj
dotnet sln add Grimoire.ArchTests/Grimoire.ArchTests.csproj

# Project references
dotnet add Grimoire.Api/Grimoire.Api.csproj reference Grimoire.Core/Grimoire.Core.csproj
dotnet add Grimoire.ArchTests/Grimoire.ArchTests.csproj reference Grimoire.Core/Grimoire.Core.csproj
dotnet add Grimoire.ArchTests/Grimoire.ArchTests.csproj reference Grimoire.Api/Grimoire.Api.csproj

# Add NetArchTest.Rules
dotnet add Grimoire.ArchTests/Grimoire.ArchTests.csproj package NetArchTest.Rules
```

**`Directory.Build.props`** content:
```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <Nullable>enable</Nullable>
    <LangVersion>13</LangVersion>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  </PropertyGroup>
</Project>
```

**Alternatives considered**: `global.json` with `sdk.version` pinning (complementary, not alternative — can be added alongside `Directory.Build.props`).

---

## R-003: Svelte 5 + Vite + TypeScript Scaffold

**Decision**: Use `npm create vite@latest . -- --template svelte-ts` inside `src/frontend/`.

**Rationale**: The official Vite Svelte-TS template targets Svelte 5 as of Vite 6 / 2025+. This is the canonical scaffold path recommended in Svelte 5 docs. TypeScript strict mode is enabled by default in this template.

**Scaffold sequence**:
```bash
cd src/frontend
npm create vite@latest . -- --template svelte-ts
npm install
```

**Post-scaffold adjustments**:
- Verify `svelte.config.js` uses Vite plugin: `import { vitePreprocess } from '@sveltejs/vite-plugin-svelte'`
- Ensure `tsconfig.json` has `"strict": true`
- Add `eslint.config.js` for ESLint 9 flat config with `@typescript-eslint/eslint-plugin`

**package.json scripts** (required by FR-013):
```json
{
  "scripts": {
    "dev": "vite",
    "build": "vite build",
    "lint": "eslint src"
  }
}
```

**Alternatives considered**: `npm create svelte@latest` (SvelteKit scaffold — overkill for a SPA; no SSR needed per spec).

---

## R-004: GitHub Actions Path-Based Triggers

**Decision**: Use `paths` filter under `on.push` and `on.pull_request` in each workflow file.

**Rationale**: GitHub Actions `paths` filters are the standard monorepo CI isolation mechanism. They run the workflow only when files matching the pattern change. This directly satisfies FR-015 and US3-AC3.

**Backend workflow trigger**:
```yaml
on:
  push:
    paths:
      - 'src/backend/**'
      - '.github/workflows/backend.yml'
  pull_request:
    paths:
      - 'src/backend/**'
      - '.github/workflows/backend.yml'
```

**Frontend workflow trigger**:
```yaml
on:
  push:
    paths:
      - 'src/frontend/**'
      - '.github/workflows/frontend.yml'
  pull_request:
    paths:
      - 'src/frontend/**'
      - '.github/workflows/frontend.yml'
```

**Note**: Including the workflow file itself in `paths` ensures CI re-runs when the pipeline definition changes.

**Alternatives considered**: Nx affected (ADR-005 explicitly rejected Nx for this project), manual conditional steps (fragile, not idiomatic).

---

## R-005: OpenTelemetry Scaffolding in .NET 9

**Decision**: Add `OpenTelemetry.Extensions.Hosting` and `OpenTelemetry.Instrumentation.AspNetCore` to `Grimoire.Api.csproj` with minimal `builder.Services.AddOpenTelemetry()` registration.

**Rationale**: Wires the OTel SDK at startup without any exporters, satisfying the Observability section requirement that the SDK is scaffolded. Exporters (OTLP, Prometheus) are added in feature implementations.

**Required packages**:
```
OpenTelemetry.Extensions.Hosting
OpenTelemetry.Instrumentation.AspNetCore
Microsoft.Extensions.Logging (included via ASP.NET Core)
```

**`Program.cs` host lifecycle logging** (mandatory per Observability section):
```csharp
var logger = app.Services.GetRequiredService<ILogger<Program>>();
logger.LogInformation("grimoire.host.started environment={Environment} version={Version}",
    app.Environment.EnvironmentName, "0.1.0-skeleton");

app.Lifetime.ApplicationStopped.Register(() =>
    logger.LogInformation("grimoire.host.stopped environment={Environment}",
        app.Environment.EnvironmentName));
```

**Alternatives considered**: Serilog (valid but adds a third-party dependency before OTel is wired; deferred to a later feature).
