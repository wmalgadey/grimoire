# Grimoire Hub runtime image (ADR-027).
#
# Two build stages and two runnable targets. The backend build also distributes the three
# agent runtimes into the image; the frontend build produces the static SPA. `runtime`
# carries the Hub, the agents, and the external converter the ingest pipeline dispatches
# to; `proxy` carries the SPA and the same-origin routing.
#
# Build context is the repository root:
#   docker build -f deploy/Dockerfile --target runtime -t grimoire-hub .
#   docker build -f deploy/Dockerfile --target proxy   -t grimoire-proxy .

# ---------------------------------------------------------------------------
# Stage 1 — backend and agent runtimes
# ---------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS backend

WORKDIR /src

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
 && dotnet build backend/src/Grimoire.QueryAgent/Grimoire.QueryAgent.csproj \
        --configuration Release --no-restore -p:GrimoireAgentDir=/app/.grimoire/agents/ \
 && dotnet build backend/src/Grimoire.LintAgent/Grimoire.LintAgent.csproj \
        --configuration Release --no-restore -p:GrimoireAgentDir=/app/.grimoire/agents/

# appsettings.json is copied to the publish output by the project itself; ADR-022 makes it
# the sole source of default paths, and the Hub pins its ContentRoot to the directory the
# assembly sits in, so it must stay beside Grimoire.Hub.dll.
RUN dotnet publish backend/src/Grimoire.Hub/Grimoire.Hub.csproj \
        --configuration Release --no-restore -o /app/hub

# ---------------------------------------------------------------------------
# Stage 2 — frontend (static SPA, ADR-027 F1)
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
# (MarkItDownOptions.DefaultExecutablePath = "markitdown"), installed the same way CI and
# the devcontainer install it. Without it on PATH, conversion fails at dispatch time
# rather than at startup.
# curl is here for the container healthcheck only; the ASP.NET runtime image ships
# without it.
RUN apt-get update \
 && apt-get install -y --no-install-recommends python3-pip curl \
 && rm -rf /var/lib/apt/lists/* \
 && pip3 install --break-system-packages --no-cache-dir markitdown

WORKDIR /app

COPY --from=backend /app/hub/ /app/
COPY --from=backend /app/.grimoire/agents/ /app/.grimoire/agents/

# The writable roots are relocated here by Grimoire__Paths__* in compose.yaml (ADR-027 S1)
# and are volume mount points. They are created in the image so the Hub's auto-creation
# never has to make the mount point itself.
RUN mkdir -p /var/lib/grimoire/data /var/lib/grimoire/wiki /var/lib/grimoire/memory

ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_NOLOGO=true

EXPOSE 8080

# No command name: starting the web server is what the Hub's default command does
# (ADR-023). Every other Hub CLI command is reachable with `docker compose exec`.
ENTRYPOINT ["dotnet", "/app/Grimoire.Hub.dll"]

# ---------------------------------------------------------------------------
# Stage 4 — proxy (the published origin, ADR-027 F1)
# ---------------------------------------------------------------------------
# Same Dockerfile, second target: compose builds `runtime` for the hub service and this
# for the proxy, so the SPA and the Hub can never be built from different commits.
FROM caddy:2-alpine AS proxy

COPY deploy/Caddyfile /etc/caddy/Caddyfile
COPY --from=frontend /src/frontend/build/ /srv/

EXPOSE 8080
