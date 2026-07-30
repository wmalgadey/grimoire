---
role: personal-learning-log
status: living
language: de (exempt from the English policy — clearly marked personal log)
binding: none — outside the SDD flow; never cite in specs, plans, or ADRs
sdd_usage: updated via /dev-log
---

# Dev Experience Log: Spec-Kit Lernreise in Grimoire

## Ziel dieses Dokuments

Dieses Log dokumentiert den Verlauf, wie ich mich mit Spec-Kit vertraut gemacht habe und versucht habe, ein vollständiges (potenziell komplexes) Projekt aufzubauen.

Startpunkt der inhaltlichen Idee war die KI-Konversation in docs/project-conversation.md.

## Format

Jeder Eintrag beginnt mit `### [JJJJ-MM-DD] <Typ> | <Kurzfassung>`, gefolgt von einer
ausführlichen Nachricht (Fließtext, Bullets, Code, Ergebnisse). Ein Eintrag transportiert
genau **eine** Erfahrung — mehrere Erfahrungen am selben Tag stehen als eigene Einträge
mit demselben Datum, nicht zusammengefasst in einem. Die Timeline ist **neueste zuerst**:
jeder neue Eintrag wird oben angefügt, ältere Einträge rücken nach unten.

## Timeline

### [2026-07-30] Process | Offene Ideen als GitHub Issues statt in Docs

- Offene Fragen aus `docs/decision-context-overview.md` (§2, §10, §11) und liegengebliebene `tasks.md`-Reste lagen bisher einfach nur in Dokumenten rum, ohne dass die je wer konsequent abgearbeitet oder geschlossen hat.
- Drei Kategorien hat das LLM ausgewöhlt: `spec-candidate` (neue Idee, noch kein Spec), `tail-tasks` (Rest einer schon spezifizierten Feature) und `housekeeping` (Branch-/Repo-Pflege, braucht keinen Spec).
- Mein Ziel ist es, die offenen Punkte der Umsetzung während der Arbeit direkt zu erfassen. Erstmal ohne Code-Änderung.

---

### [2026-07-30] Insight | Query-Modus im Eingabe-Prompt

- Der neue "Query"-Modus im Eingabe-Prompt der Claude-Code Integration in VS Code lässt mich die laufende Aufgabe nachträglich anpassen: Das LLM nimmt den Zusatz-Prompt an, verarbeitet ihn parallel zur laufenden Bearbeitung und baut ihn entweder direkt ein oder führt ihn am Ende aus.
- Genau das hatte ich mir schon vom `/btw`-Feature erhofft. Das hatte tatsächlich gewissen Einfluss auf die laufende Ausführung, ist inzwischen aber quasi zu einem zweiten Agenten geworden, der auch direkt mit mir interagiert — nicht mehr nur ein Seiteneinwurf in die laufende Aufgabe.
- Das scheint im Terminal nicht so gut zu gehen! Evtl. muss man da wieder mit `/btw` arbeiten!

---

### [2026-07-27] Insight | Max-Abo, SDD-Flow und paralleles Arbeiten in mehreren Worktrees

- Mit dem Wechsel auf das Claude Code Max-Abo macht "Programmieren lassen" nochmal deutlich mehr Spaß — kein ständiges Rechnen mit dem Session-Limit im Kopf mehr.
- Es ist einfach befriedigend: Ich sage dem LLM etwas, der Code entsteht, und er funktioniert dann auch.
- SDD gibt mir dazu einen sehr gut strukturierten, durchdachten Workflow an die Hand — mehr als nur "Code generieren lassen".
- Specs 010–013 liefen erstmals parallel: mehrere Worktrees gleichzeitig, in denen Specs, Pläne, Tasks und Code entstanden — faszinierend zuzuschauen.
- Die Qualität der generierten Specs beeindruckt mich: So präzise und gut strukturiert würde ich selbst keiner Anleitung folgen können, um eine Spezifikation oder einen Plan zu schreiben.
- Trotzdem muss man höllisch aufpassen, gerade beim Arbeiten mit mehreren Worktrees, dass wirklich das richtige Modell ausgewählt ist — sonst ist das Rate-Limit der 5h-Session schnell aufgebraucht.
- Ärgerlich: Das Vorauswählen des Modells scheint in Skills nicht zuverlässig zu funktionieren.

