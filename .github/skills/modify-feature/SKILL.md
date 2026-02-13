---
name: modify-feature
description: Guide for modifying existing features according to updated specifications. Use this when you need to change an existing feature.
---

# Modify Feature

## Workflow

- [ ] Step 1: Review the Updated Specification
- [ ] Step 2: Assess the Current Implementation
- [ ] Step 3: Update the Specification
- [ ] Step 4: Plan the Modifications
- [ ] Step 5: Update Tests
- [ ] Step 6: Implement the Changes
- [ ] Step 7: Verify Tests
- [ ] Step 8: Review and Refactor

## Steps

1. **Review the Updated Specification**
   - Read the updated spec to understand what changed and why.
   - **ASK FIRST**: If the spec is unclear or conflicts with existing behavior, ask the user.

2. **Assess the Current Implementation**
   - Explore the existing code to understand current behavior and dependencies.
   - Identify all areas affected by the change (code, tests, docs).
   - **MUST**: Check for downstream consumers that may break.

3. **Update the Specification**
   - **MUST**: Keep the spec in sync with the intended changes before implementing.
   - **MUST NOT**: Implement changes that contradict the spec.

4. **Plan the Modifications**
   - Outline specific changes using `manage_todo_list`.
   - **ASK FIRST**: If the change has significant scope or risk, confirm approach with user.

5. **Update Tests**
   - Update existing tests to reflect new behavior.
   - Add new tests for added or changed functionality.
   - **MUST NOT**: Delete tests without understanding why they existed.

6. **Implement the Changes**
   - Follow the plan; make incremental, verifiable changes.
   - **MUST NOT**: Change unrelated code without explicit approval.

7. **Verify Tests**
   - **MUST**: Run all related tests and ensure they pass.
   - **MUST NOT**: Leave failing tests unresolved.

8. **Review and Refactor**
   - Review for correctness, readability, and convention adherence.
   - Update relevant documentation.
   - **MUST NOT**: Introduce behavioral changes during refactoring.
