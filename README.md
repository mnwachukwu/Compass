# Compass

[![CI (Windows)](https://github.com/mnwachukwu/Compass/actions/workflows/ci-windows.yml/badge.svg)](https://github.com/mnwachukwu/Compass/actions/workflows/ci-windows.yml)
[![CI (Linux)](https://github.com/mnwachukwu/Compass/actions/workflows/ci-linux.yml/badge.svg)](https://github.com/mnwachukwu/Compass/actions/workflows/ci-linux.yml)

An introductory language that compiles to CIL and runs on .NET.

This project grew out of a course project for a class I took for my Software Engineering degree
where we created a lexer which then fed a semantic parser. We stopped short of creating an
interpreter or compiler however (summer semester crunch), so I took it upon myself to implement
AST generation in the parser then implement an interpreter to walk that tree.

This language is heavily influenced by and implemented with C#.

**There is a VS Code extension, and it lives in its own repository:**
**[Compass.Editors](https://github.com/mnwachukwu/Compass.Editors).** It gives a `.cm` file
diagnostics as you type, hover, go to definition, completion, renaming, formatting, an outline,
coloring by what each name means, and breakpoints and stepping. Installing it is covered there;
[what it does in full](#the-vs-code-extension) is below.

## Contents

- [What it is for](#what-it-is-for) — the six things the teaching goal decided
- [Documentation](#documentation) — the specification, the standard library, and the C# comparison
- [A taste](#a-taste) — the whole language in one screen
- [More examples](#more-examples) — what each construct looks like written out
  - [Hello, World!](#hello-world) · [Models and inheritance](#models-and-inheritance) ·
    [Optionals instead of null](#optionals-instead-of-null) · [Loops](#loops)
- [Status](#status) — how much of it works
- [Writing and running a program](#writing-and-running-a-program) — everything needed to try it
  - [1. Build the tool](#1-build-the-tool) · [2. Put `dist` on your PATH](#2-put-dist-on-your-path) ·
    [3. Write a program](#3-write-a-program) · [4. Run it](#4-run-it) · [5. Build it](#5-build-it)
  - [More than one file](#more-than-one-file) — folders, imports, and projects
  - [Seeing the machinery](#seeing-the-machinery) — the commands that print each stage
  - [One thing to remember](#one-thing-to-remember)
- [The VS Code extension](#the-vs-code-extension) — what it does, and why it is elsewhere
- [Building](#building) — building the compiler itself, and re-recording its tests
- [Samples](#samples) — every program in `samples/`, and what each is there to show
  - [Samples that fail on purpose](#samples-that-fail-on-purpose)
- [Repository layout](#repository-layout) · [License](#license)

## What it is for

Compass exists to make programming concepts legible to a beginner while staying faithful to
the patterns a C# developer uses daily, so that what a student learns **transfers** rather
than has to be unlearned. [What it keeps and what it changes](docs/side-by-side.md)
is set out side by side.

That goal is load-bearing. Where ergonomics and pedagogy conflict, pedagogy wins:
compile-time errors are preferred to runtime crashes, and explicitness is preferred to
convenience. Six consequences follow directly.

Moving mistakes from run time to build time:

- **There is no `null`.** An [optional](#optionals-instead-of-null) written with a trailing `?`
  replaces it, and reading one the compiler cannot prove is present is a compile error rather
  than a crash. A whole class of failure stops happening at run time because it stops compiling.
- **Every block says what it closes.** `end if`, `end loop`, `end model` — and the compiler
  checks the qualifier, so [closing an `if` with `end loop`](samples/negatives/compile/blocks.cm)
  is an error naming both words, rather than a missing brace reported pages away from the
  mistake.

Preferring explicitness to convenience:

- **`this.` is mandatory.** A bare name reaches only locals and parameters, so every line that
  touches object state says so. The alternative saves five characters and costs the reader the
  ability to tell a field from a local without looking elsewhere.
- **A name means one thing.** [Nothing may reuse a name a surrounding scope is already
  using](samples/negatives/compile/shadowing.cm), so reading a name is never a search for which
  one is meant.

Keeping a line readable on its own:

- **Words instead of symbols, and no abbreviations.** `and`, `or`, `not` rather than `&&`,
  `||`, `!`; `boolean`, `enumeration`, and `function` rather than `bool`, `enum`, and `func`. A
  line should be readable aloud and mean what it sounds like.
- **There is no three-clause `for`.** [`loop for i = 1 to 10` and `loop each item in items`](#loops)
  replace it. The C-style header carried the worst teaching problem in the language: an
  increment written before the body and executed after it.

Introductory does not mean limited. Compass has single inheritance with virtual dispatch,
structures with value semantics, exceptions, optionals, exact rational arithmetic, first-class
functions with closures, and compile-time definite assignment.

## Documentation

| Document | What it is |
|---|---|
| [docs/language-spec.md](docs/language-spec.md) | The normative specification, plus an appendix listing every diagnostic |
| [docs/standard-library/](docs/standard-library/README.md) | **Every type and every member**, indexed by name, each linking to the page that explains it |
| [docs/language-summary.md](docs/language-summary.md) | Where to find things — what each document holds, and which one answers what |
| [docs/side-by-side.md](docs/side-by-side.md) | The **full comparison to C#**: every construct written both ways, what C# does better, and what it can express that Compass cannot |
| [docs/grammar.ebnf](docs/grammar.ebnf) | The surface syntax as productions, and the precedence table |

The specification is written as each part of the language is implemented and covered by tests,
so it never describes more than the compiler actually does. Where it and anything else here
disagree, it is right.

## A taste

```
##
    Compass at a glance.
##

namespace Examples;

shared model Program

    constant integer PassingScore = 60;

    function Main()
        integer[] grades = {100, 95, 72, 40};

        loop each grade in grades
            Console.WriteLine(Program.Describe(grade));
        end loop
    end function

    string function Describe(integer score)
        yield if score >= Program.PassingScore then "pass" else "fail";
    end function

end model
```

Four things a C# reader should notice. **`##` opens a comment** and the next `##` closes it,
taking the rest of its own line with it; a single `#` runs to the end of a line. **`yield` means
return** — it has nothing to do with iterators. **`if ... then ... else` is an expression**,
filling the role of the ternary, which Compass does not have because `a ? b : c` has no
reading-aloud form. And conditions don't have to take any parentheses, because nothing needs them.

## More examples

### Hello, World!

A source file contains model declarations and nothing else. The entry point is a
`shared model` named `Program` holding a function called `Main`.

```
shared model Program
    function Main()
        Console.WriteLine("Hello, World!");
    end function
end model
```

`shared model` is Compass's spelling of C#'s `static class`: it cannot be instantiated or
extended, which is exactly what a running program wants — there is no such thing as an
instance of it.

A `shared model` has shared members; in C#, a `static class` must mark each of its members
`static`. So `function Main()` and `shared function Main()` mean the same thing here — write
the word if you like it, but it adds nothing.

### Models and inheritance

Members are private by default; `protected`, `internal`, and `public` widen that in turn.
**Types default to `internal`** — reachable anywhere in their project, and nowhere else. Both
defaults are the same rule: a declaration with no word belongs to the smallest thing that could
own it, and a member's owner is its type while a type's owner is its project. There's no
`private` keyword, because private is what writing nothing gets you.

Every field below is reached through `this.`, which is the rule rather than a house style: a
bare identifier reaches only locals and parameters, so there is no other way to write it.

```
abstract model Shape
    protected string Name;

    public function Shape(string name)
        this.Name = name;
    end function

    # Declared and left open: every shape has an area, and there is no answer
    # right for shapes in general. Anything built from Shape must write it.
    public abstract real function Area();
end model

model Circle extends Shape
    real radius;

    public function Circle(real r)
        base("circle");
        this.radius = r;
    end function

    public override real function Area()
        yield 3.14159 * this.radius * this.radius;
    end function
end model
```

### Optionals instead of null

An optional is written with a trailing `?`. You cannot read one without proving it is there.

```
model Greeter
    shared function Greet(string? nickname)
        # Or supplies a fallback, and does not evaluate it unless it is needed
        Console.WriteLine(nickname.Or("Hello, stranger"));

        # HasValue narrows the optional inside the guarded block
        if nickname.HasValue()
            Console.WriteLine(nickname.Value());
        end if
    end function
end model
```

### Loops

There is no three-clause `for`. It carried the worst teaching problem in the language: an
increment clause written before the body but executed after it.

```
model Counting
    shared integer function SumTo(integer limit)
        integer total = 0;

        loop for i = 1 to limit
            total = total + i;
        end loop

        yield total;
    end function

    shared integer function CountDown()
        integer total = 0;

        loop for i = 10 until 0 stepby -1
            total = total + i;
        end loop

        yield total;
    end function

    shared integer function CountLetters(string word)
        integer seen = 0;

        loop each letter in word
            seen = seen + 1;
        end loop

        yield seen;
    end function
end model
```

`to` is inclusive and `until` is exclusive. The loop variable is fresh on every iteration and
read-only inside the body, which removes the classic closure-capture trap.

## Status

**Compass runs, it compiles, and it is a language you can work in.** Programs execute on a
tree-walking interpreter, `cm build` writes a real .NET assembly for every program that checks,
breakpoints and stepping work in VS Code, and a language server answers an editor about the file
being typed into rather than the one last saved.

| Stage | State |
|---|---|
| Lexer | Complete |
| Parser | Complete |
| Resolver | Complete |
| Type checker | Complete |
| Definite assignment, optional narrowing | Complete |
| Lowering | Complete |
| Closure conversion | Complete |
| Interpreter | Complete |
| Multi-file compilation, projects, imports | Complete |
| Namespaces, `using`, qualified names | Complete |
| Standard library | Complete |
| Debugger | Complete |
| CIL emitter | Complete |
| Language server | Complete |

Source becomes a syntax tree, every name resolves, every expression has a type, nothing can be
read before it holds a value, and an optional cannot be read at all until presence is proven.
All of it reports errors with positions and recovers rather than stopping at the first
mistake.

**Namespaces scope.** Two of them may each declare a `Circle`; a bare name reaches whichever is
nearest, and a qualified one — `Shapes.Circle`, `Standard.Math` — reaches past that with no
`using` required. The library lives in `Standard`, which is in scope in every file without
being asked for.

**The back end is finished.** Every program that checks is a program that builds — the emitter
declines nothing, and all thirty-five runnable samples compile to CIL and print exactly what the
interpreter prints. Expressions, control flow, models with inheritance and virtual dispatch,
structures, enumerations, sets, optionals, exceptions, fractions, switches, functions as values,
and the standard library's own types all emit.

The interpreter is not a stopgap, and does not go away. It runs the same lowered tree the emitter
does, so it stays on as the oracle: where the two disagree about what a program means, one of them
has a bug, and the corpus is run through both on every build to find out.

**Every emitted assembly is also handed to the runtime's own IL verifier.** Running a program
only exercises the instructions it happens to reach, and only proves they did not crash on that
machine — an unbalanced stack on a branch nothing takes prints the right answer until the jit
changes its mind. The verifier reads every method whether or not it runs, and where the runtime
would say only `InvalidProgramException`, it names the method and the instruction.

[Writing and running a program](#writing-and-running-a-program) is everything needed to try it,
and every file in [Samples](#samples) runs today.

## Writing and running a program

You need the .NET 10 SDK and a clone of this repository. What follows builds the compiler from
source, which is what to do with the repository in front of you — to work on it, or to run a
change you have just made.

**To use Compass rather than work on it, install it instead.** One command per platform at
[compass.pluperfect.dev/install](https://compass.pluperfect.dev/install), or an archive from
[the latest release](https://github.com/mnwachukwu/Compass/releases/latest). Either brings
everything with it: the builds are self-contained, so nothing has to be installed first and there
is no runtime on the machine to be the wrong version.

### 1. Build the tool

```bash
dotnet publish src/Compass.Cli.Alias -p:PublishProfile=dist
```

That writes two identical executables into `dist/`: `compass`, and the shorter `cm`. Either
name works everywhere below. (In Visual Studio, right-click **Compass.Cli.Alias → Publish** and
pick the `dist` profile — it does the same thing.)

### 2. Put `dist` on your PATH

For the current terminal only, from the repository root:

```powershell
$env:PATH = "$PWD\dist;$env:PATH"
```

```bash
export PATH="$PWD/dist:$PATH"
```

To make it permanent, add the same line to your PowerShell `$PROFILE` or your `~/.bashrc`.
Adding the folder to your account's PATH through the system dialog also works — but note that
an already-running terminal, editor, or file manager keeps the environment it started with, so
open a new window afterwards.

Prefer to skip PATH entirely? Every command below also works as
`dotnet run --project src/Compass.Cli -- run <file>`, straight from the repository root.

### 3. Write a program

One command starts you an empty one:

```bash
cm new hello
```

```
Wrote hello.cm
Run it with: cm run hello.cm
```

This is what it wrote. It is the smallest legal Compass program — it compiles, runs, and does
nothing:

```
shared model Program
    function Main()
    end function
end model
```

Every rule it follows is the one described under [Hello, World!](#hello-world) above: model
declarations and nothing else, no top-level code, and `Main` inside a `shared model` named
`Program`.

**`cm sample hello` writes one that does something instead**, for a first look at what the
language reads like. The two are deliberately different commands: a new program should be empty,
because every line already in it is a line you have to read and then delete.

```bash
cm sample hello
```

Either form takes `--project`, which writes a folder holding a `.cmp` and the program it builds,
and neither writes over anything already there. This is what `sample` leaves, under the same
rules as above:

```
shared model Program
    function Main()
        Console.WriteLine("Hello, World!");

        loop for i = 1 to 5
            Console.WriteLine(i + " squared is " + (i * i));
        end loop
    end function
end model
```

### 4. Run it

```bash
cm run hello.cm
```

`run` checks the program and then executes it on the interpreter. Nothing runs until
everything checks, so a program that reaches execution has already been proved free of every
mistake the front end can see. No file is produced; [`build`](#5-build-it) is the command that
writes one.

The extension can be left off — `cm run hello` finds `hello.cm`, and finds `hello.cmp` if that
is what is there instead. Write it out when both exist and you mean one of them; anything that
is neither is refused rather than read hopefully.

To check without running:

```bash
cm check hello.cm
```

Errors come back all at once rather than one per run, with positions, in the format editors
already parse. A file with several mistakes in it reports like this:

```
scratch.cm(4,27): error CM0330: 'Total' is a function, so it has to be called: write 'Total()'.
scratch.cm(7,27): error CM0400: 'total' is used here before it has been given a value.
scratch.cm(10,27): error CM0303: '+' is not defined for an integer? and an integer.
```

Three mistakes, three messages, one run — and each caught by a different part of the compiler.

**Every command says how it went in its exit code**, which is the only part a build step reads.
There are four, because the three ways this can go wrong are three different people's problem:

| Code | What happened | Whose problem |
|---|---|---|
| 0 | It worked | — |
| 1 | Something is wrong with the program, reported with a position | Whoever wrote the code |
| 2 | Something is wrong with the command line, so no program was read | Whoever wrote the command |
| 3 | The compiler asserted something it says cannot happen | This repository |

`cm run` is the exception, and deliberately: it hands back whatever the program itself
returned, so a Compass program can say how it went too.

### 5. Build it

`run` interprets. `build` compiles to a real .NET assembly and leaves it on disk:

```bash
cm build hello.cm
```

```
hello.cm: wrote bin\hello.dll
Run it with: bin\hello.exe
```

Four files land in `bin`: the assembly, its runtime configuration, the Compass runtime it leans
on, and a **launcher** you can start without naming `dotnet`. A folder of its own rather than
beside the source, because four files a tool made should not be mixed in with the one you wrote —
and because every `.gitignore` already knows the name. `--out` puts them somewhere else.

**A loose `.cm` file always builds into the `bin` beside it.** There is nowhere in a source file
to say otherwise, which means a folder holding a dozen programs fills one `bin` with all twelve.
Where a build goes is a thing a build says about itself, and the file that says things about a
build is a project file:

```text
project Storefront
    source Program.cm
    output ../artifacts/storefront
end project
```

The path is read relative to the project file, so where the build lands does not depend on which
directory you started it from, and the folder is made if it is not there. `--out` still beats
it — you typed that just now, for this one build.

**Something that declares no `Program` builds a library**, rather than being refused for having
nowhere to begin. That is what `books.cmp` in [samples/library](samples/library) is: a project
written to be referenced by another. No launcher is made for one and no configuration is written
beside it, because nothing starts a library — `cm build` says which of the two it made, and
`cm run` on the same file still asks for a `Program`.

The launcher is the stock .NET apphost, the same one `dotnet publish` produces, with the name of
the assembly written into the region reserved in it for exactly that. **The machine still needs
.NET installed** — this is a launcher, not a self-contained copy of the runtime.

**You can build for a machine that is not this one**, the way `dotnet publish -r` does:

```bash
cm build hello.cm --runtime linux-x64
```

The default is whatever this machine is, so the common case needs no flag. What may be named is
whatever launcher the SDK has on hand, which `cm platforms` prints — and a platform that is not
there is refused with the command that fetches it, rather than producing something that will not
start:

```
cm: nothing here can build for 'freebsd-x64'. Available: linux-x64, osx-x64,
win-arm, win-arm64, win-x64, win-x86. 'dotnet publish -r freebsd-x64' on any
project fetches what is needed.
```

Building a program for another platform on Windows also prints the `chmod +x` you will need
there, since a Windows file system has nowhere to record that a file may be run.

### More than one file

A program grows out of one file eventually. Put the next model in its own file beside the
first, and it is already visible:

```
bookshelf/
  Program.cm     declares Program, so it is the program
  Book.cm        declares no Program, so it is shared code
  Shelf.cm       likewise
```

```bash
cm run bookshelf/Program.cm
```

The rule is one sentence: **a file that declares `Program` is a program, and every other `.cm`
in the folder is shared code that all of them can see.** Nothing has to be imported, listed, or
declared in an order.

That also means a folder can hold several programs at once, which is what a folder of
exercises or of half-finished ideas actually looks like. Add `Audit.cm` with its own `Main` and
it becomes a second program: it sees `Book.cm` and `Shelf.cm` too, ignores `Program.cm`
entirely, and a mistake in either one is not visited on the other.

The folder rule does not descend into subfolders. When one file needs another that isn't
beside it, name it:

```
import "shared/Tally.cm";
```

An import brings **one** file, plus whatever that file imports in turn — an imported file has
to be able to compile, and it can't if what *it* names is missing. Paths are relative to the
file that wrote them, with forward slashes on every platform; an absolute path works but warns,
since it resolves only on the machine that wrote it. Reaching the same file twice is silent —
it's one file, not two.

**Imports that circle are warned about.** If A imports B and B imports A it still builds — a
compilation reads every file it gathers together — but neither file is the one you read first,
so the compiler says so at the import that closes the circle. The fix is usually to write less:
files beside each other are already compiled together and need no import at all, so a circle
only happens across folders.

`import` and `using` never do each other's job: **an import decides which files are compiled,
a using decides which names are reachable.** Pick by scale — one file, an import; a group of
related types, a namespace; a whole build across folders, a project file:

```
# A storefront, spread across folders.

project Storefront
    source Program.cm
    source models
    source pricing
    output ../bin/storefront
end project
```

```bash
cm run storefront/storefront.cmp
```

A `source` naming a folder takes every `.cm` directly inside it, and does not descend, so what
a project builds can always be read off the file. An `output` says where the build goes, and is
the only way to say so — a loose `.cm` has nowhere to record one, which is why the forty
programs in `samples/` all land in the same `bin` and this one does not. Paths are relative to
the project file. The whole vocabulary is `project`, `source`, `reference`, `entry`, `output`,
`ignore` and `end project`; the format is deliberately small and is not Compass, since it
describes a build rather than a computation and nothing in it compiles.

**A project builds on another with `reference`**, which brings that project's types:

```
project Storefront
    reference ../Core/Core.cmp
    source Program.cm
    source models
end project
```

**A project with more than one program says which one starts, with `entry`:**

```
project Tools
    entry Tools.Program
    source Tools.cm
    source App.cm
end project
```

Only needed where there is a choice. Namespaces make `Tools.Program` and `App.Program` two
different types, so a compilation may hold both — and then the compiler must be told rather
than choose, because an assembly holds one entry point in its metadata and picking by the order
the sources were listed would make a build's behavior depend on the order of its own file
list. Written where only one program exists, the line decides nothing and says so. `cm run` on
a single file needs none of it: it runs the `Program` that file declares.

References are followed as far as they chain, a project reached twice is brought once, and what
a project references is built before the project itself. Unlike imports, projects **may not**
reference in a circle: a build that has to exist before itself can't be produced, so that one is
an error. Code two projects both need goes in a third they both reference.

### Seeing the machinery

The remaining commands print each stage of compilation, which is most of why the tool exists:

```bash
cm tokens hello.cm
```

```bash
cm ast hello.cm
```

```bash
cm lower samples/sorting.cm
```

`lower` is the interesting one — it shows the simplified tree the interpreter actually walks,
with `loop each` already rewritten into an index loop and every implicit conversion made
explicit.

Five more exist for editors rather than for reading:

- **`cm format`** lines a file up and prints it, or writes it back with `--write`. `--check`
  prints nothing and fails if the file is not already formatted, which is what a build step
  wants. It never moves your code: indentation and spacing only, so a comment cannot be lost and
  a file that does not parse is formatted anyway.
- **`cm outline`** prints what a file declares as JSON, which is where VS Code's breadcrumbs and
  Outline view come from. It answers for a file that does not compile — the state a file is in
  most of the time it is being written.
- **`cm project`** prints the `.cmp` that builds a given file, or says none does. That is how the
  editor knows what "run the project this file belongs to" means.
- **`cm debug`** speaks the Debug Adapter Protocol over its own standard input and output, so
  every decision about where to stop and what to show lives here rather than in an editor plugin.
- **`cm lsp`** speaks the Language Server Protocol, and is the only one of the five that stays
  open. The others read a file, answer, and exit, so none of them can see an editor buffer that
  has not been saved.
  This holds what the editor holds, so diagnostics arrive as the code is written rather than when
  a button is pressed, and hover, go-to-definition, renaming, coloring and the outline answer
  about what is on screen rather than what was last saved.

None of the five is a convenience. Each answers a question about Compass that an editor would
otherwise have to answer for itself — by parsing the language, or reading a project file, or
laying it out, or deciding what one step means — and a second answer to any of those agrees with
this one only until the day it does not.

### One thing to remember

`dist` holds a **copy** of the tool from when you published it. If you change the compiler's
own source, re-run step 1 or `cm` will keep running the old build. While working on the
compiler itself, `dotnet run --project src/Compass.Cli -- run <file>` cannot go stale.

**With VS Code open, republishing fails**: the language server is a running copy of `cm`, and
Windows will not let a running program be overwritten. Run `Compass: Stop the language server`
from the command palette first, then publish, then `Compass: Restart the language server`.

## The VS Code extension

**Editor support lives in its own repository**, [Compass.Editors](https://github.com/mnwachukwu/Compass.Editors).
The VS Code extension there gives a `.cm` file syntax highlighting, **breakpoints and stepping**,
breadcrumbs and an Outline, **diagnostics as you type**, hover types, go to definition,
**completion that knows what the place it is in will take** — after a dot, for a bare name, and
ordered by what would fit where the caret is — signature help, quick fixes, **renaming a name
everywhere it is written**, **coloring every name for what the compiler worked out it is**, marking
every use of the name under the caret, **finding every use of it across the whole program**,
**formatting**, and buttons to run or build what you are looking at.
It also manages `.cmp` projects: starting one, listing a file in it or taking it out, and saying
which program it starts at. Installing it, and the rest of what it does, is covered there.

**Almost none of the debugger is in the extension.** `cm debug` is all of it; the extension only
says which command to start. Two implementations of the rules about where to stop would give
two answers to every question about them.

It is a separate repository because it answers to a different clock. The extension is
declarative and ships whenever it is ready; the compiler is on a phase plan. Keeping them apart
means neither waits for the other.

**One thing here serves it.** `cm vocabulary` prints every reserved word and every built-in type
name as JSON, and the result is committed as [docs/vocabulary.json](docs/vocabulary.json). The
grammars over there are tested against that file, so a keyword added here cannot stop being
colored without a test failing. Regenerate it whenever the language gains or loses a word:

```bash
cm vocabulary > docs/vocabulary.json
```

A test fails if you forget.

## Building

Requires the .NET 10 SDK.

There is no `global.json`, deliberately. The build uses whichever SDK is newest on your machine,
so a break caused by a new SDK surfaces immediately rather than being hidden behind a pinned
version.

```bash
dotnet build
```

```bash
dotnet test
```

Much of the suite compares against recorded files — token streams, syntax trees, what a
program printed, and how a failing program failed. After a change that is meant to alter one
of those, re-record them and read the diff:

```bash
COMPASS_UPDATE_GOLDEN=1 dotnet test
```

To build and run Compass programs rather than the compiler itself, see
[Writing and running a program](#writing-and-running-a-program) above.

## Samples

Every one of these runs. Each is a complete program, and each is there to show one thing.

| Sample | What it is for |
|---|---|
| [hello.cm](samples/hello.cm) | The smallest Compass program that does something |
| [fizzbuzz.cm](samples/fizzbuzz.cm) | Range loops and an if/else-if chain |
| [fibonacci.cm](samples/fibonacci.cm) | The same sequence written recursively and iteratively, side by side |
| [primes.cm](samples/primes.cm) | The Sieve of Eratosthenes; sets used as a workspace |
| [sorting.cm](samples/sorting.cm) | Insertion sort, and why `and` short-circuiting matters |
| [scanning.cm](samples/scanning.cm) | `break` and `continue`, and which loop a `break` leaves |
| [looping.cm](samples/looping.cm) | **How far a loop counts, and when it decides.** `to` against `until`, a bound and a step that move while the loop runs, and why `loop each` is the one that does not |
| [card-table.cm](samples/card-table.cm) | **`switch`.** Grouped labels, `default`, and the warning for a member left unhandled |
| [narrowing.cm](samples/narrowing.cm) | **What the compiler knows about an optional, and where it stops knowing it.** A check, a guard that leaves rather than wraps, every arm storing one — and the three joins a proof does not survive |
| [equality.cm](samples/equality.cm) | **When two values are equal.** Deep and structural without either value saying how — through a set, through a model holding models, and around a ring that points back at itself — against `Reference.Equals`, which asks whether there is one of them |
| [numbers.cm](samples/numbers.cm) | **The four kinds of number, and which conversions the language makes for you.** One rule decides all of it — what loses nothing happens on its own — and `float` is where you meet an infinity, and a value not equal to itself |
| [structures.cm](samples/structures.cm) | **Values against references.** What copying changes, and what a structure holding a model shares |
| [binary-search.cm](samples/binary-search.cm) | **Optionals.** Yields `integer?` rather than a `-1` sentinel |
| [fractions.cm](samples/fractions.cm) | **Exact rationals.** `1\|3 + 1\|3 + 1\|3` is exactly 1; the same sum in `real` is not |
| [runtime-fractions.cm](samples/runtime-fractions.cm) | Building fractions from values with `Fraction.Create`, when literals will not do |
| [standard-library.cm](samples/standard-library.cm) | Everything the language provides without declaring anything |
| [bits.cm](samples/bits.cm) | **Working on the bits of a whole number.** Flags combined and asked about, `bitwise and`/`or`/`xor`, and the two shifts |
| [ignoring.cm](samples/ignoring.cm) | **Telling the compiler to stop saying something.** The three severities, the three forms of `# ignore`, how far each reaches, and why a comment beginning with the word stays a comment |
| [documenting.cm](samples/documenting.cm) | **Writing down what a thing is.** `@summary:` and the labels beside it, how a summary runs to several paragraphs, and why a remark above a declaration stays a remark |
| [visibility.cm](samples/visibility.cm) | **`shared` and `public` answer different questions.** One asks how many there are, the other who can reach it — a shared model's members are private until they say otherwise |
| [mathematics.cm](samples/mathematics.cm) | **Every member of `Math`.** Constants, roots, logarithms, angles, rounding, and why `Log` is the natural one |
| [long-division.cm](samples/long-division.cm) | **A worked solution, laid out in columns.** Asks for two numbers and divides one by the other the way it is taught, with `r` for the remainder — the arithmetic is the easy half |
| [conversions.cm](samples/conversions.cm) | **Getting between types.** What converts on its own, what you must ask for, and `is` / `as` |
| [lambdas.cm](samples/lambdas.cm) | **Functions as values.** Both ways to write one, leaving the parameter types out, passing and returning them, what they remember, holding any of them as a `Function`, and keeping one in a field |
| [defaults.cm](samples/defaults.cm) | **What a field holds before it is written to.** Every primitive at its own zero, an optional starting empty, and the constructor settling the one field that has no zero to start at |
| [overloads.cm](samples/overloads.cm) | **One name, several versions.** Count before kind, exact before widening, the nearest model, an optional as its own type, versions across a parent and its child, and why an override is not a second version |
| [nesting.cm](samples/nesting.cm) | **A type declared inside another.** Named from outside by writing the container first, reached unqualified from within it, private unless it says otherwise — and two containers each holding a `Node` without either giving way |
| [throwaway.cm](samples/throwaway.cm) | **A name for the value you do not want.** A bare `_` in the three places a name is obliged — a loop that only counts, a walk that ignores its element, a `catch` that goes by type alone — with a result dropped by saying nothing about it, and two throwaways nested without clashing |
| [closures.cm](samples/closures.cm) | **What a function remembers.** The variable rather than a copy of it, a fresh loop counter every turn, naming two runs at once, outliving the call that made it, keeping an instance, and reaching a parent |
| [matrices.cm](samples/matrices.cm) | **Grids and cubes, and the arithmetic they hold.** `integer[][]` is a set of sets and `integer[][][]` a set of grids, with no feature added for either — then transposing, multiplying, and using a grid to turn a point in the plane and in space |
| [shapes.cm](samples/shapes.cm) | Inheritance, `virtual`/`override`, and dispatch on the runtime type |
| [bank.cm](samples/bank.cm) | Exceptions, including one the program declares — and when to yield an optional instead |
| [exceptions.cm](samples/exceptions.cm) | **Everything about going wrong.** A hierarchy the program declares, catching by ancestor, which clause wins, `finally`, throwing again, the ones the language raises — and when an optional is the right answer instead |
| [files.cm](samples/files.cm) | **Keeping things in files.** Whole files out and back, a line at a time, what is not there against what went wrong — and it removes the folder it made |
| [asking.cm](samples/asking.cm) | **Reading typed input.** `Console.Read` and the two questions it forces — was anything typed, and did it parse as the type wanted |
| [sets.cm](samples/sets.cm) | **Rows of things.** Building, asking, taking a run out, `Union`/`Intersect`/`Except` — and the same words on a string, where the difference is that a set changes and a string does not |
| [text.cm](samples/text.cm) | **Building text.** Values written into a sentence with `{{ }}`, a pattern after the colon saying how, block strings that read nothing they hold, and taking a string apart with `Split` and `Join` |
| [dates-and-times.cm](samples/dates-and-times.cm) | **Four types, four questions.** Which day, what time of day, how long, which moment — why 23:30 plus an hour is 00:30 on a clock but the next day as a moment, and writing one out by a pattern and reading it back |
| [scopes.cm](samples/scopes.cm) | **How far a name reaches.** A `begin` block that exists only to bound one, a function declared inside another and what it can see, and `internal` on a type |
| [namespaces.cm](samples/namespaces.cm) | **Where a name sits.** Two namespaces each holding a `Circle`, both forms of declaring one, what a `using` decides, and what qualifying reaches past it |

`samples/reference/` holds four files that are not programs and declare no entry point:
[tour.cm](samples/reference/tour.cm), which contains every construct in the grammar at least
once, and [literals.cm](samples/reference/literals.cm),
[operators.cm](samples/reference/operators.cm), and
[comments.cm](samples/reference/comments.cm), which exercise the scanner.

They sit in their own folder because of the rule that naming a source file also compiles the
files beside it that declare no `Program` — which is what makes a folder of shared code work
without a project file. All four declare none, so putting them among the programs would attach
four hundred lines of reference material to every one of them. Apart, they compile as a unit
with each other, plus the one file below them that the tour reaches by `import`, which is the
only way anything reaches it.

"Every construct" is a test rather than an aspiration: `TourCoverageTests` reads the tour alone
and holds it against every reserved word, every symbol, every node the parser can build, and
every modifier, operator, literal form and receiver. The one exception is stated there too — a
file-scoped namespace claims the whole file it is written in, so no file can hold one alongside
the block form, and the tour opens with blocks. [namespaces.cm](samples/namespaces.cm) writes
that form, with blocks nested inside it, which is how the two combine.

The whole corpus reaches the back end. Every program is built, run, and compared against what
the interpreter printed; the two files that are not programs are built as libraries and their
CIL verified. A file no build gathers fails `EverySampleFileIsReachedByABuild` by name — which
is the check that would have caught `cm build` crashing on a nested type while several thousand
tests passed.

**That corpus, not [the grammar file](docs/grammar.ebnf), is what pins the syntax.** Nothing
reads the grammar — no parser is generated from it, and no build step checks it. Compass is
parsed by hand-written recursive descent, one method per production, with expressions handled
by precedence climbing against the table in `src/Compass.Compiler/Ast/Operators.cs`, so the
grammar can drift from the compiler without anything failing. The samples cannot: each is
checked against a recorded token stream and a recorded tree on every build, and the suite
asserts that between them they reach every node the parser can build.

Two samples are more than one file, and each shows a different way of saying so:

| Sample | What it is for |
|---|---|
| [bookshelf/](samples/bookshelf/) | **A folder is enough.** `Program.cm` beside `Book.cm` and `Shelf.cm`, with nothing said to connect them |
| [storefront/](samples/storefront/) | **A project across folders.** [storefront.cmp](samples/storefront/storefront.cmp) lists a file and two folders, and says with `output` where its build goes |
| [observatory/](samples/observatory/) | **Two programs, one project.** Both declare a `Program`, in different namespaces, over shared code — and [observatory.cmp](samples/observatory/observatory.cmp) says with `entry` which one begins |
| [toolkit/](samples/toolkit/) | **Naming a file directly.** An `import` reaches into `shared/`, and what it names imports one more |
| [library/](samples/library/) | **A project built on another.** [library.cmp](samples/library/library.cmp) references `books/books.cmp` and uses its types |

Each runnable sample's output is recorded under `tests/Compass.Tests/TestData/Running/` and
asserted on every build, so a sample that starts printing the wrong answer fails the suite.

### Samples that fail on purpose

`samples/negatives/` is the other half. A language is defined as much by what it turns away as
by what it accepts, so these are held to the same standard: each file gathers a group of
related mistakes and explains the lesson in its comments, and a reader sees the mistake and
the message side by side.

Programs the compiler rejects:

| Sample | Mistakes |
|---|---|
| [csharp-habits.cm](samples/negatives/compile/csharp-habits.cm) | `&&`, `\|\|`, `!`, `+=`, `++`, `**`, and a typed range counter |
| [optionals.cm](samples/negatives/compile/optionals.cm) | Using an optional without proving it holds something |
| [definite-assignment.cm](samples/negatives/compile/definite-assignment.cm) | Reading a variable before it has a value; a constant with none |
| [types.cm](samples/negatives/compile/types.cm) | Types that do not mix, and a division by a literal zero |
| [numbers.cm](samples/negatives/compile/numbers.cm) | Numbers written past the edge of what holds them — including `-9223372036854775808`, which is the most negative integer and still one past the largest, because the minus is a separate operator |
| [members.cm](samples/negatives/compile/members.cm) | A function used as a property, an instance member reached through its type, a call that yields nothing |
| [shadowing.cm](samples/negatives/compile/shadowing.cm) | Names taken again while an enclosing scope is still using them — a block's local, a lambda's parameter, a loop binding, a caught exception |
| [naming.cm](samples/negatives/compile/naming.cm) | One name claimed by two members — two fields, a field beside a function, two functions taking the same types — alongside the overloads that are correct |
| [overloads.cm](samples/negatives/compile/overloads.cm) | Calls that do not settle which version they mean: two reachable only by widening, a lambda fitting both its shape and `Function`, an argument no version takes, a count no version has, and a fraction that does not become a real on its own |
| [nesting.cm](samples/negatives/compile/nesting.cm) | A nested type reached by a bare name from outside the type holding it, and another named correctly but kept private by its container |
| [throwaway.cm](samples/negatives/compile/throwaway.cm) | A `_` written where no name was asked for, read back, handed on, and used to name a field, a function, a parameter, a type and a namespace — beside a local nothing reads and a private member nothing reaches |
| [blocks.cm](samples/negatives/compile/blocks.cm) | An `end` that closes the wrong construct |
| [switching.cm](samples/negatives/compile/switching.cm) | A switch on a real, a label that is not constant, one value handled twice, and a member left unhandled |
| [results.cm](samples/negatives/compile/results.cm) | A function that never reaches the result it promises, and a call that yields nothing used as a value |
| [imports.cm](samples/negatives/compile/imports.cm) | Imports naming a file that is not there, and one that is not Compass |
| [visibility.cm](samples/negatives/compile/visibility.cm) | Reaching a private and a protected member from outside, two visibilities on one declaration, and `protected` on a type |
| [overriding.cm](samples/negatives/compile/overriding.cm) | `override` matching nothing, overriding a function that is not `virtual`, yielding something else, and hiding one without saying so |
| [bits.cm](samples/negatives/compile/bits.cm) | `xor` on two booleans, bit operations on a real and a fraction, a shift past the width of an integer, and a word after `bitwise` that is neither `and` nor `or` |
| [looping.cm](samples/negatives/compile/looping.cm) | Inserting into, removing from, and clearing the very sequence a `loop each` is walking, walking a number and an optional set, and four range loops that cannot work: a step of zero, a step pointing away, a bound already behind, and an exclusive bound already reached |
| [conditions.cm](samples/negatives/compile/conditions.cm) | A number and an optional asked to be a condition, in all six places one is taken: `if`, `loop while`, `loop … until`, the `if` expression, an operand of `and`, and `not` |
| [closures.cm](samples/negatives/compile/closures.cm) | Misreadings of what a kept function names — assigning to a loop counter, hiding a name it kept, and reaching for an instance a shared member does not have |
| [ignoring.cm](samples/negatives/compile/ignoring.cm) | An `ignore` naming no diagnostic, one naming a diagnostic nothing here reports, and one trying to silence an error — which fires anyway |
| [documenting.cm](samples/negatives/compile/documenting.cm) | A documented parameter the function does not take, `@yields:` on a function that yields nothing, a label written twice, and a doc above a statement |
| [abstract.cm](samples/negatives/compile/abstract.cm) | A function left open on a model that can be constructed, a body where there should be none and none where there should be one, and a model that never writes what it inherited |
| [narrowing.cm](samples/negatives/compile/narrowing.cm) | A proof read past the point it ran out — a branch that may not run, a loop, a turn after the first, a `catch`, an arm that only sometimes leaves, and a name a kept function assigns |
| [constructing.cm](samples/negatives/compile/constructing.cm) | **Building a thing in the wrong order.** A `base(...)` written below a line that reads the parent, a child with no way to build a parent that needs something, and a field's starting value reaching for `this` |

Programs that compile and then fail, because the answer depends on a value the compiler cannot
see:

| Sample | Failure |
|---|---|
| [divide-by-zero.cm](samples/negatives/runtime/divide-by-zero.cm) | `DivideByZeroException` — a divisor that arrived in a variable |
| [index-out-of-range.cm](samples/negatives/runtime/index-out-of-range.cm) | `IndexOutOfRangeException` — one index past the end |
| [empty-optional.cm](samples/negatives/runtime/empty-optional.cm) | `EmptyOptionalException` — `Value()` on an optional that turned out empty |
| [sequence-changed.cm](samples/negatives/runtime/sequence-changed.cm) | `SequenceChangedException` — a set cleared during its own `loop each`, reached through a parameter where no compile-time rule could see it |
| [overflow.cm](samples/negatives/runtime/overflow.cm) | `OverflowException` — a factorial too large for an integer |
| [runaway-recursion.cm](samples/negatives/runtime/runaway-recursion.cm) | Recursion with no base case |
| [uncaught-exception.cm](samples/negatives/runtime/uncaught-exception.cm) | A declared exception no catch clause matches |

Projects that will not build:

| Sample | Mistake |
|---|---|
| [two-programs.cmp](samples/negatives/project/two-programs.cmp) | Two files that each declare `Program` |
| [ambiguous-entry.cmp](samples/negatives/project/ambiguous-entry.cmp) | Two programs in different namespaces, and no `entry` saying which one begins |
| [unknown-entry.cmp](samples/negatives/project/unknown-entry.cmp) | Words a project file does not have |
| [missing-source.cmp](samples/negatives/project/missing-source.cmp) | A path that is not there, and one listed twice |
| [circular.cmp](samples/negatives/project/circular.cmp) | Two projects referencing each other, so neither can be built first |
| [quiet-and-empty.cmp](samples/negatives/project/quiet-and-empty.cmp) | An `ignore` naming neither a severity nor a diagnostic, in a project that names nothing to build |
| [unclear-output.cmp](samples/negatives/project/unclear-output.cmp) | An `output` naming no folder, and two more disagreeing about which one |

How each one fails is recorded under `tests/Compass.Tests/TestData/Negatives/` and asserted on
every build, holding the wording as well as the outcome. A compile sample must be rejected
with the diagnostics recorded for it; a runtime sample must compile with no diagnostics at all
and then fail with the message recorded for it — one that stops compiling is testing something
else and belongs under `compile/` instead.

## Repository layout

```
src/
  Compass.Compiler/     lexer, parser, semantic analysis, lowering, CIL emission
  Compass.Runtime/      the value types a program uses: fraction, set, deep equality
  Compass.Interpreter/  runs the lowered tree, and decides where a debugger stops
  Compass.Services/     what an editor asks: completion, hover, rename, every use of a name
  Compass.Cli/          the compass command, the language server, and the debug adapter
  Compass.Cli.Alias/    cm, the short name for the same command
  Compass.Wasm/         the front end and the interpreter, built to run in a browser
tests/
  Compass.Tests/
docs/
  standard-library/    every type and every member, indexed by name
samples/               .cm programs, one per file
  bookshelf/           one program across three files in a folder
  storefront/          one program across three folders, listed by a .cmp
  observatory/         two programs in one project, with an entry saying which begins
  toolkit/             one program reaching other files by import
  library/             one project referencing another
  reference/           tour.cm and the scanner corpus; libraries, not programs
  negatives/           programs that are wrong on purpose
```

## License

MIT. See [LICENSE](LICENSE).