---

### [2026-07-05] Insight | GitHub-Copilot-Abo schon auf 85%

Dazu kommt, dass mein GitHub-Copilot-Abo bereits nach dieser Mega-Session auf 85% ist. Und mit Fable klappt die Implementierung zwar besser (am Beispiel Trace-Spans war das spürbar), aber mein Session-Limit ist damit auch in 20 Minuten weggeatmet.

---

### [2026-07-05] Retro | Auch das zweite Feature hatte es in sich

1. Agent-Loop war im Spec falsch definiert.
2. Diverse Aussagen in der `constitution.md` führten unweigerlich dazu, dass der Code in Richtung ETL-Pipeline konvergierte.
3. Test-Coverage war kein verbindlicher Teil des Contracts (nicht Teil der Constitution).
4. Genauso Logging und Trace/Log-Spans in OTel.

---

### [2026-07-05] Incident | quickstart.md und /speckit-implement Erkenntnisse

- `quickstart.md` ist zentral relevant. Auch wenn das in den Tasks nur als Teil der Implementierung auftaucht, macht es absolut Sinn, den Inhalt selbst anzuschauen und auszuführen.
- Der AgentLoop wurde nicht vollständig getestet — dazu Probleme mit dem Modell (nur Haiku lief durch) und nicht "mitbekommen", dass ein Turn- und Token-Limit eingebaut wurde (was war API-Rückmeldung, was hatten wir selbst implementiert?).
- In der `tasks.md` steht unter Umständen sowas wie `STOP and VALIDATE` — das macht das LLM dann auch!
- Den AgentLoop danach selbst mit Claudes `/simplify`-Skill geprüft und die Magic-Strings durch ein Enum ersetzen lassen — der Code sah zu sehr nach redundantem Verhalten aus.
- Aus irgendeinem Grund setzt `/speckit-implement` die Haken nicht in die Tasks → ohne weiteren Prompt erneut ausgeführt, danach anhand der Tests faktisch geprüft, ob der "Contract" erfüllt wurde.
- Beim "init" ebenfalls nicht bedacht: Logging fehlte komplett, und zur Test-Coverage war noch nichts geschrieben — aber auch keine 100%-Coverage-Fixierung gewollt, wenn die Tests dabei nutzlos bzw. an den Anforderungen vorbei wären.
- Per Chat Befunde und Remediation-Prompts erarbeitet: [[docs/befunde-remediation-prompts.md]].
- `/speckit-converge` sollte ich mir mal genauer anschauen!

---

### [2026-07-04] Decision | Dokumenten-Governance geklärt

- Verstanden: Spec-Kit liefert bewusst kein Produkt-Gedächtnis über Features hinweg. Vision-/Problemraum-Dokumente füllen eine echte Lücke — aber ein Dokument, das kein Prozessschritt liest, existiert für SDD nicht und kann trotzdem falsche Verbindlichkeit ausstrahlen.
- Meine Regel dagegen: Einbahnstraßen-Fluss Rohmaterial → Decision Context → Constitution/ADR → Specs. Verbindlich wird eine Aussage erst durch Extraktion nach unten (so ist Prinzip V entstanden). Jede Aussage wohnt an genau einem Ort.
- Neue Dateien nur noch mit deklariertem Leser; reine Verständnis-Notizen gehören in dieses Log statt in neue Dokumente. Umgesetzt: Document Map in `CLAUDE.md`, Rollen-Frontmatter in allen `docs/`-Artefakten.
- Bei Anpassungen an Dokumenten und an `CLAUDE.md` achte ich jetzt explizit auf Anti-Sabotage-Regeln: selbst erzeugte Dokumente dürfen nie implizit verbindlich werden oder am SDD-Prozess vorbei Anforderungen einschleusen.
- Den Decision Context um die fehlenden Leitplanken ergänzt: North Star Outcomes, Agentic Boundary, Autonomy Ladder, Scale-Annahmen, erweiterte Non-Goals und §11 (Agent-Evaluation & Modell-Lifecycle) — Formulierungen wie "LLM-based processing pipeline" in §2 waren aktive Drift-Keime und sind korrigiert.
- Zwei wiederkehrende Skills angelegt, damit das Ritual nicht von meiner Disziplin abhängt: `/dev-log` (dieses Log pflegen) und `/drift-check` (Implementierung regelmäßig gegen Vision und Constitution prüfen).

