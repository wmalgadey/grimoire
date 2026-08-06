---
role: source-material
status: absorbed
absorbed_into: docs/decision-context-overview.md
binding: none — never cite as requirements
dateCreated: 2026-06-21T11:30:00
dateModified: 2026-06-22T10:06:26
title: LLM-Wiki AI-Harness — Konzept und Tool-Vergleich
aliases:
  - LLM-Wiki Harness
  - Wiki-Harness-Konzept
  - AI-Harness für Zettelkasten
categories:
  - formate/konzept
  - agentic/agents
---

# LLM-Wiki AI-Harness — Konzept und Tool-Vergleich

[parent:: [[zettelkasten]]] [day:: [[2026-06-21-Sunday]]] [week:: [[2026/2026-W25]]]

> [!personal]
> Eigene Idee: Eine Harness-Schicht über einem LLM-Wiki. Ingest/Query als menschbediente UIs. Lint/Batch als Hintergrundprozesse die individuelle Artefakte/Audits erzeugen. Konfigurierbar: Persona des Query-Agents, Quellen für Batch (Zettelkasten). Kernbedingung: Ergebnis muss in Obsidian lesbar bleiben — plain Markdown.

---

## Anforderungen (Wolfgangs Kriterien)

| # | Anforderung | Details |
|---|---|---|
| 1 | Harness-Schicht | Über LLM-Wiki, nicht das Wiki selbst |
| 2 | Ingest + Query | Separate Tools/UIs für Menschen |
| 3 | Prinzipien | KISS, Security First, Self-Hosted (wie NanoClaw) |
| 4 | Lint + Batch | Laufen im Hintergrund, erzeugen einzeln lösbare Artefakte/Audits |
| 5 | Konfiguration | Persona/Stil des Query-Agents; Quellen für Batch-Tool (Zettelkasten) |
| 6 | Obsidian-Kompatibilität | Wiki-Dateien bleiben plain Markdown, in Obsidian öffenbar |

---

## Tool-Vergleich

### AnythingLLM (`Mintplex-Labs/anything-llm`)

**Architektur:** React-Frontend + Express-Server + Collector-Service + VectorDB

| Kriterium | Bewertung |
|---|---|
| Obsidian-kompatibel | ❌ Speichert in VectorDB, kein plain Markdown |
| Self-hosted | ✅ Docker, Desktop, vollständige Datensouveränität |
| Ingest/Query getrennt | ✅ Separate Flows |
| Lint/Batch-Hintergrundprozesse | ❌ Nicht vorhanden |
| KISS | ❌ Drei-Service-Architektur, 40+ Provider-Abstraktionen |
| Persona-Konfiguration | ⚠️ Custom Prompts per Workspace, aber keine Persona-Konzept |

**Urteil:** Gutes RAG-System für Teams, aber falsche Grundarchitektur. Kein Harness, kein Markdown.

---

### Dify (`langgenius/dify`)

**Architektur:** Python Flask + Celery + Redis + Postgres + VectorDB + Sandbox (Go)

| Kriterium | Bewertung |
|---|---|
| Obsidian-kompatibel | ❌ Datenbankbasiert |
| Self-hosted | ✅ Docker Compose, Helm/K8s |
| Ingest/Query getrennt | ✅ Knowledge Base + App-Layer |
| Lint/Batch-Hintergrundprozesse | ✅ Celery Workers (aber für Workflow-Execution, nicht Wiki-Qualität) |
| KISS | ❌ 6 Services, massive Komplexität |
| Persona-Konfiguration | ✅ Über Prompt-Engineering und Agent-Konfiguration möglich |

**Urteil:** LLM Application Platform — nicht Knowledge Base. Dify baut Apps über Wissen, nicht Wissen selbst. Zu groß, zu generisch.

---

### nashsu/llm_wiki ← **Nächster Fit**

**Architektur:** Tauri v2 (Rust-Backend + React-Frontend) + LanceDB (optional) + lokaler HTTP-API-Server (Port 19828)

| Kriterium | Bewertung |
|---|---|
| Obsidian-kompatibel | ✅ **Erstellt automatisch `.obsidian/`-Ordner** |
| Self-hosted | ✅ Desktop-App, vollständig lokal |
| Ingest/Query getrennt | ✅ Klar getrennte Operationen |
| Lint/Batch-Hintergrundprozesse | ✅ `runSemanticLint` erzeugt strukturierte Artefakte |
| KISS | ✅ Ein Binary, kein Server nötig |
| Persona-Konfiguration | ⚠️ Szenario-Templates (Research/Personal Growth/etc.) aber keine freie Persona |

