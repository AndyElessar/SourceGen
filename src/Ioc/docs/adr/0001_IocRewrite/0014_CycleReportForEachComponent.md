---
status: accepted
date: 2026-11-24
---

# One report of a circular dependency for each strongly connected component

## Context and problem statement

The [analysis model](../spec/Analysis.Model.spec.md#circular-dependencies) said that the generator reports `SGIOC301` one time for each cycle. A group of closed definitions that depend on each other can contain many elementary cycles. Their number can grow exponentially with the size of the group. All of them describe the same problem: the group cannot be created.

The specification also did not say where the definite check reports a cycle. A cycle has one dependency site for each edge.

## Decision outcome

- The generator reports `SGIOC301` one time for each strongly connected component that has a cycle. A component with one closed definition has a cycle only when the closed definition depends on itself.
- The message lists the shortest cycle through the first closed definition of the component in the ordering. The cycle starts and ends at that closed definition, so the message does not change when the declarations move.
- The definite check reports the cycle at the first edge of the message.

### Consequences

- Good: the number of reports is at most the number of closed definitions.
- Good: the message and the location are deterministic.
- Bad: when a component has more than one cycle, the message shows only one of them. The user sees the next cycle after the first one is broken.

## Rejected options

- **One report for each elementary cycle.** The number of reports can grow exponentially. The reports repeat the same problem.
- **One report that lists all closed definitions of the component.** The list is not a path, so the user cannot see which dependency to change.

## Evidence

No tests apply to this decision. The tests of the generator check the message and the location (`DependencyGraphTests`).
