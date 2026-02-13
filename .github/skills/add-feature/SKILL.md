---
name: add-feature
description: Guide for adding new features according to specifications. Use this when you need to add a new feature.
---

# Add Feature

## Workflow

- [ ] Step 1: Clarify the Specification
- [ ] Step 2: Update the Specification
- [ ] Step 3: Plan the Implementation
- [ ] Step 4: Write Tests
- [ ] Step 5: Implement the Feature
- [ ] Step 6: Verify Tests
- [ ] Step 7: Review and Refactor

## Steps

1. **Clarify the Specification**
   - Read the spec and related code to understand requirements, constraints, and expected outcomes.
   - **ASK FIRST**: If anything is unclear or ambiguous, ask the user before proceeding.

2. **Update the Specification**
   - **MUST**: If the feature introduces new behavior not covered by the spec, update the spec first.
   - **MUST NOT**: Skip spec updates — implementation must always match a documented spec.

3. **Plan the Implementation**
   - Break the feature into smaller tasks using `manage_todo_list`.
   - Identify affected files, dependencies, and integration points.
   - **ASK FIRST**: If there are multiple viable approaches, present options to the user.

4. **Write Tests**
   - **MUST**: Write tests before or alongside implementation (test-first or test-along).
   - Cover positive cases, negative cases, and edge cases from the spec.
   - **MUST NOT**: Write tests that depend on implementation details rather than behavior.

5. **Implement the Feature**
   - Follow the plan and adhere to the spec.
   - Make incremental changes; verify each step compiles.
   - **MUST NOT**: Change unrelated code without explicit approval.

6. **Verify Tests**
   - **MUST**: Run all related tests and ensure they pass.
   - Fix failing tests before moving on.
   - **MUST NOT**: Skip test verification or mark failing tests as ignored.

7. **Review and Refactor**
   - Review for readability, performance, and adherence to project conventions.
   - Update relevant documentation (code comments, user guides, API docs).
   - **MUST NOT**: Introduce behavioral changes during refactoring.
