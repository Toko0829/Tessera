<!--
Template for a module document. Copy, rename, fill in every section.
Delete this comment. Do not delete sections — if one does not apply, say so
explicitly and give the reason. An empty section is a defect; "not applicable
because X" is a decision.
-->

# <Module Name>

## Purpose

What this module is responsible for, in two sentences.

**Not responsible for:** the adjacent concerns it must not absorb. This line
prevents the module quietly growing into everything.

## Public API

Every exported function, endpoint, component, or service.

| Name | Signature | Returns | Throws / Errors |
| --- | --- | --- | --- |
| | | | |

For HTTP endpoints also state: method, path, auth requirement, rate limit tier.

## Data Model

Tables, columns, types, constraints.

Every index gets a justification. An index without a stated query pattern is
either dead weight or a guess.

| Index | Columns | Serves which query | Why |
| --- | --- | --- | --- |
| | | | |

## Dependencies

**Depends on:** …
**Depended on by:** …

Justify any new coupling. If this module now needs something it did not before,
say what forced it.

## Security

- **Authentication:** …
- **Authorisation:** which ownership rule, enforced where
- **Input validation:** what is validated, against what schema
- **Rate limits:** which tier from CLAUDE.md §6, and why that one

If a control does not apply, state that and why. Silence reads as an oversight.

## Failure Modes

| What fails | Detection | Recovery | What the user sees |
| --- | --- | --- | --- |
| | | | |

Cover at minimum: dependency unavailable, timeout, malformed input, concurrent
modification, partial write.

## Testing

**Covered:** …
**Deliberately not covered:** … and why.

Every authorisation rule must have a test proving the unauthorised case is
rejected. List them here.

## Decisions

The section that matters most in six months.

### <Decision>

**Chose:** …
**Over:** …
**Because:** …
**Trade-off accepted:** …
