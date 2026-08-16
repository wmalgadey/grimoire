# Phase 1 Data Model: Readable API Error Presentation

**Feature**: 024-api-error-presentation | **Spec**: [spec.md](./spec.md) | **Contract**: [contracts/api-error-envelope.md](./contracts/api-error-envelope.md)

This feature introduces no persisted state. Every entity below is either a transport shape or an
in-memory value; nothing is written to the database, the artifact store, or the wiki.

---

## Backend

### `ApiErrorDefinition` — a catalogue entry

The authored description of one failure the API can return.

| Field | Type | Rules |
|---|---|---|
| `Code` | string | Stable, `snake_case`, unique across the catalogue. Never displayed to the user. |
| `Status` | int | The HTTP status this failure is answered with. 4xx or 5xx. |
| `Title` | string | Non-empty. Short headline. Contains no identifier, no status code, no serialized structure. |
| `Detail` | string | Non-empty. The actionable sentence. Same content rules as `Title`. |

**Invariants**

- Every entry has a non-empty `Title` and `Detail` (SC-006).
- `Code` is unique; two entries may not share one.
- `Status` is in `400..599`.
- Neither `Title` nor `Detail` contains the entry's own `Code` — the whole point is that the
  identifier does not reach the user (FR-002).

### `ApiErrorCatalogue` — the collection

Holds every `ApiErrorDefinition` and resolves a code to its entry.

**Behaviour**

- `Resolve(code)` returns the entry for a known code.
- `Resolve(unknown)` returns the generic fallback entry rather than failing or echoing the code
  (FR-016). The fallback is itself a catalogue entry (`internal_error` for 5xx-shaped fallbacks,
  a generic declined entry otherwise), so the "every response has readable prose" guarantee has no
  hole.

**State transitions**: none. The catalogue is immutable after construction.

### `ApiErrorResponse` — the wire shape

Serialized as `application/problem+json`. Members exactly as pinned in the contract: `status`,
`title`, `detail`, `code`, and optional `traceId`.

**Construction rules**

- `status`, `title`, `code` come from the catalogue entry and cannot be overridden at the call site.
- `detail` defaults to the catalogue entry's and may be overridden with a more specific sentence.
- `traceId` is read from the ambient trace context at construction; when absent, the member is
  omitted from the payload entirely rather than serialized as `null` or `""` (spec edge case).

---

## Frontend

### `ApiErrorCategory`

A closed set of four: `unreachable`, `declined`, `fault`, `unexpected`. Derived from what happened
to the request (research.md R4), never from body parseability.

| Category | Retryable |
|---|---|
| `unreachable` | Yes |
| `declined` | No |
| `fault` | Yes |
| `unexpected` | No |

Retryability is a property of the category, not an independent field — deriving it keeps FR-008 and
SC-004 from drifting apart.

### `PresentedError`

What a surface hands to the error component.

| Field | Type | Rules |
|---|---|---|
| `category` | `ApiErrorCategory` | Exactly one. |
| `message` | string | Non-empty. The primary readable text. From the envelope's `detail` when available, otherwise the category's generic message (FR-013). |
| `title` | string \| null | The envelope's `title` when available. |
| `status` | number \| null | Present whenever a response arrived. Technical detail only. |
| `code` | string \| null | Present when the envelope parsed. Technical detail only. |
| `traceId` | string \| null | Present when the envelope carried one. Technical detail only. |
| `bodyExcerpt` | string \| null | Bounded excerpt of what was received, populated **only** when the body was not a recognizable envelope. Technical detail only. |
| `fullMessage` | string \| null | Set only when `message` was shortened for the primary area; carries the untruncated text (FR-014). |

**Invariants**

- `message` is non-empty for every category — there is no state in which the component has nothing
  readable to show.
- `code`, `status`, `traceId`, and `bodyExcerpt` are never rendered in the primary area (FR-005).
- `bodyExcerpt` is length-capped at construction, and is populated from the response the Hub
  produced — never from an arbitrary caught object (FR-015).
- `message` is length-bounded; when bounding elides text, `fullMessage` holds the original.

### Derivation

| Input observed | `category` | `message` source |
|---|---|---|
| Request threw before a response (network, DNS, timeout) | `unreachable` | Generic connectivity message |
| Response 4xx, `application/problem+json`, parses | `declined` | Envelope `detail` |
| Response 4xx, body unrecognizable | `unexpected` | Generic unexpected-response message |
| Response 5xx, `application/problem+json`, parses | `fault` | Envelope `detail` |
| Response 5xx, body unrecognizable | `fault` | Generic fault message |
| Response ok but payload unusable | `unexpected` | Generic unexpected-response message |

### `RecordedFailurePresentation`

The narrow adapter for already-recorded agent-run failure text (`failureReason`, `outcomeReason`,
status-history `detail`). Produces a `PresentedError` in the `fault` category from a recorded
string, stripping the known technical prefix our own code composes
(`Model API error <status> (<type>): `) into the technical detail and leaving the provider's own
sentence as `message`.

**Rules**

- Only the prefix our own code writes is recognized. Text that does not carry it is presented
  unchanged as `message` — no general-purpose parsing of arbitrary recorded strings (research.md R6).
- The recorded value itself is never modified. This is a display-time transformation.
