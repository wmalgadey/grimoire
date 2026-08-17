FROM mcr.microsoft.com/devcontainers/dotnet:1-10.0

# The base image ships a stale apt source for Yarn Classic (unused — this project
# uses Bun) whose keyring no longer matches dl.yarnpkg.com's current signing key,
# breaking every subsequent `apt-get update` (including the ones devcontainer
# Features run). Remove it rather than work around it downstream.
RUN rm -f /etc/apt/sources.list.d/yarn.list

# markitdown (CLI) is a real, external conversion dependency several
# Grimoire.IntegrationTests cases exercise directly (see IngestConvertStepTests
# "FR-015: default path converts (real markitdown)"). Installed and pinned the same way
# .github/workflows/ci.yml and deploy/Dockerfile install it, so the devcontainer, CI and
# the shipped image all exercise one version.
RUN apt-get update \
    && apt-get install -y --no-install-recommends python3-pip \
    && rm -rf /var/lib/apt/lists/* \
    && pip3 install --break-system-packages "markitdown==0.1.7"
