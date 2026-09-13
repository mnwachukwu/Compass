# Numbers

[← Back to the index](README.md)

**Four number types**, and the capitalized names beside them.

| Type | What it is | Written |
|---|---|---|
| `integer` | A whole number, 64 bits | `42` |
| `real` | A number with a decimal point, counted **in tens** and exact about them | `3.14` |
| `float` | Binary floating point: `float` or `double` in C, C#, Java, and Go | `3.14f` |
| `fraction` | An **exact** ratio of two whole numbers | `22\|7` |

| Section | Members |
|---|---|
| [Members on a number](#members-on-a-number) | `Format` `ToFloat` `ToReal` `ToFraction` `Reciprocal` `Numerator` `Denominator` |
| [Writing a number out](#writing-a-number-out) | `Format` |
| [Fraction](#fraction) | `Fraction.Create` |
| [What each type knows about itself](#what-each-type-knows-about-itself) | `Integer.MaxValue` `Integer.MinValue` `Real.MaxValue` `Real.MinValue` `Float.MaxValue` `Float.MinValue` `Integer.Parse` `Real.Parse` `Float.Parse` `Fraction.Parse` |
| [What only a float has](#what-only-a-float-has) | `Float.Infinity` `Float.NegativeInfinity` `Float.NotANumber` |
| [Writing one too large](#writing-one-too-large) | — |
| [The whole conversion chart](#the-whole-conversion-chart) | — |
| [Crossing between a real and a float](#crossing-between-a-real-and-a-float) | `ToFloat` `ToReal` `ToFraction` |

Arithmetic reached through a name lives on its own page: [`Math`](math.md) for roots, logarithms,
angles, and rounding, and [`Random`](random.md) for chance.

**`real` is not floating point.** A tenth cannot be written exactly in binary, so `0.1` in most
languages is the nearest binary number to a tenth rather than a tenth. A `real` holds the digits
as digits, and the arithmetic comes out as written:

```
Console.WriteLine((0.1 + 0.2) == 0.3);      # true
Console.WriteLine((0.1f + 0.2f) == 0.3f);   # false
```

Both exist for that reason. `real` is what a decimal point means in this language. `float` is
what a decimal point means in most others, and it is named rather than implied so that binary
floating point is asked for deliberately.

`fraction` is exact where neither can be. A third has no decimal form that ends and no binary one
either:

```
Console.WriteLine(1|3 + 1|3 + 1|3);   # 1|1
```

## Members on a number

| On | Member | Yields | What it does |
|---|---|---|---|
| `integer` | `Format(string pattern)` | `string` | Written out by a pattern |
| `integer` | `ToFloat()` | `float` | The same whole number in binary |
| `real` | `Format(string pattern)` | `string` | Written out by a pattern |
| `real` | `ToFloat()` | `float` | The same number in binary, to about sixteen digits |
| `float` | `Format(string pattern)` | `string` | Written out by a pattern |
| `float` | `ToReal()` | `real` | The same number in tens, where there is one |
| `float` | `ToFraction()` | `fraction` | The ratio it is really holding |
| `fraction` | `Format(string pattern)` | `string` | Written out by a pattern |
| `fraction` | `ToReal()` | `real` | The nearest real |
| `fraction` | `ToFloat()` | `float` | The nearest float |
| `fraction` | `Reciprocal()` | `fraction` | The fraction turned over |
| `fraction` | `Numerator` | `integer` | The number above the line |
| `fraction` | `Denominator` | `integer` | The number below it, never zero and never negative |

A `real` has no `ToFraction()`, and needs none: it counts in tens, so it already is a fraction
over a power of ten and converts on its own. The [chart below](#the-whole-conversion-chart) has
every direction in one place.

**`Reciprocal` is exact where dividing is not.** A third turned over is three, while
`1.0 / (1.0 / 3.0)` is not quite one, in tens or in binary.

```
fraction third = 1|3;

Console.WriteLine(third.Reciprocal());   # 3|1
Console.WriteLine(third.ToReal());       # 0.3333333333333333333333333333
```

**`Numerator` and `Denominator` answer for the reduced fraction, not for what was written.** A
fraction reduces when it is made, so `2|4` is `1|2` and the `2` and the `4` are not kept. The sign
is held on the numerator, so a denominator is never negative.

Both are read without parentheses. Nothing is worked out to answer them: a fraction is already
kept as these two numbers.

```
Console.WriteLine((2|4).Numerator);      # 1
Console.WriteLine((2|4).Denominator);    # 2
Console.WriteLine((5|1).Denominator);    # 1
```

<a id="writing-a-number-out"></a>

## Writing a number out

`Format` takes **.NET's own patterns, unchanged**: `F2` is two decimal places, `N0` is a whole
number with separators, `P1` is a percentage. A pattern learned here is the same pattern .NET
takes. A pattern the runtime does not recognize raises `FormatException` rather than producing
unexpected output.

```
real price = 1234.5678;

Console.WriteLine(price.Format("F2"));   # 1234.57
Console.WriteLine(price.Format("N0"));   # 1,235
Console.WriteLine(42.Format("D5"));      # 00042
```

## `Fraction`

Note the two spellings: **`fraction` is the type** and a reserved word; **`Fraction` is the model**
beside it, holding what a fraction needs that is not a member of one.

| Member | Yields | What it does |
|---|---|---|
| `Fraction.Create(integer numerator, integer denominator)` | `fraction` | A fraction from two values |
| `Fraction.Create(integer whole)` | `fraction` | That whole number over one |

A fraction literal is two numerals fixed when the program is written, so `Create` is the only way
to build one from values that exist while it runs. The result is an ordinary fraction: reduced,
with its sign on the numerator.

**A denominator of zero is rejected while compiling where it can be seen**, exactly as `1 / 0` is.
Written as a literal it always can be, so `1|0` is `CM0027`; built from values it cannot, so
`Fraction.Create(top, 0)` raises `DivideByZeroException`.

The one-argument form is for where nothing else states the type: `let f = 3;` holds an integer,
and `let f = Fraction.Create(3);` holds `3|1`.

```
integer top = 22;
integer bottom = 7;

Console.WriteLine(Fraction.Create(top, bottom));   # 22|7
Console.WriteLine(Fraction.Create(4, 8));          # 1|2 — reduced
```

## What each type knows about itself

Each number has a capitalized name beside its keyword, holding the facts about it. The keyword
names the type and the capital names where those facts live. A reserved word cannot stand in
front of a dot, so `integer.MaxValue` does not parse and `Integer.MaxValue` does. `Fraction`
already reads this way beside `fraction`.

Bounds belong to the numbers. Where a number runs out is a fact about that number. A `character`
has no equivalent, since where the alphabet stops is a fact about how text is stored rather than
about the language, so `Character` carries only the `Parse` that all of these have.

| Member | Yields | What it is |
|---|---|---|
| `Integer.MaxValue` | `integer` | The largest whole number, 9223372036854775807 |
| `Integer.MinValue` | `integer` | The smallest, which has no literal — the minus is a separate operator, so this name is the only way to write it |
| `Real.MaxValue` | `real` | The largest real, about 79 followed by 27 zeros |
| `Real.MinValue` | `real` | Its negative |
| `Float.MaxValue` | `float` | The largest finite float, about 1.8 times ten to the 308th |
| `Float.MinValue` | `float` | Its negative |
| `Integer.Parse(string)` | `integer?` | The number the text spells, or nothing |
| `Real.Parse(string)` | `real?` | The same, as a `real` |
| `Float.Parse(string)` | `float?` | The same, as a `float` |
| `Fraction.Parse(string)` | `fraction?` | The ratio the text spells, or nothing |

`Parse` is the other spelling of the string's own
[`ToInteger` and its family](text.md#reading-a-value-back-out). One method answers both, so the
two cannot disagree. A string already held reads as `typed.ToInteger()`; text arriving from
elsewhere reads as `Integer.Parse(typed)`.

A `string` has a capitalized name too, holding [`String.Empty`](text.md#string). That is not a
bound, but it is the same arrangement: a fact about the type, kept where a reserved word cannot
reach. So do `boolean` and `character`, each holding
[nothing but a `Parse`](text.md#boolean-and-character).

### What only a `float` has

| Member | Yields | What it is |
|---|---|---|
| `Float.Infinity` | `float` | What `1.0f / 0.0f` produces |
| `Float.NegativeInfinity` | `float` | What `-1.0f / 0.0f` produces |
| `Float.NotANumber` | `float` | What `0.0f / 0.0f` produces — and the one value **not equal to itself**, so comparing against this name is always false |

Each is the value a float's own arithmetic produces, so `1.0f / 0.0f == Float.Infinity` is true.
**A float is the one type allowed to divide by a zero written down.** For every other type
`CM0324` refuses it while compiling, since there is no answer to give. A float has one, so the
expression stands and the constant names the result.

A `real` has none of these. It counts in tens and has nowhere to hold them, so it stops where a
float continues into an infinity, as an `integer` does.

`NotANumber` is written out rather than abbreviated, the way this language writes `shiftleft`
and `bitwise and`. It prints as that word too, so what a reader sees and what they would write
are the same.

### Writing one too large

A number written past its type's edge is reported (`CM0026`) rather than wrapping, saturating, or
converting to something else. The digits scan as a number; only storing them fails, so the
refusal comes after scanning rather than during it.

```text
integer counted = 9223372036854775808;   # CM0026 — one past Integer.MaxValue
real measured = 1e400;                   # CM0026 — past Real.MaxValue
```

**The most negative integer has no literal.** The minus sign is a separate operator, so
`-9223372036854775808` is a minus applied to a number one past the largest, and both halves are
reported together. `Integer.MinValue` is how it is written, and is why the name exists.

**A float is the exception.** It is the one type with a value for a number too large, so a float
literal past its edge becomes that value rather than being refused, which matches what its own
arithmetic does:

```
Console.WriteLine(1e400f);          # Infinity
Console.WriteLine(1.0f / 0.0f);     # Infinity — the same answer, reached the other way
```

## The whole conversion chart

Read a row as *from*, a column as *to*. **Bold** happens on its own; anything else is written out.

| from ↓ to → | `integer` | `real` | `float` | `fraction` |
|---|---|---|---|---|
| **`integer`** | — | **automatic** | `.ToFloat()` | **automatic** |
| **`real`** | `Math.Round(x)` | — | `.ToFloat()` | **automatic** |
| **`float`** | `Math.Round(x)` | `.ToReal()` | — | `.ToFraction()` |
| **`fraction`** | `Math.Round(x)` | `.ToReal()` | `.ToFloat()` | — |

The table names `Math.Round`, but [`Math.Floor` and `Math.Ceiling`](math.md#rounding) also yield
an `integer`. Each of the three takes a real, a float, or a fraction and answers with a whole
number, and each names the direction it rounds. A cast would name none of them.

### One rule, and its two exceptions

**A conversion that loses nothing happens on its own.** That is the bold column group: every
whole number is a real and is a ratio over one, and a real counts in tens, so it already *is* a
ratio over a power of ten.

Two conversions lose nothing and are still written out, because the result does not look like the
value that produced it:

- **`fraction.ToReal()`** — a third has no decimal that ends, so `1|3` becomes `0.3333…` and does
  not multiply back to one.
- **`float.ToFraction()`** — `0.1f` is `3602879701896397|36028797018963968`, which is the exact
  value a binary float holds for a tenth.

**Nothing reaches a `float` on its own**, an integer included. If a whole number widened to both a
real and a float, every member of `Math` would have two readings and `Math.Sqrt(2)` would be
ambiguous. Widening goes to a real, and a float is asked for by name.

### What each conversion can cost

| Conversion | Can it fail? | What is lost |
|---|---|---|
| `integer → real`, `integer → fraction` | no | nothing |
| `real → fraction` | **yes** — `CM0346` where the compiler can see the value, otherwise at run time | nothing; the parts can outgrow a whole number |
| `real → float`, `integer → float` | no | digits past the sixteenth |
| `fraction → real`, `fraction → float` | no | thirds and the like stop being exact |
| `float → real` | **yes**, three ways | see below |
| `float → fraction` | **yes**, on an infinity or a value that is not a number | nothing otherwise — the ratio is exact |
| `float → integer` | **yes**, on an infinity or a value that is not a number | everything after the point, which is what rounding was asked for |
| `real → integer`, `fraction → integer` | no | everything after the point, which is what rounding was asked for |

**Failing is not the same as being written out.** `real → fraction` is the one conversion that
happens on its own and can still stop. It loses nothing, which is why it needs no asking, but a
real carrying more places than a fraction's two whole numbers can hold has no fraction to become.
Where the compiler can see the value it reports `CM0346`; otherwise the program stops on the line
that converts.

**Only a float can fail this way.** A real and a fraction have no infinity and no not-a-number, so
every value of either is a number and every conversion out of one loses digits or loses nothing. A
float has three values that name no number, and `ToReal`, `ToFraction`, and rounding to an
`integer` all refuse them rather than substituting a number.

## Crossing between a `real` and a `float`

Both directions are written out. Each changes the value in the way the table below states.

| Member | Yields | What it does |
|---|---|---|
| `real.ToFloat()` | `float` | The same number in binary. **Always answers**: every real fits well inside a float's range. What is lost is digits — a real holds about twenty-eight and a float sixteen |
| `float.ToReal()` | `real` | The same number in tens. **Can fail three ways**, and silently changes what it does convert |
| `float.ToFraction()` | `fraction` | The exact ratio the float holds. **Stops on an infinity or a value that is not a number**, neither of which is a ratio |

A `real` needs no `ToFraction()`: it counts in tens, so it already is a fraction over a power of
ten and widens on its own.

### What `ToReal()` can do to a number

```
Console.WriteLine((1.5f).ToReal());         # 1.5

# The same float, asked two ways.
Console.WriteLine((0.1f).ToFraction());     # 3602879701896397|36028797018963968
Console.WriteLine((0.1f).ToReal());         # 0.1
```

The last two lines read one value. `ToFraction` yields the exact ratio it holds; `ToReal` yields
`0.1`, the shortest decimal that rounds to it. **Nothing is reported**, and the value that comes
back is not the value that went in, which is why the conversion is written out rather than
applied automatically.

The three failures are simpler: a float larger than a real can hold, an infinity, and a value
that is not a number. A real has no form for any of them, so each stops.

## Also on every number

[`ToString()` and `Equals()`](every-value.md).