**Lint-Artefakt-Format:**

```
---LINT: contradiction | high | Widersprüchliche Datumsangaben in zwei Notizen---
---LINT: orphan | medium | Konzept "Temporal Coupling" erwähnt, Seite fehlt---
---LINT: stale | low | Quelle aus 2022 — neuere Version verfügbar?---
```

Diese sind einzeln lösbar — exakt das gewünschte Muster.

**Weitere Features:**

- SHA256 inkrementeller Cache → nur geänderte Dateien werden neu ingested
- Auto-Watch auf `raw/sources/` → Zettelkasten als Watch-Ordner konfigurierbar
- Lokaler HTTP-API-Server → externe Agenten können zugreifen
- Zwei-Schritt-Ingest: Analyse → Generierung (Chain-of-Thought)
- Deep Research Pipeline: Web-Suche + Synthese → neue Wiki-Seiten

---

## Lücken von llm_wiki gegenüber Wolfgangs Vision

| Lücke | Beschreibung | Lösungsidee |
|---|---|---|
| **Keine freie Persona-Konfiguration** | Query-Agent hat keine konfigurierbare Rolle/Stil | Eigene `purpose.md` + `schema.md` als Persona-Override |
| **Batch ≠ Lint** | Lint ist reaktiv (findet Probleme); ein eigenständiger Batch-Prozess fehlt | Harness-Schicht würde Batch orchestrieren: z.B. "tägliche Zusammenfassungen", "Verbindungsvorschläge" |
| **Desktop-App, kein Server** | Läuft als Desktop-App, nicht als dauerhafter Hintergrunddienst | API-Server (Port 19828) könnte als Brücke dienen |
| **Kein expliziter Source-Config per Batch** | Quellen-Konfiguration ist projektbasiert, nicht per-Operation | Harness würde das konfigurierbar machen |

---

## Architektur-Skizze: Harness-Schicht über llm_wiki

```
┌──────────────────────────────────────────────────────┐
│                   HARNESS LAYER                      │
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────┐  │
│  │  Ingest-UI  │  │  Query-UI   │  │  Audit-UI   │  │
│  │  (manuell)  │  │  (Persona-  │  │  (Artefakte │  │
│  │             │  │  konfigur.) │  │  lösen)     │  │
│  └──────┬──────┘  └──────┬──────┘  └──────┬──────┘  │
│         │                │                │          │
│  ┌──────▼──────────────────────────────────▼──────┐  │
│  │              ORCHESTRATOR                      │  │
│  │  Persona-Config · Source-Router · Batch-Planer │  │
│  └──────────────────┬─────────────────────────────┘  │
└─────────────────────│────────────────────────────────┘
                      │ HTTP API (Port 19828)
┌─────────────────────▼────────────────────────────────┐
│                   nashsu/llm_wiki                    │
│  raw/sources/ (Zettelkasten)                         │
│  wiki/ (plain Markdown, Obsidian-kompatibel)         │
│  Lint · Ingest · Query · Deep Research               │
└──────────────────────────────────────────────────────┘
```

---

## Weiterentwicklung: Git-Native, Headless, Server-First

> [!personal]
> Nächste Design-Iteration: nashsu/llm_wiki ist Desktop-App. Das Ziel ist ein headless Server mit Web-UI und Git als zentraler Komponente — analog zu NanoClaw: `git clone`, konfigurieren, starten.

### Git als Fundament

**Kernidee:** Das Git-Repo IS das Wiki. Nicht Git als Backup, sondern Git als primäre Datenschicht.

| Agent-Operation | Git-Aktion | Commit-Message |
|---|---|---|
| Ingest neue Quelle | `git add wiki/*.md && git commit` | `ingest: Zettelkasten sync 2026-06-21` |
| Lint-Lauf | `git add audits/2026-06-21-lint.md && git commit` | `lint: 3 orphans, 1 contradiction found` |
| Batch-Synthese | `git add wiki/synthesis/*.md && git commit` | `batch: weekly connection suggestions` |
| Human löst Audit | `git add audits/resolved/ && git commit` | `resolve: orphan "Temporal Coupling" → Seite erstellt` |

**Git History = Audit Trail:**  
`git log --oneline` zeigt die vollständige Geschichte aller Agent-Aktionen — wann, was, warum. Kein separates Logging-System nötig.

**Tornhill-Verbindung:** Genau wie Tornhill Git-History nutzt um Code-Qualität zu analysieren, kann man die Git-History des Wikis nutzen um die Wissensbasis selbst zu analysieren: Welche Seiten werden oft von Lint gefunden? Wo erzeugt Batch immer wieder Verbindungen? Das ist **Behavioral Wiki Analysis**.