---

### [2026-07-04] Incident | Drift erkannt — System wurde deterministisch statt agentisch

Was ich gemacht habe:

- Die Codebase gegen die Constitution und die ursprüngliche Idee (`docs/decision-context-overview.md`, `docs/llm-wiki-magrathea-skill.md`) analysieren lassen (mit Fable 5!).
- Nach Spec 002 fühlte sich das Ergebnis falsch an — zu viel deterministische Struktur, zu wenig agentische Funktionen.

Was die Analyse ergab:

- Der "Ingest-Agent" war kein Agent, sondern eine deterministische ETL-Pipeline: ein einziger LLM-Call (Text → JSON), danach deterministische Planner/Writer in C#.
- `CLAUDE.md`/`SKILL.md` wurden geladen, gehasht und im Task-Artifact protokolliert — aber **nie an das LLM übergeben**. Die Anforderung "Instruktionen steuern den Run" war dem Buchstaben nach erfüllt, dem Sinn nach leer (Compliance-Theater).
- Die gesamte Wiki-Intelligenz (update-vs-create, Supersession, Frontmatter, Tagging, Confidence) war als C#-Code reimplementiert — obwohl sie laut Vision im Skill/den Instruktionen leben soll.
- Guardrails bewachten den falschen Akteur: Sie wrappten die Dateizugriffe des eigenen deterministischen Codes statt der Tool-Calls eines autonomen Agenten.
- Wichtigster Einzelbefund: Die Drift begann nicht in Spec 002, sondern schon in der **Implementierung von Spec 001**. Dessen FR-012 verlangte explizit semantisches Agent-Urteil "without requiring a deterministic filename/title lookup rule" — geliefert wurde exakt ein Regex-Lookup. Unentdeckt, weil kein Test das prüfen durfte.

Root Cause:

- **SDD optimiert, was die Constitution misst.** Meine Constitution kannte nur deterministische Prinzipien (ArchTests, Testcontainers, Observability) und verbot Live-LLM-Calls in allen Tests, dazu 100%-Success-Criteria auf Ergebnisse, die eigentlich Agent-Urteile sind. Ein Coding-Agent löst das rational, indem er alles Testbare nach C# verlagert und das LLM auf einen mockbaren Call schrumpft — bei jeder Iteration ein Stück mehr.
- Ein Spec kann richtig sein und die Implementierung trotzdem driften, wenn es für die Anforderung keine zulässige Testkategorie gibt. Unverifizierbare Anforderungen werden still verletzt.
- Formulierungen in Success Criteria steuern Architektur: "100% der Runs …" auf einem Agent-Urteil erzwingt strukturell deterministischen Code.

Was ich daraus gemacht habe:

- **Constitution v1.1.0**: neues Prinzip V "Agentic Core & Deterministic Harness" — Wiki-Urteilsvermögen MUSS in Instruction-Dateien leben, die real in den Agent-Kontext geladen werden; Backend besitzt nur den Harness; Guardrails am Tool-Boundary (deny-by-default). Prinzip II erweitert: Hermetik nur für Harness-Verträge, Agent-Verhalten wird per Eval-Tests mit Schwellwerten verifiziert; 100%-Kriterien auf Agent-Urteile sind jetzt offiziell ein Spec-Defekt.
- Neuer Spec **002-agentic-ingest-core**: Der Agent-Loop ersetzt die Pipeline; Wiki-Konventionen leben ausschließlich in den Instruktionen; Guardrails bekommen mit dem echten Agenten erstmals ihren richtigen Job.

Erkenntnisse:

