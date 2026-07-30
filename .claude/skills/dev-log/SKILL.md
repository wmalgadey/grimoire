---
name: dev-log
description: Append this session's Spec-Kit/SDD learnings to dev-experience.md as one or more short, precise, first-person German log entries. Use when the user asks to log insights, update the dev experience log, or wrap up a significant working session.
allowed-tools: Read, Edit
---

# dev-log — Update the Dev Experience Log

`dev-experience.md` (repository root) is the personal learning log of the Spec-Kit/SDD
journey. This skill adds the current session's insights to it. The log is outside
the SDD flow: never cite it in specs, plans, or ADRs.

## User Input

```text
$ARGUMENTS
```

You **MUST** consider the user input before proceeding (if not empty).

## Entry format

Every entry is a level-3 heading followed by its message:

```markdown
### [YYYY-MM-DD] <Type> | <Short summary>

<comprehensive message: prose, bullets, code, results, or experiences>
```

- **One experience per entry.** If the session produced more than one kind of learning
  (e.g. a process change and an unrelated technical insight), write separate entries —
  do not merge them under one heading. The same date may appear on multiple entries.
- **Type** is a short free-text label naming the kind of experience — pick whatever fits;
  common ones already in use: `Setup`, `Process`, `Decision`, `Incident`, `Retro`,
  `Insight`, `Question`. Reuse an existing type over inventing a near-duplicate.
- **Short summary** is a few words, not a sentence.
- The message body is comprehensive, not a one-liner — include code blocks, concrete
  results, or the reasoning behind a decision where relevant. There is no hard length
  cap, but stay precise: bullets over prose, no filler.

## Schreibstil (verbindlich)

Der Log ist Ich-Text, kein Fließtext im Berichtsdeutsch. Er muss klingen, als hätte der
Autor ihn selbst getippt, nicht als hätte ein LLM ihn für ihn zusammengefasst.

Beobachtete Merkmale, die zu übernehmen sind — jeweils mit einem Originalbeispiel:

- **Einfache, direkte Wortwahl.** Keine bildungssprachlichen/literarischen Wörter
  (Beispiel für ein No-Go: "zutage" — stattdessen "gezeigt", "gemerkt", "aufgefallen",
  "rausgekommen"). Im Zweifel das naheliegendste, unauffälligste Wort nehmen.
- **Lockerer Satzbau**, ruhig auch mal wie gesprochen. Kein künstliches Glattbügeln zu
  sauberem Schriftdeutsch. Sätze dürfen mit "Und", "Aber", "Dabei", "Konkret", "Am Ende",
  "Dazu kommt, dass …" anfangen. Beispiel: *"Und selbst dabei musste ich die Constitution
  mehrfach nachschärfen."*
- **Ehrliche Selbstkritik ohne Beschönigung**, wenn etwas nicht geklappt hat oder er
  selbst einen Fehler gemacht hat. Beispiel: *"Am Ende war es meine eigene Nachlässigkeit
  die zu dem Drift geführt hat."*
- **Klammer-Einschübe** für Erklärungen oder einen kleinen Seitenhieb. Beispiel:
  *"Nach 4 Fable-5-Sessions (also 4x das Session-Limit in wenigen Minuten verbraten) und
  ungefähr 80% meines Copilot Pro+ Budgets war Spec 002 dann irgendwann umgesetzt 🥵."*
- **Übliche Abkürzungen**: "bzw.", "z.B.", "evtl.". Beispiel: *"SDD bzw. die spec-kit
  Skills optimieren sehr konsequent auf das, was du als Contract definierst."*
- **Konkrete Zahlen** zur Illustration, wo vorhanden (Budget-Anteile, Zeit, Prozente) —
  siehe das Copilot-Beispiel oben, oder *"vielleicht gerade mal 10% meines Systems
  umgesetzt"*.
- **Anführungszeichen** für Konzepte oder ironische Distanz. Beispiel: *"ich weiß ehrlich
  gesagt nicht, ob ich das in der Doku hätte nachlesen können"* wird an anderer Stelle zu
  einem in Anführungszeichen gesetzten *"entdeckt"*.
- **Emojis/Emoticons sparsam**, nicht in jedem Eintrag, nur wenn's passt (🥵, `;)`).
- **Rhetorische Fragen** als Gliederungs-/Stilmittel sind willkommen. Beispiel: *"Was mich
  deshalb gerade ernsthaft beschäftigt: **Wie macht man das sinnvoll im Team?**"*

Nicht jeder Text des Autors ist ein gutes Vorbild: manche stark strukturierten,
bullet-lastigen Entwürfe klingen eher analytisch-generiert als nach seiner eigenen
Stimme — im Zweifel eher Richtung "locker, ehrlich, direkt" gehen als Richtung
"dicht und aufzählungslastig".

**Wichtigste Einzelregel:** Beim Übertragen oder Umstrukturieren von Inhalten so nah wie
möglich an den Originalworten bleiben — nicht in eigenen Worten zusammenfassen oder
umformulieren. Nur wenn wirklich neuer Text nötig ist, für den es keine Vorlage gibt, das
oben beschriebene Stilprofil anwenden. Bleibt der Ton dabei unsicher, lieber nachfragen
als raten.

## Rules

- **Language**: German, first person ("ich"). The log is exempt from the project's
  English policy (clearly marked personal log).
- **Log process learnings, not work items**: insights about SDD, Spec-Kit, AI
  collaboration, and architecture governance. Litmus test: would this insight still
  matter on the next project, or does it change how I work? Routine implementation
  details do not qualify.
- **Order: newest first.** The Timeline lists entries in reverse-chronological order —
  new entries go directly under the `## Timeline` heading, above all existing entries,
  regardless of what date they carry.
- **Never rewrite or reorganize older entries.** Only new entries may be added; existing
  entries keep their position and content untouched.

## Procedure

1. Read the top of `dev-experience.md`'s Timeline (roughly the first 80 lines, right
   after `## Timeline`) to match the established format and avoid duplicating
   already-logged insights.
2. Determine today's date (`YYYY-MM-DD`).
3. Review the current session: which learnings are genuinely new relative to existing
   entries? Select at most a handful; prefer transferable insights over event recaps.
   Each qualifying learning becomes its own entry.
4. For each new entry, insert it immediately below `## Timeline` (above the entry that
   is currently first), separated from the following entry by a `---` rule. If several
   new entries are added in one pass, they end up stacked newest-first among themselves
   too (the last one you write ends up on top).
5. Do not commit unless the user asks.
6. Confirm to the user what was added, quoting each entry's heading.
