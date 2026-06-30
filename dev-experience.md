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

## Meta-Erkenntnisse aus dem bisherigen Verlauf

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
