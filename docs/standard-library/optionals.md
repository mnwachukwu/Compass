# Optionals

[← Back to the index](README.md)

A `T?` holds a `T` or holds nothing. It has three members and no others, and it is what the
language has in place of `null`.

The rule around those members is the substance: **the compiler will not read an optional it
cannot prove is present.** Reading a value that might be absent is a compile error rather than a
run-time fault.

| Section | Members |
|---|---|
| [Members](#members) | `HasValue` `Value` `Or` |
| [HasValue narrows](#hasvalue-narrows) | `HasValue` |
| [Or supplies a fallback](#or-supplies-a-fallback) | `Or` |
| [Where optionals come from](#where-optionals-come-from) | — |
| [A set of optionals](#a-set-of-optionals) | — |

## Members

| Member | Yields | What it does |
|---|---|---|
| `HasValue()` | `boolean` | Whether a value is present |
| `Value()` | `T` | The value, refused unless the compiler can prove one is present |
| `Or(T fallback)` | `T` | The value, or `fallback` |
| `Or(T? fallback)` | `T?` | The value, or the other optional |

## `HasValue` narrows

Inside a block guarded by `HasValue()`, the compiler knows the value is present and `Value()` is
allowed.

```
string? typed = Console.Read();

if typed.HasValue()
    # Legal here and nowhere else: the guard is what proves it.
    Console.WriteLine("you typed " + typed.Value());
else
    Console.WriteLine("nothing was typed");
end if
```

Written without the guard, `typed.Value()` is `CM0401`: a compile error rather than a run-time
fault.

## `Or` supplies a fallback

The shorter form, and the one to use where there is a sensible default.

```
string name = Console.Read().Or("stranger");
Console.WriteLine("hello, " + name);
```

**The fallback is not evaluated unless it is needed**, so an expensive one costs nothing when the
optional turns out to be present.

## The two forms of `Or`, and chaining

Given a plain value, `Or` **ends** the chain and yields a `T`. Given another optional, it yields
a `T?`, so fallbacks can be run one after another:

```
string? fromFile = File.Read("settings.txt");
string? fromInput = Console.Read();

# Each step stays optional until the last, which ends it.
string chosen = fromFile.Or(fromInput).Or("a built-in default");
```

`fromFile.Or(fromInput)` is still a `string?`, because both might be absent. `.Or("...")` is a
`string`, because that one cannot be.

## When there is nothing to fall back on

`Value()` on an optional that turns out empty raises `EmptyOptionalException`. The compiler had
been given proof the value was present, so this is a claim that turned out false rather than a
missing check. See [exceptions](exceptions.md).

## Where optionals come from

The library yields one wherever an answer may not exist:

| From | Yields | Absent when |
|---|---|---|
| [`Console.Read()`](input-output.md#console) | `string?` | The input has run out |
| [`File.Read(path)`](input-output.md#file) | `string?` | There is no such file |
| [`"12".ToInteger()`](text.md#reading-a-value-back-out) | `integer?` | The text does not spell a number |
| [`DateTime.Parse(text)`](dates-and-times.md) | `DateTime?` | The text does not read as a moment |

**Absence is an answer, not a fault**, so none of these raises. `File.Exists` followed by
`File.Read` is a race: the file can be removed between the two calls. Read it and handle the
absence.

## A set of optionals

`T?[]` holds values that may each be absent, and has
[four members of its own](sets.md#dropping-the-empties) for removing them. `TrimAll` yields a
`T[]`, which ends the unwrapping.

## Comparing them

`==` and `!=` work on an optional without any proof being needed, and they are the only things
that do. Two optionals are equal when both hold nothing, or when both hold values that are equal;
an optional compared against a plain value is equal when it holds one equal to it, so an absence
equals no value at all.

```
integer? held = 41;
integer? same = 41;
integer? gone;

Console.WriteLine(held == same);      # true
Console.WriteLine(held == 41);        # true — the value widens to an optional
Console.WriteLine(gone == 41);        # false
Console.WriteLine(gone == held);      # false
```

Comparing does not read what is held, which is why it needs no check first. Everything that does
read it — printing it, joining it to a string, reaching a member of it — still does.

## Also on every optional

Nothing else. An optional does **not** answer [`ToString()` or `Equals()`](every-value.md). Both
are members, and a member call reads what the optional holds, which requires proof. `==` is an
operator rather than a member, so it answers where `Equals()` refuses.