### Headless-Architektur

```
git clone https://github.com/user/my-wiki-harness
cp config.example.yml config.yml    # LLM-Key, Zettelkasten-Pfad, Persona
docker run -v ./wiki:/wiki wiki-harness
```

**Single-Container-Design (KISS):**

```
┌─────────────────────────────────────┐
│          wiki-harness               │
│                                     │
│  Watcher  →  Agent-Runner  →  Git   │
│                                     │
│  FastAPI Web-UI (optional)          │
│  /ingest  /query  /audit            │
└─────────────────────────────────────┘
         │
    Git-Repo (Volume)
    ├── wiki/          ← plain Markdown (Obsidian-kompatibel)
    ├── raw/sources/   ← Zettelkasten-Sync-Target
    └── audits/        ← Lint/Batch-Artefakte (offen + resolved/)
```

### Stack-Entscheidung (KISS-Kriterium)

| Komponente | Wahl | Begründung |
|---|---|---|
| Core | **Python** | LLM-Libraries (anthropic, openai), einfache Scripts |
| Web-UI | **FastAPI + htmx** | Kein JS-Framework, Server-Side HTML |
| Git-Integration | **subprocess / GitPython** | Kein ORM, direkte Git-Calls |
| Config | **config.yml** | Ein File, kein Env-Var-Chaos |
| Storage | **plain Markdown + Git** | Kein VectorDB required (optional für Semantic Search) |
| Deploy | **Single Docker Container** | Kein Compose mit 6 Services |

### Unterschied zu nashsu/llm_wiki

| Aspekt | nashsu/llm_wiki | Git-Native Harness |
|---|---|---|
| Platform | Desktop (Tauri) | Server/Container |
| Install | App-Download | `git clone` |
| History | SHA256-Cache | Git-History als Audit-Trail |
| UI | Electron-ähnlich | Web-UI im Browser |
| Erweiterbarkeit | Rust/TypeScript | Python-Scripts (hackable) |
| NanoClaw-Pattern | ❌ | ✅ |

---

## Projekt-Architektur (Stand Jun 2026)

> [!personal]
> Eigenes Projekt — NanoClaw-Architektur als Basis, aber zweckgebunden und vereinfacht. `git fork + setup.sh`. Agents in Containern, kommunizieren miteinander. Erstmal kein Telegram — Web-UI als primärer Channel, Telegram/etc. optional später.

### Was aus NanoClaw übernommen wird

| NanoClaw-Konzept | Übernahme ins Wiki-Harness |
|---|---|
| Agents in Containern | ✅ Direkt übernommen |
| Agent-zu-Agent-Kommunikation | ✅ Übernommen, aber via Git statt Message Queue |
| Channel-Konzept | ⚡ Reduziert auf Web-UI (Telegram optional) |
| Agent-Rollen-Definition | ⚡ Vorkonfiguriert (4 feste Rollen) |
| Messaging-Routing | ⚡ Vereinfacht (kein dynamisches Wiring) |
| Permissions-System | ❌ Erstmal weggelassen |

**Kernfrage:** Kann diese Vereinfachung zurück in NanoClaw fließen? Möglicherweise — das Projekt zeigt wie NanoClaw aussieht wenn man sich auf einen Use-Case committed. Könnte als "NanoClaw Starter" / Reference Implementation dienen.

### Vier feste Agent-Rollen

```
┌─────────────────────────────────────────────────────┐
│                    Git-Repo (Volume)                │
│  raw/sources/  wiki/  audits/open/  audits/resolved/│
└──────┬──────────────┬───────────────────────────────┘
       │              │
┌──────▼──────┐  ┌────▼──────────────────────────────┐
│ Ingest-     │  │ Query-Agent                        │
│ Agent       │  │ (Web-UI: /query)                   │
│ watch raw/  │  │ reads wiki/, antwortet mit Persona │
│ → commit    │  └────────────────────────────────────┘
└─────────────┘
       │ (cron)
┌──────▼──────┐  ┌──────────────────────────────────┐
│ Lint-Agent  │  │ Batch-Agent                      │
│ → audits/   │  │ → synthesis, connections         │
│   open/*.md │  │ → wiki/batch/*.md                │
└─────────────┘  └──────────────────────────────────┘
       │
┌──────▼──────────────────────────────────────────────┐
│                 Web-UI (FastAPI + htmx)              │
│  /ingest (form)  /query (chat)  /audit (checklist)  │
└─────────────────────────────────────────────────────┘
```

