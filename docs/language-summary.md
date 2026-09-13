# Where to find things

Compass's documentation is four documents and a folder. This page says what each one is for and
which questions it answers.

**Compass is an introductory language.** It aims to make concepts legible to a beginner while
staying close enough to C# that what a student learns transfers. Most of what looks unusual here
follows from that goal; the [README](../README.md#what-it-is-for) says why.

## The four documents

| Document | What it is | Read it when |
|---|---|---|
| [language-spec.md](language-spec.md) | The normative definition. Every rule the language has, with the diagnostic that enforces it | You need to know what a construct *means*, or what the compiler will say |
| [side-by-side.md](side-by-side.md) | Every construct written both ways, Compass then C#, and three sections comparing the two | You already write C# |
| [standard-library/](standard-library/README.md) | Every type and every member the language provides, indexed by name | You want to know what you can call |
| [grammar.ebnf](grammar.ebnf) | The surface syntax as productions | You are writing a tool that reads Compass |

The [samples](../samples) are all runnable and their output is recorded, so anything shown there
is a program that works rather than a fragment.

## Which one holds what

**The words of the language.** All 63 reserved words, what `@` does to one, and the words a C#
author expects and will not find: [specification §2.1](language-spec.md#21-reserved-words). The
count beside C#'s, and what the two counts do and do not measure:
[side-by-side §9](side-by-side.md#9-where-compass-does-it-better).

**How a program is laid out.** Comments, identifiers, literals, and escapes:
[specification §1](language-spec.md#1-lexical-structure).

**What the types are.** The four number types, the two suffixes, what is a value and what is a
reference, and every conversion:
[specification §3](language-spec.md#3-types). Why a structure cannot be assigned to a `Model`,
permanently: [§3.6](language-spec.md#36-type-identity).

**Declaring things.** Variables, constants, fields, functions, definite assignment, and the four
reaches a declaration can have: [specification §4](language-spec.md#4-declarations).

**Models, structures, and enumerations**, including virtual dispatch, deep equality, and nesting:
[specification §7](language-spec.md#7-models-structures-and-enumerations).

**Optionals**, and how narrowing proves one holds something:
[specification §8](language-spec.md#8-optionals).

**What the language provides.** `Console`, `Math`, `Random`, `DateTime`, the members on a number, a
string, a set, an optional — each with its signature and an example:
[the standard library](standard-library/README.md). What every value inherits, and what `ToString`
says when nothing overrides it: [every-value.md](standard-library/every-value.md). What can be
thrown and what a `catch` takes: [exceptions.md](standard-library/exceptions.md).

**Diagnostics.** What the three severities mean and how a warning or an opinion is silenced:
[specification §0.5](language-spec.md#05-conformance-and-terminology). Every identifier the
compiler reports, with its severity and what it says:
[Appendix A](language-spec.md#appendix-a-diagnostics).

**Documenting code.** The `@summary:` labels, what the compiler checks about them, and what the
`@` does: [specification §1.3](language-spec.md#13-comments), and
[documenting.cm](../samples/documenting.cm) for a program that uses them.

**What can be thrown, and what a `catch` takes:**
[specification §10](language-spec.md#10-exceptions).

**Where a program starts, and what a project is.** Entry points, files, folders, `.cmp` projects,
namespaces, and `using`:
[specification §12](language-spec.md#12-execution-and-entry-point).

## If you are coming from C#

Read [side-by-side.md](side-by-side.md) rather than this page. It writes every construct out both
ways and ends with three sections: what Compass does better, what C# does better, and what C# has
that Compass has no form for. The third is the longest.

Four differences to know before anything else, stated in
[specification §0.4](language-spec.md#04-relationship-to-c):

- **`yield` means return.** C# uses the word for iterators; Compass's has nothing to do with them.
- **There is no `null`.** Optionals replace it, and reading one the compiler cannot prove present
  does not compile.
- **`==` is deep by default** on models, sets, and optionals, comparing structurally rather than
  by identity.
- **`this.` is mandatory**, and so is `ModelName.` for a shared member. A bare name reaches only
  locals and parameters.
