# Architecture Decision Records

This directory contains Architecture Decision Records (ADRs) for the Keep Starting Gear project.

## What are ADRs?

ADRs document significant architectural decisions made during development, including:
- The context and constraints that led to the decision
- Options that were considered
- The rationale for the chosen approach
- Consequences and trade-offs

## How to Use

1. **Create a new ADR**: Copy `.claude/templates/ADR_TEMPLATE.md` and fill in the details
2. **Name format**: `ADR-NNN-short-description.md` (e.g., `ADR-001-use-harmony-for-patching.md`)
3. **Update this index**: Add the new ADR to the appropriate section below

---

## Accepted

| ADR | Title | Date | Categories |
|-----|-------|------|------------|
| [ADR-001](ADR-001-fika-compatibility.md) | FIKA Multiplayer Mod Compatibility | 2026-01-30 | Architecture, Compatibility |
| [ADR-002](ADR-002-spt-3114-compatibility.md) | SPT 3.11.4 Backward Compatibility | 2026-01-30 | Architecture, Compatibility |

## Proposed

| ADR | Title | Status |
|-----|-------|--------|
| (None yet) | | |

## Superseded

| ADR | Title | Superseded By |
|-----|-------|---------------|
| (None yet) | | |

## Deprecated

| ADR | Title | Reason |
|-----|-------|--------|
| (None yet) | | |

---

## Guidelines

See `.claude/standards/ARCHITECTURE_DECISIONS.md` for full ADR guidelines.

### Quick Tips

- Write ADRs when making decisions that are hard to reverse
- Be fair when documenting alternatives - don't strawman
- Include risks and mitigations
- Reference related ADRs
- Never delete ADRs - mark them as Deprecated or Superseded