### Hub-Spoke Architektur (korrigiert Jun 2026)

> [!important]
> Kein CLI nötig — NanoClaw + `/wiki`-Skill + Zettelkasten-Flow funktioniert bereits. Das Projekt professionalisiert diesen Flow: ein Hub-Server der Agent-Container orchestriert und Channels verbindet.

```
                    ┌─────────────────┐
                    │      HUB        │
                    │  (Backend-Server│
                    │  Orchestrierung │
                    │  Channel-Router)│
                    └────────┬────────┘
           ┌─────────────────┼────────────────────┐
     CHANNELS                │              AGENTS
           │                 │                    │
    ┌──────▼──────┐   ┌──────▼──────┐   ┌─────────▼──────┐
    │  Web-UI     │   │  Git-Repo   │   │ Ingest-Agent   │
    │  (primär)   │   │  (State)    │   │ Query-Agent    │
    └─────────────┘   └─────────────┘   │ Lint-Agent     │
    ┌─────────────┐                     │ Batch-Agent    │
    │  Telegram   │                     └────────────────┘
    │  (optional) │
    └─────────────┘
```

**Hub-Aufgaben:**

- Agent-Container orchestrieren (Start/Stop/Health)
- Requests von Channels an den richtigen Agent routen
- Channel-Routing-Konfiguration verwalten
- Auth (wer darf auf den Hub zugreifen)

**Deployment-Ziel:** Nach `docker compose up` ist das System bereit. Keine manuelle Konfiguration der Agents — vorkonfiguriert für den Wiki-Use-Case.

**Erweiterung später:** Telegram-Channel als weiterer Spoke am Hub — ohne Änderung der Agent-Container.

### Was dieses Projekt ist (vs. NanoClaw)

| NanoClaw | Wiki-Harness |
|---|---|
| Generische Agent-Plattform | Zweckgebunden: Knowledge-Management |
| Dynamisches Agent-Provisioning | 4 feste, vorkonfigurierte Agents |
| Viele Channel-Typen gleichzeitig | Web-UI first; Telegram optional |
| Konfigurierbar für viele Use-Cases | Nach Clone: sofort nutzbar |
| Komplexes Wiring/Routing | Hub-Spoke: klare Verantwortlichkeiten |

**NanoClaw bleibt:** Für den bestehenden Telegram-Workflow. Der Wiki-Harness ist das professionalisierte, deploybare System daneben.

### Installation (Ziel)

```bash
git clone https://github.com/user/wiki-harness
cd wiki-harness
cp .env.example .env   # LLM-Key, Zettelkasten-Mount
docker compose up      # fertig
```

---

## Entwicklungsphilosophie — Spec-Driven, ADR-First (Grimoire)

> [!note]
> Gemini-Konversation, 22. Jun 2026. Bezug: Wie Grimoire entwickelt werden soll — kein BDUF, aber klare Leitplanken von Tag 1 an.

### Kein Big Design Up Front

Eine starre "Constitution" ab Tag 1 erzeugt Reibungsverluste in der Frühphase. Trennung ist entscheidend:

**Strategisches DDD — ab Tag 1 verankert:**

- Ubiquitous Language und initiale Bounded Contexts
- Falsche Begriffe im Code erzeugen später teure Refactorings

**Taktisches DDD — später:**

- Komplexe Aggregates, strikte Repositories, Domain Events
- Lohnt sich initial nur in der isolierten Core Domain

### Pragmatisches TDD

Dogmatisches Red-Green-Refactor für jede DTO-Zuweisung bremst den Aufbau.

- **Unit-Tests:** Ausschließlich für komplexe Business-Logik (Entities/Domain Services)
- **Integrationstests:** Für den Rest — via Testcontainers gegen echte Datenbank entlang API-Grenzen → keine Fragilität durch exzessives Mocking

### Leichtgewichtige Constitution: ADRs im Repo

Keine 50-seitige Wiki. Drei initiale ADRs:

| ADR | Inhalt |
|---|---|
| ADR 001 | Architekturstil (z.B. Modularer Monolith) |
| ADR 002 | Testing-Strategie (Integrationstests für API-Contracts, Unit-Tests für Domain-Logic) |
| ADR 003 | Umgang mit Abhängigkeiten (Domain Core ist referenzfrei) |

**Durchsetzung in CI/CD:**

> Konventionen, die nicht automatisiert in der CI/CD-Pipeline brechen, existieren praktisch nicht.

- **NetArchTest.Rules** — Architekturvorgaben als automatisierte Tests (z.B. `Types.InNamespace("Domain").ShouldNot().HaveDependencyOn("Infrastructure")`)
- **Roslyn Analyzer + .editorconfig** — formale Diskussionen aus Pull Requests verbannen