- Die Constitution ist der stärkste Hebel im ganzen SDD-Workflow — und damit auch die gefährlichste Stelle für blinde Flecken. Was sie nicht misst, existiert für den Coding-Agenten nicht.
- Ein Framing kann Spezifikationsarbeit ersparen: Alt-002 wollte "Wiki-Struktur" als Systemfeature (viel Code). Sobald der Kern agentisch ist, kollabiert derselbe Scope in eine SKILL.md.
- Positiv: Der Harness aus 001/002 (Hub-Dispatch, Credential-Scoping, Task-Artifacts, Restart-Reconciliation, Observability) war solide und blieb — der kontrollierte Rückbau kostete fast nur den fehlgeleiteten Kern.
- Regelmäßig gegen die Ursprungsidee (nicht nur gegen den letzten Spec) validieren. Drift fällt im Diff zweier Specs nicht auf; sie fällt auf, wenn man Code gegen die Vision hält.

---

### [2026-07-03] Setup | Plan des ersten Features

```claude
/speckit-plan the first feature should define the tech stack, take a look into @file:decision-context-overview.md for guidance which tech stack is reasonable
```

---

### [2026-07-02] Process | Original LLM-Wiki Idea gegen Problemcontext abgeglichen und erster Spec

- Als ersten Spec einen "Minimal Ingest MVP" geplant.
- Mit `/speckit-clarify` gegen die ursprüngliche LLM-Wiki-Idee gechallenged.
- Parallel die Unstimmigkeiten zwischen Problemcontext und der Original-Idee gechallenged.
- Final nochmal `/speckit-clarify` gegen den Problemcontext abgeglichen.

---

### [2026-07-01] Process | Problemdomäne erkannt, Constitution geprüft

Was ich gemacht habe:

- ADRs als Arbeitswerkzeug genutzt, um Probleme und offene Fragen in ein zentrales Dokument zu überführen — bewusst ohne Entscheidungen oder Pro/Contra-Listen, nur Problemdomäne, Kontext und Problemstatement.
- Dieses Dokument mit KI gechallenged: Was übersehe ich? Gibt es Duplikate? Passt es zur `constitution.md`?
- Die Constitution aus dem Spec-Kit-GitHub-Projekt erneut eingespeist und um Validierung gebeten.
- Gezielt nach einem sauberen Startvorgehen für ein Greenfield-Projekt mit Spec-Kit gefragt.

Ergebnis:

- Klarere Trennung zwischen Problemraum und Entscheidungsraum.
- Besseres Gefühl für Redundanzen und mögliche Lücken in den Problemstatements.
- Schärferes Verständnis, wie gut der aktuelle Problemfokus zur Constitution passt.
- Praktischere Leitplanken für den Neustart als Greenfield-Projekt mit Spec-Kit.

---

### [2026-06-30] Decision | Status heute

Ich habe die Git-Historie weitgehend zurückgesetzt und den bisherigen Code bewusst verworfen — kein Scheitern, sondern ein kontrollierter Neustart mit besserem Fokus.

Neustart-Plan:

- **Phase A — Klarheit vor Code**: alle Use Cases roh sammeln, priorisieren (MVP zuerst), nur wirklich nötige ADRs neu/sauber formulieren.
- **Phase B — Kleine, vertikale Scheiben**: pro Spec ein klarer, kleiner End-to-End-Flow; erst wenn verstanden und getestet, den nächsten starten; Connection-Code nur bauen, wenn ein konkreter Use Case ihn erzwingt.
- **Phase C — Architektur absichern**: OpenAPI/Swagger explizit entscheiden und ggf. als ADR aufnehmen; Security/Auth früh als Querschnitt spezifizieren; jede neue ADR-Entscheidung direkt mit automatisierbarer Regel/Test koppeln.

Offene Arbeitsfragen zum Neustart:

- Welche 3–5 Use Cases liefern den größten Lern- und Nutzwert für ein erstes lauffähiges System?
- Was ist der kleinste Ingest-Flow, der echten Output in Wiki-Dateien produziert?
- Welche Endpunkte sind für diesen kleinsten Flow wirklich zwingend?
- Welche ADRs sind Pflicht für den Neustart, welche können später folgen?

---

### [2026-06-30] Retro | Erkenntnisse aus dem bisherigen Verlauf

