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

### [2026-08-25] Decision | Constitution 1.12: mehr agentisch, Determinismus wo er Sinn macht

Bisher stand in Principle II sinngemäß: jedes Agent-Judgment-Kriterium braucht eine
Eval-Suite (gesampelte LLM-Läufe, Threshold, gated die DoD). Klingt erstmal sauber, war
in der Praxis aber teuer und vor allem unverhältnismäßig — Tokens für die Sample-Läufe,
Review-Zeit, und dann pflegt man das Ding auch noch. Und das für Sachen wie Tagging oder
Kategorisierung, wo ein falscher Call schlicht ein Wiki-Edit ist, den der nächste Lauf
wieder geradezieht.

Was jetzt drinsteht:

- Agent-Judgment wird in **high-stakes** und **lower-stakes** einsortiert. High-stakes =
  großer oder schwer rückgängig zu machender Blast Radius (irreversible Wiki-Writes,
  Supersession/Löschen, alles guardrail-nah). Nur das braucht noch die formale Eval mit
  Threshold.
- Specs müssen jedes Agent-Kriterium explizit einsortieren. Ohne Einsortierung gilt
  automatisch high-stakes. Lower-stakes darf narrativ formuliert sein, ohne Prozentzahl.

**high-stakes** und **lower-stakes** sind eine Erfindung der KI an dieser Stelle. Aber ich lasse sie mal bestehen.

Im selben Durchgang sind noch zwei Dinge dazugekommen, die inhaltlich dazugehören:

- **Host stability guarantee** (Principle V): Egal was in einem Task oder einem
  Instruction-File steht — auch wenn's kaputt oder bösartig ist — der Harness muss
  garantieren, dass der Agent-Prozess nicht aus seinen Roots rausschreibt oder
  Subprozesse startet, die seine Guarded Tools nicht brauchen. Ein Instruction-File darf
  das nie lockern. Und: das wird hermetisch bewiesen, nie per Agent-Eval — weil es
  gerade dann halten muss, wenn der Agent sich danebenbenimmt. (Ressourcen-Limits — CPU,
  RAM, Disk — sind bewusst *nicht* drin, das gehört in den Container, nicht in den
  Harness. Der Harness schuldet da nur Observability.)
- **Der Nutzer als vierter Akteur.** Ich stehe jetzt explizit als Operator in der
  Constitution, neben Agenten, Harness und Hub. Mit der Konsequenz, dass die
  Principle-IV-Signale über mindestens eine Oberfläche erreichbar sein müssen, auf der
  ich auch wirklich hinschaue — und wo ein lower-stakes-Kriterium sich auf den
  Correction Loop stützt, muss `plan.md ## Observability` benennen, *wo* ich das
  beobachten soll. Ein Signal, das emittiert wird und nirgends ankommt, schließt die
  Schleife nicht.

---

### [2026-08-25] Insight | ADRs manuell prüfen

Mittlerweile werden viele ADRs angepasst. Da die alten ADRs nicht direkt angepasst werden, legt Claude neue ADRs an. Dabei sie die neuen ADRs aber keine vollwertigen ADRs sondern einfach nur Dummies, damit das irgendwie sinn mach (also, alte ADRs nicht zu ändern).

Das macht für moch keinen Sinn, irgendwann muss ja mal die entscheidung kommen, einen adr komplett zu ersetzen und nicht immer nur teile davon. Das kann doch auch niemand mehr überprüfen irgendwann. 

Der SDD-Workflow selbst erzeugt da einen gewissen Sog Richtung "mehr ADRs" — der
Plan-Schritt hat einen Trigger ("neue strukturelle Boundary → ADR"), und der ist leicht
zu weit auszulegen, wenn man nicht genau hinschaut, ob es wirklich eine *neue* Entscheidung
ist oder nur eine Präzisierung einer alten. 

> **Mitnahme für mich: bei jedem "ADR needed?"-Vorschlag reflexartig nachfragen, ob wirklich das Feature die ADR fordert — oder nur der Workflow, der gerade mechanisch durchläuft.**

---

### [2026-08-21] Setup | Deployment auf den aibot-Server, mit eigener CLI

In den letzten Sessions habe ich dafür gesorgt, dass ich meine Entwicklung schnell auf
meinem aibot-Server testen kann. Der Grund ist banal: Ich bin bald im Urlaub und möchte
dann gerne per Claude Code Web weiterentwickeln, nicht auf meinem Server oder Mac.

