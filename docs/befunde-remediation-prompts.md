---
title: Befunde Remediation Prompts
status: reference
role: source-material
binding_for_sdd: false
reader: manual remediation and workflow prompting
usage: "Prompt library only; never cite as normative requirement in specs, plans, or ADRs."
---

# Befund: Die Grundlagen sind größtenteils da — aber mit drei echten Lücken

Ich habe Constitution, alle fünf .specify/templates/, die Feature-Artefakte von 002 und die Repo-Struktur geprüft. Test-Coverage und Logging sind auf Prinzipien- und Plan-Ebene gut verankert, aber auf Template- und CI-Ebene gibt es Widersprüche bzw. Leerstellen.

## Was vorhanden und solide ist

Constitution: Principle II (Testing-Strategie inkl. Harness-vs-Agent-Split), Principle III (Struktur-Tests mit Red/Green-Probe), Principle IV (verpflichtende ## Observability-Sektion mit Metrics, Structured Logs, Trace Spans).
plan-template.md: enthält die mandatorische Observability-Sektion (plan-template.md:87-112) — Logging ist damit strukturell abgedeckt.
spec-template.md: der Success-Criteria-Split (deterministisch 100% vs. Eval-Threshold ≥ X%) ist eingearbeitet.
Feature 002 selbst: vorbildlich — vollständige Observability-Tabellen im Plan, Test-Tasks T021–T028 (hermetisch), T030–T032 (Observability-Tests), T034–T038 (Evals, bereits erledigt).
Lücke 1 — Keine CI-Pipeline (die gravierendste)
.github/workflows/ existiert nicht; es gibt keinerlei Pipeline-Konfiguration. Die Constitution sagt aber wörtlich: "Conventions not enforced by CI/CD do not exist" (Principle IV), und die Definition of Done von 002 verlangt "CI/CD pipeline passes: architecture tests, integration tests, linting, build". Nach der eigenen Logik der Constitution existieren die Regeln derzeit formal nicht — und 002 kann sein DoD strukturell nicht erfüllen.

## Lücke 2 — tasks-template.md widerspricht der Constitution

tasks-template.md:12 sagt: "Tests are OPTIONAL - only include them if explicitly requested", und jede User-Story-Phase trägt "Tests (OPTIONAL - only if tests requested) ⚠️". Das ist der unveränderte Spec-Kit-Stock-Default und kollidiert direkt mit Principle II (Integrationstests sind der primäre Verifikationsmechanismus, nicht optional). Bei 002 wurde das manuell übersteuert ("Tests: Included and non-optional"), aber jedes zukünftige /speckit-tasks kann per Template Tests legal weglassen. Die finale Phase mit Observability-Tests und Evals ist dagegen bereits korrekt angepasst (tasks-template.md:199-200).

## Lücke 3 — Keine Test-Strategie-Sektion im plan-template

Der Technical Context hat nur eine einzeilige **Testing**: [framework]-Angabe. Es gibt keinen Ort, an dem der Plan die Zuordnung Success Criterion → Testart (hermetischer Integrationstest vs. Eval mit Threshold, welches Test-Double, welche Fixtures) festhalten muss — bei 002 wurde das ad hoc in der tasks.md-Präambel gelöst. Eine ## Test Strategy-Sektion analog zu ## Observability würde den Split aus Principle II auf Planungsebene erzwingen statt ihn dem Tasks-Schritt zu überlassen.

Bewusst keine Lücke: Es gibt kein Coverage-Prozent-Gate. Das würde ich so lassen — die Constitution definiert Coverage qualitativ ("all API boundaries"), und ein %-Threshold widerspräche dem pragmatischen Testing-Prinzip. Logging ist ebenfalls ausreichend abgedeckt (ADR-005 + Plan-Sektion + Log-Test-Tasks); dort fehlt nichts Wesentliches.

## Wie überführen — zum jetzigen Zeitpunkt

Die zwei Arbeitsstränge haben unterschiedliche Wege ins Projekt:

1. CI-Pipeline → gehört in Feature 002, jetzt. Da 002s DoD sie explizit verlangt, ist der saubere Weg /speckit-converge: das hängt die fehlende Arbeit als neue Tasks an tasks.md an — vor T039 (der DoD-Validierung). Inhaltlich minimal: GitHub-Actions-Workflow mit dotnet build + dotnet test (ArchTests, UnitTests, hermetische IntegrationTests — ohne ANTHROPIC_AUTH_TOKEN, Principle II), Evals ausdrücklich ausgeschlossen (opt-in per GRIMOIRE_EVAL=1). Ein neuer ADR ist m.E. nicht nötig — CI ist Tooling, keine Custom-Infrastruktur im Sinne von Principle IV — aber das könnt ihr im Review kurz festhalten.

2. Template-Fixes → über /speckit-constitution, unabhängig von 002. Das Entfernen des "Tests OPTIONAL"-Defaults und das Ergänzen einer ## Test Strategy-Sektion ändern keine Prinzipien, sondern gleichen Templates an bestehende Prinzipien an — das ist genau die in der Governance vorgesehene Template-Propagation (PATCH-Bump + Sync Impact Report). Diese Änderungen wirken nur auf zukünftige Features; 002s bereits generierte Artefakte übersteuern korrekt und bleiben unberührt. Es gibt also keinen Grund zu warten, aber auch keinen Zwang, 002 dafür anzuhalten.

Empfohlene Reihenfolge: erst der CI-Task via /speckit-converge (blockiert real das DoD von 002), dann der Template-Sync via /speckit-constitution als eigener kleiner Commit. Sag Bescheid, wenn ich einen der beiden Schritte ausführen soll.

## Prompts zur Behebung der Befunde (in sinnvoller Reihenfolge)

Dieses Dokument enthält direkt nutzbare Prompt-Vorlagen, um die drei identifizierten Lücken strukturiert zu schließen.

### Empfohlene Reihenfolge

1. CI-Lücke in Feature 002 schließen (blockiert die Definition of Done direkt)
2. Template-Widersprüche per Constitution-Sync beheben (wirkt auf zukünftige Features)
3. Optional: Konsistenz- und Qualitätsprüfung nachziehen

---

### Schritt 1: CI-Pipeline über Feature-Workflow ergänzen

Ziel:

- Fehlende CI-Arbeit in Feature 002 als neue Tasks ergänzen
- Reihenfolge so setzen, dass die CI-Aufgaben vor der finalen DoD-Validierung liegen
- Evals explizit nicht im Standard-CI-Lauf ausführen

Prompt:

"Führe /speckit-converge für Feature 002 aus und hänge fehlende Arbeiten als neue Tasks an specs/002-agentic-ingest-core/tasks.md an. Ergänze explizit CI-Tasks vor der finalen DoD-Validierung. Inhalte: GitHub-Actions-Workflow mit dotnet build sowie dotnet test für Architekturtests, Unit-Tests und hermetische Integrationstests (ohne ANTHROPIC_AUTH_TOKEN). Evals müssen aus dem Standard-CI ausgeschlossen sein und nur opt-in über GRIMOIRE_EVAL=1 laufen. Bitte nur fehlende Tasks ergänzen und bestehende erledigte Tasks nicht umnummerieren."

Erwartetes Ergebnis:

- Neue CI-bezogene Tasks in specs/002-agentic-ingest-core/tasks.md
- Klare Abgrenzung zwischen deterministischen CI-Tests und optionalen Evals

---

### Schritt 2: Template-Fixes per Constitution-Propagation

Ziel:

- Den Widerspruch im tasks-template beseitigen (Tests nicht optional)
- Im plan-template eine verpflichtende Test-Strategie-Sektion ergänzen
- Governance-konform per Constitution-Flow propagieren

Prompt:

"Führe /speckit-constitution aus, um die bestehenden Prinzipien korrekt in die Templates zu propagieren, ohne neue Prinzipien einzuführen. Bitte setze folgende konkrete Anpassungen um: 1) In .specify/templates/tasks-template.md alle Formulierungen entfernen, die Tests als optional deklarieren. Tests sollen standardmäßig verpflichtend sein, im Einklang mit Principle II. 2) In .specify/templates/plan-template.md eine verpflichtende Sektion Test Strategy ergänzen (analog zur Observability-Sektion), inklusive Zuordnung Success Criterion zu Testart (hermetischer Integrationstest vs. Eval mit Threshold), benötigten Test-Doubles und Fixtures. 3) Einen PATCH-Bump und einen kurzen Sync Impact Report erstellen, welche Templates geändert wurden und warum."

Erwartetes Ergebnis:

- Aktualisierte .specify/templates/tasks-template.md
- Aktualisierte .specify/templates/plan-template.md
- Nachvollziehbarer Constitution/Template-Sync mit Impact-Hinweis

---

### Schritt 3: Optionaler Qualitäts-Check nach den Änderungen

Ziel:

- Sicherstellen, dass Spec, Plan und Tasks weiterhin konsistent sind
- Keine neuen Widersprüche durch die Anpassungen einführen

Prompt:

"Führe /speckit-analyze aus und prüfe die Konsistenz über spec.md, plan.md und tasks.md für Feature 002 nach den CI-Task-Ergänzungen. Prüfe zusätzlich, ob die Template-Änderungen aus dem Constitution-Sync den Principles entsprechen. Gib nur konkrete Findings mit Schweregrad, Datei und kurzer Handlungsempfehlung aus."

Erwartetes Ergebnis:

- Klare Liste relevanter Findings oder explizite Bestätigung, dass keine kritischen Inkonsistenzen vorliegen

---

### Schritt 4: Logging für kommende Specs verbindlich machen

Ziel:

- Logging nicht nur strukturell im Plan nennen, sondern als umsetzbare Tasks und Tests erzwingen
- Für jedes neue Feature einen prüfbaren Logging-Vertrag etablieren

Prompt:

"Führe /speckit-converge für das aktuelle Feature aus und ergänze fehlende Logging-Arbeit minimal-invasiv. Stelle sicher, dass aus plan.md ## Observability ein konkreter Logging-Vertrag in tasks.md entsteht: 1) Implementierungs-Tasks für strukturierte Log-Events mit stabilen Event-Namen und Mandatory Fields, 2) deterministische Integrationstests, die Event-Name, Level und Pflichtfelder validieren, 3) CI-relevante Verankerung der Logging-Tests im Standardlauf. Wenn Logging bereits teilweise vorhanden ist, nur die Lücken ergänzen und bestehende erledigte Tasks nicht umnummerieren."

Erwartetes Ergebnis:

- Logging-spezifische Tasks in tasks.md (Implementierung + Tests + CI-Enforcement)
- Eindeutige Zuordnung Plan-Event -> Code-Emission -> Testfall

---

### Schritt 5: Logging-Vertrag in Constitution + Templates für alle künftigen Features verankern

Ziel:

- Logging-Vertrag als verbindliche Governance-Regel festschreiben (nicht nur feature-spezifisch)
- Sicherstellen, dass /speckit-plan und /speckit-tasks den Logging-Vertrag künftig automatisch erzwingen

Prompt:

"Führe /speckit-constitution aus und verankere den Logging-Vertrag für alle kommenden Features verbindlich, ohne den bestehenden Architekturrahmen zu brechen. Setze konkret um: 1) Ergänze in .specify/memory/constitution.md unter Principle IV und in der Definition of Done eine explizite MUST-Regel: Jede in plan.md ## Observability definierte Structured-Log-Event-Zeile muss in tasks.md durch drei Pflichtbausteine abgedeckt sein: a) Implementierungs-Tasks mit stabilen Event-Namen und Mandatory Fields, b) deterministische Integrationstests, die Event-Name, Level und Pflichtfelder validieren, c) CI-Verankerung dieser Logging-Tests im Standard-PR-Lauf. 2) Propagiere die Regel in die Templates: .specify/templates/plan-template.md (klare Ableitungspflicht von Structured Log Events in umsetzbare Tasks) und .specify/templates/tasks-template.md (verpflichtende Logging-Contract-Tasks für Implementierung, Tests, CI). 3) Führe nur einen minimal-invasiven SemVer-Bump durch (PATCH, falls reine Klarstellung/Propagation; MINOR nur falls neue normative Pflicht hinzukommt) und ergänze den Sync Impact Report mit geänderten Dateien und kurzer Begründung."

Erwartetes Ergebnis:

- Constitution enthält einen expliziten, prüfbaren Logging-Vertrag für alle künftigen Features
- plan-template.md und tasks-template.md erzwingen die Umsetzung des Logging-Vertrags künftig standardmäßig
- Nachvollziehbarer Version-Bump und Sync Impact Report

---

### Schritt 6: Logging in Spec 002 nachträglich via Converge ergänzen

Ziel:

- Die in 002 geplanten Structured Log Events vollständig als Code und Tests absichern
- Fehlende Events vor finaler DoD-Validierung als Tasks ergänzen

Prompt:

"Führe /speckit-converge für specs/002-agentic-ingest-core aus und ergänze ausschließlich fehlende Logging-bezogene Tasks vor der finalen DoD-Validierung. Orientiere dich an plan.md ## Observability / Structured Log Events und prüfe auf vollständige Umsetzung in Code und Tests. Ergänze bei Bedarf Tasks für: ingest.instructions.loaded, ingest.instructions.load_failed, ingest.tool.allowed, ingest.tool.denied, ingest.run.rolled_back, ingest.log.backstop_appended, ingest.agent.completed, ingest.agent.cap_exceeded inklusive Mandatory Fields. Ergänze außerdem fehlende deterministische Tests in backend/tests/Grimoire.IntegrationTests/ObservabilityLogTests.cs (oder passende Testdateien), sodass Event-Name, Level und Pflichtfelder verifiziert werden. Bitte nur neue, tatsächlich fehlende Tasks anhängen, nichts umnummerieren, keine bereits erledigten Tasks verändern."

Erwartetes Ergebnis:

- Konkrete Nachtrags-Tasks für Logging-Lücken in specs/002-agentic-ingest-core/tasks.md
- Vollständigere Abdeckung der geplanten Log-Events durch deterministische Tests

---

### Optional: Kompakter Abschluss-Prompt (alles nacheinander)

Wenn du die Schritte in einem Stück anstoßen willst:

"Bitte führe nacheinander aus: zuerst /speckit-converge für Feature 002 zur Ergänzung der fehlenden CI-Tasks vor der finalen DoD-Validierung, danach /speckit-constitution zur Korrektur von tasks-template (Tests nicht optional) und plan-template (verpflichtende Test Strategy), danach /speckit-constitution zur verbindlichen Verankerung des Logging-Vertrags in Constitution plus Template-Propagation, danach /speckit-analyze als Abschlussprüfung. Arbeite minimal-invasiv, dokumentiere nur echte Änderungen und nenne abschließend offene Risiken."

---

### Schritt 7: Trace-Spans als Grundvertrag in der Constitution verankern

Ziel:

- Distributed Trace Spans nicht nur als Observability-Detail behandeln, sondern als expliziten Grundvertrag in der Constitution festschreiben
- Die Verbindung zwischen Spans, Logs und Metriken als prüfbare Regel definieren
- Für künftige Features sicherstellen, dass Trace-Verknüpfung, Parent/Child-Beziehungen und notwendige Attribute nicht nur implizit im Code entstehen, sondern im Constitution-/Plan-/Task-Flow erzwungen werden

Prompt:

"Führe /speckit-constitution aus und verankere Distributed Trace Spans als first-class contract in der Constitution, ohne den bestehenden Architekturrahmen zu brechen. Setze konkret um: 1) Ergänze in .specify/memory/constitution.md unter Principle IV eine explizite MUST-Regel, dass jede plan.md ## Observability-Sektion neben Business Metrics und Structured Log Events auch Distributed Trace Spans enthalten muss, inklusive Span-Namen, Parent/Child-Beziehungen und Attributen. 2) Ergänze eine weitere MUST-Regel, dass Spans nicht nur dokumentiert, sondern im Code als nachvollziehbare Trace-Kette umgesetzt werden müssen, sodass Logs und Metriken innerhalb des aktiven Span-Kontexts entstehen und über gemeinsame Identifikatoren wie task_id mit den Spans korrelierbar sind. 3) Propagiere diese Regel in die Templates: .specify/templates/plan-template.md soll eine verpflichtende Trace-Strategy- bzw. Span-Mapping-Ableitung erhalten, und .specify/templates/tasks-template.md soll für jede Trace-Zeile konkrete Implementierungs-, Test- und CI-Tasks erzwingen. 4) Ergänze in der Definition of Done einen expliziten Check, dass Observability-Tests die Span-Namen, Parent/Child-Beziehungen und die für die Korrelation nötigen Attribute validieren. 5) Führe nur einen minimal-invasiven SemVer-Bump durch und ergänze einen kurzen Sync Impact Report mit den geänderten Dateien und dem Grund der Änderung."

Erwartetes Ergebnis:

- Constitution behandelt Trace-Spans als gleichwertigen, verpflichtenden Teil des Observability-Vertrags
- plan-template.md und tasks-template.md erzwingen die Trace-Verknüpfung für zukünftige Features
- Span-Verknüpfung zwischen Logs, Metriken und Traces wird als DoD-prüfbarer Standard festgelegt