- Ich kann mit Spec-Kit schnell Struktur erzeugen, aber zu viel Struktur zu früh erzeugt Overhead.
- ADRs helfen stark, müssen aber regelmäßig konsolidiert und auf Widersprüche geprüft werden.
- Die Reihenfolge war ungünstig: erst Architekturbreite, dann Details. Sinnvoller wäre früh vertikal und klein.
- KI-Output ist hilfreich, aber ohne harte Scope-Grenzen entsteht schnell "alles gleichzeitig".

---

### [2026-06-24] Incident | Ingest-Agent als Fokus, aber erneut Scope-Drift

Was ich gemacht habe:

- Neuen Feature-Anlauf für Ingest-Agent + Web-UI gestartet.
- Vorigen Versuch verworfen, weil der Agent im Backend eingebettet statt als eigener Prozess umgesetzt war.
- Spec per Rückfragen mehrfach präzisiert:
  - Agent als eigenständiger Prozess.
  - Später containerisierbar.
  - Hub-Orchestrierung + Standalone-Fallback.
  - Human-in-the-loop nicht nur bei Fehlern, sondern auch für Rückfragen/Diskussion.
  - Embedding-Entscheidung auf LLM-basiert geschärft.

Ergebnis:

- ADR-010 als Ergänzung erstellt; ADR-002 und ADR-006 angepasst.

Offene Schmerzen:

- Zu viele Endpunkte/Verbindungen, zu wenig Klarheit über die Notwendigkeit.
- OpenAPI/Swagger als ADR fehlt.
- Unsicherheit, ob Kernanforderungen sauber beschrieben sind (Ingest-Output = konkrete Wiki-Dateien; Agent muss LLM-Antworten real aufs Dateisystem anwenden).

---

### [2026-06-23] Question | Warum .NET 9 und nicht .NET 10?

Aus derselben Session offen geblieben — nie explizit beantwortet, bevor der Neustart (2026-06-30) das Thema überholte.

---

### [2026-06-23] Retro | Von Strategie zu erster konkreter Umsetzung

Was ich gemacht habe:

- Techstack-Fragen diskutiert (C#-Wunsch vs. LLM-Unterstützung).
- ADRs auf Basis eines KI-Dialogs erstellt und nachgeschärft.
- Erstes Skeleton-Spec für Grimoire erstellt (Monorepo, .NET, Svelte, ArchTests, CI).
- Git-Branching-Extension in Spec-Kit hinzugefügt.
- LikeC4 als gewünschte Architektur-Komponente identifiziert.
- Plan/Tasks/Implementierung von der KI ausführen lassen.

Ergebnis:

- Mehrphasige Zielarchitektur (Hub, Ingest, Query, Lint, Batch, optional Telegram) als Implementierungspfad angelegt.
- Umfangreiche Spezifikationen mit vielen Storys und Akzeptanzkriterien entstanden.

Erkannte Probleme:

- Security/Auth-Fragen kamen erst spät.
- API-/Ordnerstruktur wirkte nicht wie die gewünschte Screaming Architecture.
- Constitution-Vorgaben wurden nicht immer konsequent im Ergebnis sichtbar.
- Aufgabenstatus und Agent-Kontext-Dateien waren nicht immer sauber synchron.

---

### [2026-06-22] Process | Constitution-first eingeführt

- Eine umfassende Constitution per `speckit.constitution` erstellt.
- Fokus: ADR-first, Strategic DDD, pragmatische Testing-Strategie, Observability/Behavioral Engineering.
- Ergebnis: starker methodischer Rahmen vorhanden, aber auch hoher Anspruch an Plan-/Tasks-Qualität von Anfang an gesetzt.

---

### [2026-06-21] Setup | Erster Einstieg in Spec-Kit

Was ich gemacht habe:

```bash
uv tool install specify-cli --from git+https://github.com/github/spec-kit.git@v0.11.3
uv tool update-shell
specify init --here
specify preset add --install-allowed --from https://github.com/0xrafasec/spec-kit-preset-claude-ask-questions/archive/refs/tags/v1.0.0.zip
```

Ergebnis:

- Spec-Kit lokal installiert und initialisiert.
- Preset/Workflow-Erweiterung integriert.
- Dieses Log als Lernprotokoll angelegt.
