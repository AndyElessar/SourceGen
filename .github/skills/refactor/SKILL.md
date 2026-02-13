---
name: refactor
description: Guide for refactor code to improve its structure and readability without changing its external behavior. Use this when you need to improve existing code.
---

# Refactoring

## Workflow

- [ ] Step 1: Identify the Code to Refactor
- [ ] Step 2: Understand the Existing Behavior
- [ ] Step 3: Plan the Refactoring
- [ ] Step 4: Make Incremental Changes
- [ ] Step 5: Verify Tests

## Steps

1. **Identify the Code to Refactor**
   - Locate code with poor readability, duplication, or convention violations.
   - **ASK FIRST**: If the refactoring scope is large, confirm boundaries with the user.

2. **Understand the Existing Behavior**
   - Read and trace the code to understand current behavior.
   - **MUST**: Ensure existing tests cover the behavior before changing code. Write tests if coverage is insufficient.

3. **Plan the Refactoring**
   - Choose specific techniques (extract method, rename, reorganize, etc.).
   - **MUST NOT**: Plan changes that alter external behavior — refactoring is structure-only.
   - **ASK FIRST**: If design decisions arise (e.g., choosing a pattern), confirm with user.

4. **Make Incremental Changes**
   - Change one thing at a time; verify tests pass after each step.
   - **MUST**: Run tests after every incremental change.
   - **MUST NOT**: Batch large changes without intermediate verification.

5. **Verify Tests**
   - **MUST**: Run the full related test suite and confirm all tests pass.
   - **MUST NOT**: Change test assertions to match refactored code — if tests fail, the refactoring broke behavior.
