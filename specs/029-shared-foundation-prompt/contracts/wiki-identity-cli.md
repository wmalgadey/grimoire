# Contract: `wiki-identity` Hub CLI command

**Feature**: 029-shared-foundation-prompt | ADR-048, ADR-049, ADR-055, ADR-056

One command in the Hub's existing catalog, with the established exit-code convention. It runs
in-process against the shared composition root like every other Hub command.

## Catalog entry

| Name | Description |
|---|---|
| `wiki-identity` | Report or set which wiki this instance maintains. |

## Invocations

| Invocation | Effect |
|---|---|
| `wiki-identity` (no options) | Reports the identity in effect: `default` or `instance`, the resolved path, the document's hash and its first heading. Writes nothing |
| `wiki-identity set --default` | Reports that the instance stays on the shipped default. Writes nothing |
| `wiki-identity set --specialised --description <text>` | Prints a drafting brief built from `<text>`. Writes nothing |
| `wiki-identity set --from-file <path>` | Validates the drafted document and persists it as the instance document |
| `wiki-identity set --from-file <path> --replace` | Same, and permitted to replace an existing instance document |

`--description` may be supplied as `-` to read the description from stdin, so a long description does
not have to survive a shell quoting round-trip.

## Exit codes

| Code | Name | When |
|---|---|---|
| 0 | `Success` | The requested action completed (including "default kept" and "brief emitted") |
| 1 | `OperationFailed` | The document could not be written, or an unexpected error occurred |
| 2 | `UsageError` | An answer the command needs was not supplied and no terminal is attached to prompt for it; or a malformed invocation |
| 3 | `NotFound` | `--from-file` names a path that does not exist |
| 4 | `StateConflict` | An instance document already exists and `--replace` was not given |

## Terminal handling

- **Terminal attached**: a missing answer is prompted for.
- **No terminal**: a missing answer is a `UsageError` naming the option to supply. The command never
  blocks on input it cannot receive, and it changes nothing on that path.

## Guarantees

- `--default` leaves the instance's instruction content and effective configuration identical to one
  that never ran the command.
- A document handed back is persisted **verbatim** — the bytes read from `--from-file` are the bytes
  written. Validation is limited to "readable and not effectively empty".
- An existing instance document is never replaced without `--replace`; on refusal the bytes on disk are
  unchanged and the exit code is `StateConflict`.
- Emitting a brief changes nothing, so the command can be re-run from the beginning at any point.

## Deployment-script glue

`grimoire-server` forwards to this command through the `docker compose exec` invocation it already
builds, and passes the exit code through unchanged. `grimoire-server status` prints the identity this
command reports, alongside the deployed ref and the tool version. The script implements no wizard logic
and determines no identity by its own means (FR-018a).
