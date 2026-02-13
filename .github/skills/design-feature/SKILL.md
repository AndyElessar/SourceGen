---
name: design-feature
description: Guide for designing new features according to request. Use this when you need to design a new feature.
---

# Design Feature

## Workflow

- [ ] Step 1: Understand the Feature Request
- [ ] Step 2: Research and Gather Information
- [ ] Step 3: Create a Design Plan
- [ ] Step 4: Review the Design with User
- [ ] Step 5: Write the Specification

## Steps

1. **Understand the Feature Request**
   - Read the request to identify requirements, objectives, and constraints.
   - **ASK FIRST**: If the request is unclear or incomplete, ask questions before proceeding.
   - **MUST NOT**: Assume missing requirements — always confirm.

2. **Research and Gather Information**
   - Study existing codebase, similar features, and relevant specs.
   - Identify technical constraints, dependencies, and potential conflicts.
   - **MUST**: Use read-only exploration (subagent or search tools) to understand context.

3. **Create a Design Plan**
   - Outline the structure, components, data flow, and integration points.
   - Include diagrams or flowcharts where they add clarity.
   - **ASK FIRST**: Present design options with trade-offs if multiple approaches exist.

4. **Review the Design with User**
   - Present the plan to the user for feedback.
   - **MUST**: Iterate based on feedback before finalizing.
   - **MUST NOT**: Proceed to specification without user approval.

5. **Write the Specification**
   - Document the final design in a spec file.
   - Include: technical details, public API surface, dependencies, edge cases.
   - **MUST**: Place spec in the project's designated spec directory.
   - **MUST NOT**: Start implementation — this skill produces a spec only.
