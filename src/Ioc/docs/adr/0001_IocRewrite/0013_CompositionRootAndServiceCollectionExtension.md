---
status: accepted
date: 2026-11-24
---

# The terms "composition root" and "service collection extension"

## Context and problem statement

The design documents used two terms that readers found hard to understand.

- `Add{Name}` named one of the three outputs. It is the signature of a method, not the name of a concept. A reader had to know that it is an `IServiceCollection` extension method before the text made sense. The specification of this output was `Register.Output.spec.md`, and that name was easy to confuse with `IocRegisterAttribute`.
- "Composer" named the output that flattens all modules into one dependency graph. The word is not common in dependency injection. The definition also said that the composer owns the instances. That is not true for the service collection extension, because MS.DI owns the instances there.

## Decision outcome

- **Service collection extension** is the name of the `IServiceCollection` extension method that the generator emits. The term follows `IocServiceCollectionExtensionsAttribute`, which turns the output on. `Add{Name}` stays only where the text shows the signature or the naming rule of the method.
- **Composition root** replaces "composer". It is the usual term in dependency injection for the place that builds the complete object graph. A composition root is a generated container or the service collection extension of an assembly.
- The constitution now states who owns the instances. A module owns none. Only the composition root builds the dependency graph. A container owns its instances. With the service collection extension, MS.DI owns them.
- The table of documented differences in the constitution labeled the service collection extension "Registration" in the column of the output that a difference applies to. The label is now "Service collection extension", because "registration" has other meanings.
- "Container" stays the term for the generated container. The text uses "composition root" only where a rule applies to both outputs, for example in the module output, which does not know which output uses it.
- The specification `Register.Output.spec.md` is renamed to `ServiceCollection.Output.spec.md`. ADRs [IocRewrite/0007](0007_ServiceCollectionExtensionGeneration.md) and [IocRewrite/0012](0012_ServiceCollectionExtensionWrapperWithoutCandidate.md) get new file names and titles with the new term. All ADRs use the new terms, so that a reader does not need a table of old names.

The diagnostic messages of `SGIOC108` and `SGIOC504` and the run-time message of `IocRegistrationMarker` still say "Add method". These messages name one concrete method, so this decision does not change them.

### Consequences

- Good: a reader who knows MS.DI understands both terms without a definition.
- Good: the definition of the composition root no longer says that it owns instances that MS.DI owns.
- Bad: links to the old file names break. Links inside the repository are updated.

## Rejected options

- **"Container" for both outputs.** The service collection extension is not a container. The module output must name both outputs in one rule, and "container" would be wrong for the service collection extension.
- **"Registration method".** The documents already use "registration" for the registration attributes and for concrete, open, and derived registrations. One more meaning would make the term ambiguous.
- **Keep the old terms in the old ADRs.** The decision of an old ADR does not change. Only its terms change. Mixed terms in the documents would make the new terms harder to learn.

## Evidence

No tests apply to this decision.