Deployment hat auch super funktioniert, und ich habe jetzt eine eigene CLI, mit der ich

- das geclonte Git-Repo aktuell halten,
- das Image bauen,
- und auch neue Branches testen kann.

Alles wie gewünscht.

Was ich daraus mitnehme: Solange Deployen und Testen nur vom Mac aus oder per SSH auf dem
Server geht, hängt der ganze Flow an einem Gerät. Mit der CLI ist der Server die
Testumgebung, und vorne reicht dann irgendein Browser. Das ist eigentlich weniger eine
Deployment-Sache als eine Voraussetzung dafür, überhaupt ortsunabhängig weiterarbeiten
zu können — und das hätte ich besser gemacht, bevor der Urlaub in Sichtweite war und
nicht erst kurz davor.

---

### [2026-08-21] Process | Triage-Map als Issue (#133) und "Waves"

Parallel dazu habe ich mich um die Issue-Triage gekümmert und Meilensteine angelegt.
Claude hat eine Triage-Map als Issue (#133) angelegt und alles, was bisher aufgefallen
ist, in die Meilensteine sortiert bzw. "Waves" definiert, die man umsetzen kann.

Der Unterschied zum Labeln aus dem Juli: Labels beantworten "was ist offen?", aber nicht
"was zuerst?". Mit den Waves hat das Board jetzt eine Reihenfolge, und ich muss die nicht
jedes Mal neu im Kopf zusammenbauen, wenn ich schaue, was als Nächstes drankommt.

Dass die Map selbst ein Issue ist und kein Dokument unter `docs/`, ist dabei der halbe
Trick. Sie liegt da, wo ich sowieso hinschaue, und wird zusammen mit den Issues gepflegt.
Ein Übersichtsdokument im Repo wäre nach zwei Wochen wieder das, was die alten
`tasks.md`-Reste waren.

---

### [2026-08-21] Insight | Nicht jeder Fix braucht SDD

Bisher waren das alles Fixes, die nicht per SDD umgesetzt wurden, und ich bin da auch ganz
froh drüber. Es waren alles "nervige" Dinge, die mit SDD wahrscheinlich Stunden gedauert
hätten. Die Codeänderungen sind nicht so aufwändig, wie sich gezeigt hat.

Und das ist für mich die eigentliche Erkenntnis: SDD lohnt sich da, wo die *Entscheidung*
teuer ist — Struktur, Boundaries, Verhalten des Agenten. Wo die Änderung selbst kleiner
ist als ihr Spec, ist der ganze Zyklus nur Zeremonie. Die Constitution schreibt den
Workflow ja für "feature work" vor, nicht für jede Zeile, die ich anfasse. Ich hatte nur
bisher im Kopf, dass alles durch den Trichter muss.

Aufpassen muss ich trotzdem: Die Grenze zwischen "nerviger Fix" und "das war eigentlich
ein Feature" verschiebt sich gerne im Nachhinein, und dann steht Code im Repo, zu dem es
keinen Spec gibt. Genau dafür sind die Labels aus der Triage da — `quick-fix` ist eine
bewusste Entscheidung, kein Vorbeischleichen.

---

### [2026-08-21] Insight | Vorne Specs, hinten der SDD-Cycle

Parallel bespreche ich meine Ideen und versuche herauszufinden, wie ich das Tool für mich
besser nutzbar mache. Ich zahle schon genug für die KI-Abos und möchte sie daher auch
optimal nutzen — eben auch fürs Wissensmanagement. Quasi parallel nur an den Specs/Issues
zu arbeiten, gefällt mir ganz gut.

So in etwa stelle ich mir das auch im Team vor: Specs ausarbeiten anstatt Code umzusetzen.
Im Hintergrund permanent der SDD-Cycle, im Vordergrund parallel Specs entwickeln.

Das ist ehrlich gesagt eine ziemliche Umstellung im Kopf. Meine Zeit geht dann nicht mehr
in "wie schreibe ich das", sondern in "was genau will ich eigentlich" — und die
Umsetzung läuft nebenher. Ob das im Team wirklich trägt, weiß ich noch nicht; für mich
alleine fühlt es sich gerade nach der besseren Nutzung des Abos an, als dem LLM beim
Tippen zuzuschauen.

---

### [2026-08-16] Process | GitHub-Backlog statt Doku-Übersicht

Mehr und mehr Issues lege ich als Backlog in GitHub an. Das macht Sinn, da die Docs im
Projekt keine gute Übersicht liefern.

Inzwischen sind es 45 Issues, und die Labels aus dem Juli tragen tatsächlich:
`spec-candidate` (neue Idee, noch kein Spec), `tail-tasks` (Rest einer schon
spezifizierten Feature), `housekeeping`, dazu `bug`. Das war im Juli noch der Versuch,
liegengebliebene Punkte überhaupt mal zu erfassen — jetzt ist es der Ort, an dem ich
schaue, was als Nächstes drankommt.

Und genau das ist der Unterschied: Die Docs (`decision-context-overview.md`, die alten
`tasks.md`-Reste) sind gut darin, eine Entscheidung oder einen Kontext festzuhalten. Aber
sie beantworten nicht die Frage "was ist offen?". Dafür müsste ich sie durchlesen — und
das tut niemand, ich selbst am wenigsten. Eine Issue-Liste mit Labels beantwortet die
Frage in 10 Sekunden.

Nebeneffekt, den ich nicht bedacht hatte: Die Complexity-Findings landen automatisch als
`housekeeping`-Issues (#76–#79) im gleichen Backlog wie die Feature-Ideen. Damit
konkurriert Aufräumen direkt sichtbar mit neuen Features, statt in einem separaten
"Technical Debt"-Dokument zu verstauben, das eh keiner aufmacht.

---

### [2026-08-16] Decision | Complexity-KPIs gegen den Wildwuchs

Ich hatte das Gefühl, die Anwendung wird immer komplexer, und das Ziel, eine einfache Code
Base zu haben, die man auch als Mensch noch versteht, geht immer weiter in den
Hintergrund. Dennoch habe ich Complexity-KPIs eingeführt, um den Wildwuchs etwas in den
Griff zu bekommen.

Konkret zwei Sachen:

- Zwei Badges im README — durchschnittliche zyklomatische Komplexität pro Funktion
  (Low/Moderate/High/Very High) und eine geschätzte "Zeit, um die Codebase einmal zu
  lesen". Beide aus einem `lizard`-CSV über `backend/src` + `frontend/src` gerechnet.
- Ein PR-Gate (`.github/workflows/complexity.yml`, `CCN_THRESHOLD: 15`), das nur bei
  *Regressionen* rot wird: Wer eine neue Funktion über CCN 15 reinbringt oder eine
  bestehende verschlimmert, fliegt raus. Der Bestand wird nur berichtet, nicht bestraft.

Diese Delta-Regel war die wichtigste Design-Entscheidung dabei. Ein hartes "alles unter 15"
hätte ich am ersten Tag nicht bestanden — `SharedFileWriteGuard.EvaluateWriteAsync` steht
bei CCN 34, `TaskArtifactStore.ParseMarkdown` bei 29, der EvalRunner-Switch bei 25,
`GuardedToolExecutor.ExecuteWriteFileAsync` bei 20. Die liegen jetzt als
Housekeeping-Issues (#76–#79) im Backlog, statt das Gate von vornherein unbrauchbar zu
machen. Ein Gate, das man beim ersten Kontakt abschalten muss, ist kein Gate.

Und es greift auch schon: Beim Ingest-Feature musste ich `ProcessAsync` auseinandernehmen,
weil das Gate sonst rot war (`f922603`). Genau das war die Absicht — nicht nachträglich
aufräumen, sondern im PR gestoppt werden.

Ehrlich bleibt trotzdem: Eine Zahl im README macht die Codebase nicht einfacher. Sie macht
nur sichtbar, in welche Richtung sie sich bewegt. Ob ich das Ziel "als Mensch noch
verstehen" damit wirklich halte, weiß ich nicht — aber vorher hatte ich nicht mal ein
Gefühl dafür, ob es besser oder schlechter wird.

---

### [2026-08-16] Retro | Die Tests haben das Falsche getestet

Die Tests waren mir zu ungenau bzw. aus meiner Sicht wurde das Falsche getestet. Das hat
sich über mehrere Constitution-Amendments hingezogen, bis ich benennen konnte, was mich
eigentlich stört.

Auslöser war `HubCliCommandTests`. Die Datei testet das, wofür die Suite da ist — Exit
Codes, Meldungen, die stdout/stderr-Trennung, gegen einen echten Coordinator und ein
echtes Repository. Aber daneben standen Assertions, die nur deshalb grün waren, weil
Spectre.Console `Settings.Validate()` vor `ExecuteAsync` aufruft. Die Tests haben
`ExecuteAsync` aber direkt aufgerufen, den Pfad also nie durchlaufen. Das "kein Store wurde
kontaktiert" war wahr durch das Test-Setup selbst, nicht durch den Produktionscode. So ein
Test kann gar nicht rot werden — der kostet nur Review-Aufmerksamkeit und Reibung beim
Refactoring.

Daraus ist die Regel "Test what we own" geworden (v1.9.0), mit einem Entscheidungskriterium
das ich mir merken kann: **Könnte diese Assertion durch eine Änderung an unserem eigenen
Code rot werden?** Wenn nur ein Dependency-Update sie kippen kann, ist es der Test der
Dependency und gehört nicht zu uns. Wichtig war mir dabei die Gegenrichtung explizit
reinzuschreiben: Straddling-Tests werden *umgeschrieben, nicht gelöscht*. Sonst wird aus
"weniger falsche Tests" ganz schnell "weniger Tests".

Zwei Wochen später kam das gleiche Muster nochmal von einer anderen Seite (v1.11.0):

- Ich hatte Structural Tests für Regeln, die gar keine Architektur-Grenze sind — "das CLI
  hat genau N Path-Switches", "kein Literal dupliziert einen Config-Default". Das sind
  keine dauerhaften Dependency-Richtungen, das ist die aktuelle Form *eines* Features. Wenn
  ich einen Switch dazubaue, ist der Test rot — und das ist dann keine erkannte
  Regression, sondern nur ein kaputter Test. Jetzt gibt es zwei Kategorien, und die ADR
  muss selbst sagen, welche sie meint: **Boundary Rule** (Phase-0-Test, IL-Level, mit
  Red/Green-Probe) oder **Feature-Scoped Invariant** (ganz normaler Verhaltens-Test in der
  Implementierungs-Phase).
- Und: Deterministische Tests haben angefangen, den *Inhalt* der `system-prompt.md` zu
  prüfen — String-Matching auf Sätze, die drinstehen müssen. Das ist genau die Grenze aus
  Prinzip V von der falschen Seite. Ein Harness-Test darf nur den Lade-*Mechanismus*
  prüfen (existiert, wird byte-genau geladen, failt closed, Hash wird protokolliert). Was
  der Inhalt beim Agenten *bewirkt*, gehört ausschließlich in die Eval-Tests. Sonst habe
  ich Agent-Verhalten zweimal abgedeckt: einmal richtig und einmal als sprödes Proxy.

Was ich daraus mitnehme: "Die Tests sind zu ungenau" war die falsche Diagnose. Es waren
nicht zu wenige Assertions, es waren Assertions auf der falschen Ebene. Und das merkt man
nicht daran, dass sie fehlschlagen — sondern daran, dass sie *nie* fehlschlagen.

---

### [2026-08-13] Incident | CodeQL hält "ccn" für eine Kreditkartennummer

CodeQL hat heute gleich dreimal in `scripts/ci/*` rumgezickt, weil die
clear-text-logging Query `ccn` (für cyclomatic complexity number) offenbar für eine
Kreditkartennummer hält und deswegen jede Stelle anmeckert, an der der
durchschnittliche CCN-Wert nach stdout geschrieben wird. Vorher schon mal mit `name`
und `location` passiert — die Query hält die für private Daten.

Der Fix ist jedes Mal banal: einfach die Identifier umbenennen (`ccn` → `complexity`,
`name` → `func`, `location` → `file_line`). Am ausgegebenen Report-Text ändert sich
nichts, weil die Heuristik nur auf Variablen-/Dict-Key-Namen schaut, nicht auf den
tatsächlich gedruckten Text.

Nervig, weil man das vorher nicht sehen kann — kommt erst als High-Severity
Security-Alert im PR-Review, nicht beim lokalen Testen. Für nächste Scripts in dem
Bereich merke ich mir: Variablennamen wie `ccn`, `ssn`, `pwd`, `secret`, `token`
grundsätzlich meiden, auch wenn sie inhaltlich nichts mit Sensitive Data zu tun haben.

---

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
