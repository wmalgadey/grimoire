# Contract: `wiki-identity` Hub CLI command

**Feature**: 029-shared-foundation-prompt | ADR-048, ADR-049

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
| 2 | `UsageError` | An answer the command needs was not supplied, or the invocation is malformed |
| 3 | `NotFound` | `--from-file` names a path that does not exist |
| 4 | `StateConflict` | An instance document already exists and `--replace` was not given |

## No prompting, ever

The command never asks for input. Every answer is supplied with the invocation; a missing one is a
`UsageError` naming the option to supply, and nothing is changed on that path. Behaviour is identical
with and without a terminal attached, so the command needs no way to tell the difference.

This matches every other Hub command: none of them prompt, and the only stdin use anywhere in the CLI
is piping pasted text into `submit-source`. It is also what the callers need — the deployment script,
a container exec, and later a user-facing surface all drive this without a terminal.

## Guarantees

- `--default` leaves the instance's instruction content and effective configuration identical to one
  that never ran the command.
- A document handed back is persisted **verbatim** — the bytes read from `--from-file` are the bytes
  written. Validation is limited to "readable and not effectively empty"; nothing inspects, templates
  or rewrites the content, which is what keeps the wizard a helper rather than an author.
- An existing instance document is never replaced without `--replace`; on refusal the bytes on disk are
  unchanged and the exit code is `StateConflict`.
- Emitting a brief changes nothing, so the command can be re-run from the beginning at any point.

## Deployment-script glue

`grimoire-server` forwards to this command through the `docker compose exec` invocation it already
builds, and passes the exit code through unchanged. When `--from-file <path>` names a path that exists
on the deploy host, the script stages it into the `hub` container first (`docker compose cp`) and
rewrites the invocation to the path it lands at, since the command resolves `--from-file` inside the
container's own filesystem, not the host's — a path that is not on the host is forwarded unchanged.
`grimoire-server status` prints the identity this command reports, alongside the deployed ref and the
tool version. The script implements no wizard logic and determines no identity by its own means
(FR-018a): staging a file into the container it already execs into is deployment-script plumbing, not a
judgment about wiki content or identity.
