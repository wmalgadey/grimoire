# Grimoire Hub runtime image. See deploy/README.md.
#
# One image: the Hub, the three agent runtimes, the built frontend, and the external
# converter the ingest pipeline dispatches to. Two build stages feed it.
#
# The frontend is served by the Hub itself (HubEndpoints.MapSingleOriginFrontend), so the
# deployment is a single container and any proxy in front of it is ordinary infrastructure
# with one upstream — nothing that needs to know Grimoire's routes.
#
# Build context is the repository root:
#   docker build -f deploy/Dockerfile -t grimoire-hub .

# ---------------------------------------------------------------------------
# Stage 1 — backend and agent runtimes
# ---------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS backend

WORKDIR /src

# The version this image reports for itself (ADR-027). GitVersion owns every version number
# in this repository, and it reads the git history to produce one — history this stage does
# not have and should not have: .dockerignore excludes .git/, which is what keeps the build
# context small and the layer cache from invalidating on every commit. So the version is
# computed outside and handed in. backend/Directory.Build.props detects the missing
# repository on its own and steps aside; this argument is what fills the gap.
#
# Only InformationalVersion, deliberately: it is a free-form string, so whatever the caller
# can honestly say — a GitVersion SemVer, a `git describe`, nothing at all — lands here
# without a malformed value failing the build. It is also the one the Hub's help header
# reads (HubCliHelpProvider).
ARG GRIMOIRE_VERSION=0.0.0-nogit
ENV GRIMOIRE_INFORMATIONAL_VERSION=${GRIMOIRE_VERSION}

# Only the four projects the image needs are restored and built — the test projects are
# never copied into the build context stage, so the image build does not carry them.
# Directory.Build.targets is what makes an agent project distribute its runtime (below).
COPY backend/Directory.Build.props backend/Directory.Build.targets backend/Directory.Packages.props ./backend/
COPY backend/src/ ./backend/src/

RUN dotnet restore backend/src/Grimoire.Hub/Grimoire.Hub.csproj \
 && dotnet restore backend/src/Grimoire.IngestAgent/Grimoire.IngestAgent.csproj \
 && dotnet restore backend/src/Grimoire.QueryAgent/Grimoire.QueryAgent.csproj \
 && dotnet restore backend/src/Grimoire.LintAgent/Grimoire.LintAgent.csproj

# ADR-022: each agent's own build distributes its complete runtime — worker DLL,
# deps.json, runtimeconfig.json, every dependency assembly and its Instructions/ — into
# $(GrimoireAgentDir)/<agent-id>/. Pointing that at the image's /app/.grimoire/agents is
# the redirect ADR-022 designed, and it is the ONLY thing that populates the directory:
# the Hub never builds an agent (ADR-022 rule R4), it only spawns from here.
#
# The three agent projects are built individually rather than via the solution so the
# test projects stay out of the image build.
RUN dotnet build backend/src/Grimoire.IngestAgent/Grimoire.IngestAgent.csproj \
        --configuration Release --no-restore -p:GrimoireAgentDir=/app/.grimoire/agents/ \
        -p:InformationalVersion="$GRIMOIRE_INFORMATIONAL_VERSION" \
 && dotnet build backend/src/Grimoire.QueryAgent/Grimoire.QueryAgent.csproj \
        --configuration Release --no-restore -p:GrimoireAgentDir=/app/.grimoire/agents/ \
        -p:InformationalVersion="$GRIMOIRE_INFORMATIONAL_VERSION" \
 && dotnet build backend/src/Grimoire.LintAgent/Grimoire.LintAgent.csproj \
        --configuration Release --no-restore -p:GrimoireAgentDir=/app/.grimoire/agents/ \
        -p:InformationalVersion="$GRIMOIRE_INFORMATIONAL_VERSION"

# appsettings.json is copied to the publish output by the project itself; ADR-022 makes it
# the sole source of default paths, and the Hub pins its ContentRoot to the directory the
# assembly sits in, so it must stay beside Grimoire.Hub.dll.
RUN dotnet publish backend/src/Grimoire.Hub/Grimoire.Hub.csproj \
        --configuration Release --no-restore -o /app/hub \
        -p:InformationalVersion="$GRIMOIRE_INFORMATIONAL_VERSION"

# ---------------------------------------------------------------------------
# Stage 2 — frontend (static SPA)
# ---------------------------------------------------------------------------
FROM oven/bun:1.3.14 AS frontend

WORKDIR /src/frontend

COPY frontend/package.json frontend/bun.lock ./
RUN bun install --frozen-lockfile

COPY frontend/ ./
RUN bun run build

# ---------------------------------------------------------------------------
# Stage 3 — runtime
# ---------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime

# markitdown is a real external dependency of the ingest convert step
# (MarkItDownOptions.DefaultExecutablePath = "markitdown"). Without it on PATH, conversion
# fails at dispatch time rather than at startup.
#
# Pinned, and pinned in all three places at once — here, .github/workflows/ci.yml and
# .devcontainer/Dockerfile. An image rebuilt months from now must contain the converter the
# suite was green against, and a pin that only one of the three carries is worse than none:
# CI would then verify a version the deployment does not run. Moving the version means
# moving all three.
# curl is here for the container healthcheck only; the ASP.NET runtime image ships
# without it.
RUN apt-get update \
 && apt-get install -y --no-install-recommends python3-pip curl \
 && rm -rf /var/lib/apt/lists/* \
 && pip3 install --break-system-packages --no-cache-dir "markitdown==0.1.7"

WORKDIR /app

COPY --from=backend /app/hub/ /app/
COPY --from=backend /app/.grimoire/agents/ /app/.grimoire/agents/
# Beside the Hub assembly, because the Hub pins its content root there (ADR-022). Read-only
# at runtime like the rest of /app; the Hub mounts it only when this directory exists, so a
# source checkout without it is served by `bun run dev` instead.
COPY --from=frontend /src/frontend/build/ /app/wwwroot/

# The writable roots are relocated here by Grimoire__Paths__* in compose.yaml and are
# volume mount points. They are created in the image so the Hub's auto-creation never has
# to make the mount point itself.
#
# Ownership follows the arbitrary-uid convention (the pattern OpenShift requires and
# Kubernetes `runAsUser` benefits from): owned by root **group 0**, group-writable. A
# fresh named volume inherits these bits from the image, so the Hub can write to one as
# the default uid below — and equally as any other uid, provided its primary group stays
# 0. That is what makes `runAsUser: <anything>` work on a cluster without an init
# container fixing permissions first.
RUN mkdir -p /var/lib/grimoire/data /var/lib/grimoire/wiki /var/lib/grimoire/memory \
 && chown -R $APP_UID:0 /var/lib/grimoire \
 && chmod -R g=u /var/lib/grimoire

ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_NOLOGO=true

EXPOSE 8080

# Rootless by default. Numeric rather than the `app` name on purpose: Kubernetes'
# `runAsNonRoot` admission check reads the image's USER and can only verify it when it is
# a number. Nothing in /app is written at runtime — the secrets file and the agent runtime
# are read-only inputs — so the Hub needs no write access outside /var/lib/grimoire.
USER $APP_UID

# No command name: starting the web server is what the Hub's default command does
# (ADR-023). Every other Hub CLI command is reachable with `docker compose exec`.
ENTRYPOINT ["dotnet", "/app/Grimoire.Hub.dll"]