---

### ADRs im Spec-Kit-Workflow (Agentenkontext)

ADRs klinken sich zwischen `/specify` (Was) und `/plan` (Wie) ein. `.specify/memory/constitution.md` ist der permanente System-Prompt für Coding-Agents.

**Regeln in constitution.md:**

- "Before generating a plan.md, you must read all existing ADRs in docs/adr/. The resulting plan must explicitly reference which ADRs constrain the implementation."
- "If the spec.md introduces a new structural pattern, integration point, or cross-cutting concern not covered by existing ADRs, you must draft a new ADR using the MADR format in docs/adr/ before finalizing the plan.md."

**plan.md-Template:** Dedizierte Sektion `## Architectural Constraints & ADRs` — Agent evaluiert bei jedem Planungsschritt die Brücke zwischen Feature und Gesamtarchitektur.

**Workflow:**

1. `/speckit.specify` — rein fachlich, Bounded Contexts, keine Technik
2. `/speckit.plan` — Agent liest Spec + Constitution; bei architektonischer Weichenstellung → erst ADR, dann Plan
3. `/speckit.tasks` — Plan in Tickets

### Test-Driven Architecture

ADRs im Markdown-Format stoppen keinen fehlerhaften Commit. Kopplung von ADR + automatisiertem Test:

Constitution-Regel: "Whenever a new ADR introduces a structural boundary or dependency rule, the first task in tasks.md must be to implement an automated architecture test (e.g., NetArchTest.Rules or Roslyn Analyzers) enforcing this decision."

**Reihenfolge:** ADR schreiben → Architektur-Test (Red) → Feature-Code (Green)

---

### Constitution vs. ADRs — Trennung der Verantwortlichkeiten

**Constitution = Prinzipien (Warum + Was) — unverrückbar:**

| Prinzip | Kernregel |
|---|---|
| **Behavioral Engineering** | "Pit of Success" definieren — KI durch harte Prompts zur Einhaltung von Leitplanken zwingen. Custom Infrastructure ohne ADR-Approval ist verboten. |
| **Observable Engineering** | Observability ist strukturelle Anforderung, kein Afterthought. Jeder Domain Service emittiert semantische Business-Metriken und distributed Traces. Code ohne Instrumentation verletzt die DoD. |

**Observability in constitution.md:**

> "During the /plan phase, the plan.md must include a mandatory 'Observability' section. The AI must explicitly list what business metrics, structured log events, and trace spans the new feature will emit."

**ADRs = Werkzeuge & Schwellenwerte (Wie) — austauschbar:**

Für Behavioral Engineering:

- Azure Policies mit 'Deny'-Effekt für nicht-konforme Cloud-Ressourcen
- CI-Build-Abbruch bei Coverage-Reduktion >1% oder neuen Roslyn-Warnungen

Für Observable Engineering:

- OpenTelemetry statt herstellerspezifischer Application Insights SDKs
- DORA-Metriken + Flaky-Test-Raten via GitHub Actions → Azure Data Explorer

**Offene Frage (Gemini-Konversation):** Wie werden Telemetrie- und Pipeline-Daten (steigende Build-Zeiten, Analyzer-Warnungen, DORA-Metriken) aggregiert, um automatisiert zu reagieren — statt nur passive Dashboards zu befüllen?

---

## Verbindungen

- [[harness-engineering]] — Diese Architektur ist die konkrete Umsetzung des Harness-Konzepts für Wissensmanagement
- [[sensors-for-coding-agents-boeckeler]] — Lint-Artefakte = Böckelers Sensor-Output: einzeln lösbare Findings statt globale Qualitätsbewertung
- [[agent-dx-orchestrator-workflow]] — Run-Directory-Pattern als Inspiration für Batch-Artefakt-Verwaltung
- [[agenten-vollstaendige-transparenz-reflexion]] — Obsidian-Kompatibilität = Transparenz-Bedingung: Agent-Arbeit muss für Menschen lesbar bleiben
- [[code-qualitaetssignale-monitoring]] — Observable Engineering: DORA-Metriken und Code-Signale als Grundlage der ADRs

#LLMWiki #Harness #Obsidian #Zettelkasten #RAG #SelfHosted #NanoClaw #KISS #Ingest #Query #Lint #Git #GitNative #Headless #Python #FastAPI #WebUI #Container #DDD #ADR #SpecKit #TDD #NetArchTest #ObservableEngineering #BehavioralEngineering #Grimoire
