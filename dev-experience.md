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

## Timeline

### 2026-06-21: Setup und erster Einstieg in Spec-Kit

Was ich gemacht habe:

```bash
uv tool install specify-cli --from git+https://github.com/github/spec-kit.git@v0.11.3
uv tool update-shell
specify init --here
specify preset add --install-allowed --from https://github.com/0xrafasec/spec-kit-preset-claude-ask-questions/archive/refs/tags/v1.0.0.zip
```

Ergebnis:

- Spec-Kit lokal installiert und initialisiert
- Preset/Workflow-Erweiterung integriert
- Diese Datei als Lernprotokoll angelegt

### 2026-06-22: Constitution-first eingefuehrt

Was ich gemacht habe:

- Eine umfassende Constitution per speckit.constitution erstellt.
- Fokus: ADR-first, Strategic DDD, pragmatische Testing-Strategie, Observability/Behavioral Engineering.

Ergebnis:

- Starker methodischer Rahmen vorhanden.
- Hoher Anspruch an Plan/Tasks-Qualitaet frueh gesetzt.

### 2026-06-23: Von Strategie zu erster konkreter Umsetzung

Was ich gemacht habe:

- Techstack-Fragen diskutiert (C#-Wunsch vs. LLM-Unterstuetzung).
- ADRs auf Basis KI-Dialog erstellt und nachgeschaerft.
- Erstes Skeleton-Spec fuer Grimoire erstellt (Monorepo, .NET, Svelte, ArchTests, CI).
- Git-Branching-Extension in Spec-Kit hinzugefuegt.
- LikeC4 als gewuenschte Architektur-Komponente identifiziert.
- Plan/Tasks/Implementierung von KI ausfuehren lassen.

Was dabei herauskam:

- Mehrphasige Zielarchitektur (Hub, Ingest, Query, Lint, Batch, optional Telegram) wurde als Implementierungspfad angelegt.
- Umfangreiche Spezifikationen mit vielen Storys und Akzeptanzkriterien entstanden.

Erkannte Probleme:

- Security/Auth-Fragen kamen erst spaet.
- API-/Ordnerstruktur wirkte nicht wie gewuenschte Screaming Architecture.
- Constitution-Vorgaben wurden nicht immer konsequent im Ergebnis sichtbar.
- Aufgabenstatus und Agent-Kontext-Dateien waren nicht immer sauber synchron.
- Frage offen: Warum .NET 9 und nicht .NET 10?

### 2026-06-24: Ingest-Agent als Fokus, aber erneut Scope-Drift

Was ich gemacht habe:

- Neuen Feature-Anlauf fuer Ingest-Agent + Web-UI gestartet.
- Vorigen Versuch verworfen, weil Agent im Backend eingebettet statt als eigener Prozess umgesetzt war.
- Spec per Rueckfragen mehrfach praezisiert:

- Agent als eigenstaendiger Prozess
- spaet containerisierbar
- Hub-Orchestrierung + Standalone-Fallback
- Human-in-the-loop nicht nur bei Fehlern, sondern auch fuer Rueckfragen/Diskussion
- Embedding-Entscheidung auf LLM-basiert geschaerft

Ergebnis:

- ADR-010 als Ergaenzung erstellt; ADR-002 und ADR-006 angepasst.

Offene Schmerzen:

- Zu viele Endpunkte/Verbindungen, zu wenig Klarheit ueber Notwendigkeit.
- OpenAPI/Swagger als ADR fehlt.
- Unsicherheit, ob Kernanforderungen sauber beschrieben sind:

- Ingest-Output sind konkrete Wiki-Dateien
- Agent muss LLM-Antworten real auf Dateisystem anwenden

## Erkenntnisse aus dem bisherigen Verlauf

- Ich kann mit Spec-Kit schnell Struktur erzeugen, aber zu viel Struktur zu frueh erzeugt Overhead.
- ADRs helfen stark, muessen aber regelmaessig konsolidiert und auf Widersprueche geprueft werden.
- Die Reihenfolge war teilweise unguenstig: erst Architekturbreite, dann Details. Sinnvoller waere frueh vertikal und klein.
- KI-Output ist hilfreich, aber ohne harte Scope-Grenzen entsteht schnell "alles gleichzeitig".

## Status heute (2026-06-30)

Heute habe ich die Git-Historie weitgehend resetet und werde den aktuellen Code bewusst verwerfen.

Das ist kein Scheitern, sondern ein kontrollierter Neustart mit besserem Fokus.

## Neustart-Plan (ab jetzt)

### Phase A: Klarheit vor Code

- Alle Use Cases sammeln (roh, ohne Architektur-Diskussion).
- Use Cases priorisieren (MVP zuerst).
- Nur die wirklich noetigen ADRs neu/sauber formulieren.

### Phase B: Kleine, vertikale Scheiben

- Pro Spec nur ein klarer, kleiner End-to-End-Flow.
- Erst wenn ein Flow verstanden und getestet ist, den naechsten starten.
- Connection-Code nur dann bauen, wenn ein konkreter Use Case ihn erzwingt.

### Phase C: Architektur absichern

- OpenAPI/Swagger als explizite Entscheidung pruefen und ggf. als ADR aufnehmen.
- Security/Auth frueh als Querschnitt spezifizieren (nicht nachgelagert).
- Jede neue ADR-Entscheidung direkt mit automatisierbarer Regel/Test koppeln.

## Konkrete naechste Arbeitsfragen

- Welche 3-5 Use Cases liefern den groessten Lern- und Nutzwert fuer ein erstes lauffaehiges System?
- Was ist der kleinste Ingest-Flow, der echten Output in Wiki-Dateien produziert?
- Welche Endpunkte sind fuer diesen kleinsten Flow wirklich zwingend?
- Welche ADRs sind Pflicht fuer den Neustart, welche koennen spaeter folgen?
- ADRs nochmal sauber aufsetzen, sie sind aktuell zu stark angepasst

## 2026-07-01: Problemdomaene erkannt und Constitution geprüft

Was ich gemacht habe:

- ADRs als Arbeitswerkzeug genutzt, um Probleme und offene Fragen in ein zentrales Dokument zu ueberfuehren.
- Dabei bewusst keine Entscheidungen und keine Pro/Contra-Listen festgehalten, sondern nur Problemdomaene, Kontext und Problemstatement.
- Dieses Dokument mit KI gechallenged: Was uebersehe ich? Gibt es Duplikate? Passt es zur constitution.md?
- Danach die Constitution aus dem Spec-Kit-GitHub-Projekt erneut in den Chatbot gegeben und nach einer Validierung gefragt.
- Zusaetzlich gezielt nach einem sauberen Startvorgehen fuer ein Greenfield-Projekt mit Spec-Kit gefragt.

Ergebnis:

- Klarere Trennung zwischen Problemraum und Entscheidungsraum.
- Besseres Gefuehl fuer Redundanzen und moegliche Luecken in den Problemstatements.
- Schaerferes Verstaendnis, wie gut der aktuelle Problemfokus zur Constitution passt.
- Praktischere Leitplanken fuer den Neustart als Greenfield-Projekt mit Spec-Kit.

## 2026-07-02: Original LLM-Wiki Idea gegen Problemcontext abgeglichen und erster spec

- Als ersten Spec habe ich einen "Minimal Ingest MVP" geplant
- Mit /speckit-clarify habe ich diesen gegen die LLM-Wiki Idee "gechallanged"
- Parallel habe ich die Unstimmigkeiten zwischen dem Problemcontext und der original LLM-Wiki Idee "gechallenged"
- Final habe ich dann nochmal /speckit-clarify gegen den Problemcontext abgeglichen

## 2026-07-03: Plan des ersten Features

```claude
/speckit-plan the first feature should define the tech stack, take a look into @file:decision-context-overview.md for guidance which tech stack is reasonable
```

## 2026-07-04: Drift erkannt — das System wurde deterministisch statt agentisch

Was ich gemacht habe:

- Die Codebase gegen die Constitution und die ursprüngliche Idee (docs/decision-context-overview.md, docs/llm-wiki-magrathea-skill.md) analysieren lassen. (mit Fable 5!)
- Nach Spec 002 fühlte sich das Ergebnis falsch an — zu viel deterministische Struktur, zu wenig agentische Funktionen.

Was die Analyse ergab:

- Der "Ingest-Agent" war kein Agent, sondern eine deterministische ETL-Pipeline: ein einziger LLM-Call (Text → JSON), danach deterministische Planner/Writer in C#.
- CLAUDE.md/SKILL.md wurden geladen, gehasht und im Task-Artifact protokolliert — aber **nie an das LLM übergeben**. Die Anforderung "Instruktionen steuern den Run" war dem Buchstaben nach erfüllt, dem Sinn nach leer (Compliance-Theater).
- Die gesamte Wiki-Intelligenz (update-vs-create, Supersession, Frontmatter, Tagging, Confidence) war als C#-Code reimplementiert — obwohl sie laut Vision im Skill/den Instruktionen leben soll.
- Guardrails bewachten den falschen Akteur: Sie wrappten die Dateizugriffe des eigenen deterministischen Codes statt der Tool-Calls eines autonomen Agenten.
- Wichtigster Einzelbefund: Die Drift begann nicht in Spec 002, sondern schon in der **Implementierung von Spec 001**. Dessen FR-012 verlangte explizit semantisches Agent-Urteil "without requiring a deterministic filename/title lookup rule" — geliefert wurde exakt ein Regex-Lookup. Unentdeckt, weil kein Test das prüfen durfte.

Root Cause (die eigentliche Lektion):

- **SDD optimiert, was die Constitution misst.** Meine Constitution kannte nur deterministische Prinzipien (ArchTests, Testcontainers, Observability) und verbot Live-LLM-Calls in allen Tests. Dazu 100%-Success-Criteria auf Ergebnisse, die eigentlich Agent-Urteile sind. Ein Coding-Agent löst das rational, indem er alles Testbare nach C# verlagert und das LLM auf einen mockbaren Call schrumpft — bei jeder Iteration ein Stück mehr.
- Ein Spec kann richtig sein und die Implementierung trotzdem driften, wenn es für die Anforderung keine zulässige Testkategorie gibt. Unverifizierbare Anforderungen werden still verletzt.
- Formulierungen in Success Criteria steuern Architektur: "100% der Runs …" auf einem Agent-Urteil erzwingt strukturell deterministischen Code.

Was ich daraus gemacht habe:

- **Constitution v1.1.0**: Neues Prinzip V "Agentic Core & Deterministic Harness" — Wiki-Urteilsvermögen MUSS in Instruction-Dateien leben, die real in den Agent-Kontext geladen werden; Backend besitzt nur den Harness; Guardrails am Tool-Boundary (deny-by-default). Prinzip II erweitert: Hermetik nur für Harness-Verträge, Agent-Verhalten wird per Eval-Tests mit Schwellwerten verifiziert; 100%-Kriterien auf Agent-Urteile sind jetzt offiziell ein Spec-Defekt. Beides in die Templates propagiert (Agentic-Boundary-Gate im Plan, SC-Split im Spec, Pflicht-Eval-Task in Phase N).
- Neuer Spec **002-agentic-ingest-core**: Der Agent-Loop ersetzt die Pipeline; Wiki-Konventionen leben ausschließlich in den Instruktionen (Verhaltensänderung = Instruktions-Edit, keine Code-Änderung); Guardrails bekommen mit dem echten Agenten erstmals ihren richtigen Job.

Erkenntnisse für die Spec-Kit-Lernreise:

- Die Constitution ist der stärkste Hebel im ganzen SDD-Workflow — und damit auch die gefährlichste Stelle für blinde Flecken. Was sie nicht misst, existiert für den Coding-Agenten nicht.
- Ein Framing kann Spezifikationsarbeit ersparen: Alt-002 wollte "Wiki-Struktur" als Systemfeature (viel Code). Sobald der Kern agentisch ist, ist dieselbe Fähigkeit nur noch Instruktions-Inhalt — der komplette Feature-Scope kollabierte in eine SKILL.md.
- Positiv: Der Harness aus 001/002 (Hub-Dispatch, Credential-Scoping, Task-Artifacts, Restart-Reconciliation, Observability) war solide und bleibt — der kontrollierte Rückbau kostete fast nur den fehlgeleiteten Kern.
- Regelmäßig gegen die Ursprungsidee (nicht nur gegen den letzten Spec) validieren. Drift fällt im Diff zweier Specs nicht auf; sie fällt auf, wenn man Code gegen die Vision hält.

Nachtrag (gleiche Session): Dokumenten-Governance geklärt

- Ich habe verstanden: Spec-Kit liefert bewusst kein Produkt-Gedächtnis über Features hinweg. Vision-/Problemraum-Dokumente füllen eine echte Lücke — aber ein Dokument, das kein Prozessschritt liest, existiert für SDD nicht und kann trotzdem falsche Verbindlichkeit ausstrahlen.
- Meine Regel dagegen: Einbahnstraßen-Fluss Rohmaterial → Decision Context → Constitution/ADR → Specs. Verbindlich wird eine Aussage erst durch Extraktion nach unten (so ist heute Prinzip V entstanden). Jede Aussage wohnt an genau einem Ort.
- Neue Dateien nur noch mit deklariertem Leser; reine Verständnis-Notizen gehören in dieses Log statt in neue Dokumente. Umgesetzt: Document Map in CLAUDE.md, Rollen-Frontmatter in allen docs/-Artefakten.
- Bei Anpassungen an Dokumenten und an CLAUDE.md achte ich jetzt explizit auf Anti-Sabotage-Regeln: Selbst erzeugte Dokumente duerfen nie implizit verbindlich werden oder am SDD-Prozess vorbei Anforderungen einschleusen.
- Den Decision Context habe ich um die fehlenden Leitplanken ergänzt: North Star Outcomes, Agentic Boundary, Autonomy Ladder, Scale-Annahmen, erweiterte Non-Goals und §11 (Agent-Evaluation & Modell-Lifecycle) — Formulierungen wie "LLM-based processing pipeline" in §2 waren aktive Drift-Keime und sind korrigiert.
- Zwei wiederkehrende Skills angelegt, damit das Ritual nicht von meiner Disziplin abhängt: /dev-log (dieses Log pflegen) und /drift-check (Implementierung gegen Vision auditieren, Constitution früh nachschärfen).
- Zusätzlich habe ich mir eigene Skills gebaut:
  - /dev-log (Lernpfad festhalten) und
  - /drift-check (Umsetzung regelmaessig gegen Vision und Constitution pruefen).

## 2026-07-05: quickstart.md und /speckit-implement Erkenntnisse

- Quickstart.md ist zentral relevant. Auch wenn das in den Tasks als teil der Implementierung auftaucht, macht es absolut Sinn sich den Inhalt selbst anzuschauen und auszuführen.
- Der AgentLoop wurde nicht vollständig getestet. Mal davon abgesehen, dass ich Probleme mit dem Model hatte (nur Haiku lief durch) und ich nicht "mitbekommen" habe, dass ein Turn und Token Limit eingebaut wurde (was war jetzt API Rückmeldung und was haben wir selbst implementiert?).
- In der task.md steht unter Umständen sowas wie `STOP and VALIDATE`! Das macht das LLM dann auch!
- Den AgentLoop habe ich dann selbst nochmal mit Claudes `/simplify`-Skill geprüft und die Magic-Strings durch einen Enum ersetzen lassen. Der Code sah mir zu sehr nach Redundantem Verhalten aus.
- aus irgendeinem grund macht /speckit-implement die Haken nicht in die Tasks!? ->  /speckit-implement ohne weiteren prompt ausgeführt! danach wurde anhand der tests auch faktisch geprüft, ob der "contract" erfüllt wurde!
- Etwas, dass ich scheinbar auch beim "init" nicht bedacht habe:
  - Logging fehlt
  - Und zur Test-Coverage habe ich noch gar nichts geschrieben. Aktuell ist die Coverage nicht wirklich nützlich. Ich will aber auch nicht auf 100% wenn die Tests dazu dann nutzlos bzw. nicht den Anforderungen entsprechen.

Per Chat Befunde und Remediation erarbeitet

- [[docs/befunde-remediation-prompts.md]]

und Prompts erzeugt, mit denen die Aufgaben umgesetzt werden können.

- `/speckit-converge` sollte ich mir mal genauer anschauen!

---

Das zweite Feature hatte es dann auch in Sich!

1. Agent-Loop hatte ich falsch im Spec definiert
2. Div. Aussagen in der constitution.md führten unweigerlich dazu, dass der Code in Richtung ETL-Pipeline konvergierte
3. Test-Coverage war nicht teil der constitution.md, bzw. nicht verbindlicher Teil des Contracts.
4. Genauso Logging und Trace/Log-Spans in Otel

---

Dazu kommt, dass mein Github Copilot Abo bereits nach dieser Mega-Session auf 85% ist, und mit Fable klappt zwar die implementierung besser (am Beispiel Trace-Spans war das spürbar), aber mein Session-Limit ist damit auch in 20 Minuten weggeatmet.
