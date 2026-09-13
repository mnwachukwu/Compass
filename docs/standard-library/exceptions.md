# Exceptions

[← Back to the index](README.md)

`Exception` is the root of everything that can be thrown, and one of only two built-in models a
program may extend — the other being `Model`.

| Section | Members |
|---|---|
| [The member every exception carries](#the-member-every-exception-carries) | `Message` |
| [Declaring your own](#declaring-your-own) | — |
| [What the language raises](#what-the-language-raises) | the eleven names |
| [When to throw and when to yield an optional](#when-to-throw-and-when-to-yield-an-optional) | — |

## The member every exception carries

| Member | Yields | What it does |
|---|---|---|
| `Message()` | `string` | What went wrong, as text |

Carried by every exception, including one a program declares.

```
integer divisor = 0;

try
    integer half = 10 / divisor;
catch DivideByZeroException problem
    Console.WriteLine(problem.Message());
end try
```

**The divisor arrives in a variable on purpose.** `10 / 0` written out does not compile:
`CM0324` reports a zero the compiler can see, leaving nothing to catch at run time. The exception
covers the zero it cannot see.

## Declaring your own

Extend `Exception` and pass the message to `base(...)`. `Exception` declares no constructor a
program can see, but it accepts the message every exception carries, so `base("...")` is what
`Message()` later returns.

```
model NotEnoughMoney extends Exception
    public function NotEnoughMoney(string why)
        base(why);
    end function
end model

shared model Program
    function Main()
        try
            throw new NotEnoughMoney("the account is empty");
        catch NotEnoughMoney problem
            Console.WriteLine(problem.Message());
        end try
    end function
end model
```

**Catching by an ancestor catches the children too.** A `catch Exception` reaches everything, and a
`catch` on a model you declared reaches anything extending it.

## What the language raises

Eleven names. Each is the same name at run time as the one a program writes. A `catch` can take
all of them except `RecursionTooDeepException`.

| Exception | Raised when |
|---|---|
| `Exception` | The root; catches every one below |
| `DivideByZeroException` | Dividing by a zero the compiler could not see, including a zero denominator in `Fraction.Create` |
| `IndexOutOfRangeException` | An index outside the set or string it is used on |
| `EmptyOptionalException` | `Value()` on an [optional](optionals.md) that turned out empty |
| `SequenceChangedException` | A [set](sets.md) changed while a `loop each` was walking it |
| `InvalidCastException` | An `as` to a type the value is not |
| `FormatException` | A pattern `Format` does not recognize |
| `ArgumentException` | A value a member cannot work with |
| `OverflowException` | A number grown too large to hold, including a fraction's parts |
| `RecursionTooDeepException` | Recursion with no base case; **nothing catches this one** |
| `IOException` | Anything that goes wrong with a file except its absence |

**`RecursionTooDeepException` is nameable but not catchable.** The name exists so the message can
say what stopped the program. It cannot be handled because the stack that would run the handler is
the stack that ran out. A `catch` naming it is reported (`CM0344`), so it cannot sit in a program
looking like a handler.

**Absence is never an exception.** A file that is not there, text that does not read as a number,
and input that has run out each yield an [optional](optionals.md) instead. Each is an ordinary
outcome rather than a fault.

**The names are .NET's; the messages are not.** A name learned here means the same thing in C#, so
none were renamed. The text is written for this language. C# reports "Attempted to divide by
zero"; Compass reports that a literal zero divisor would have been refused while compiling, so
this one arrived in a variable.

**`catch Exception` takes less here than C#'s `catch (Exception)` does.** It takes what the
program caused, not a failure in the implementation. In C# the root clause takes everything,
including a bug in a library, so a `catch (Exception)` can report another component's defect as
the program's. `RecursionTooDeepException` is uncatchable for the same reason C#'s
`StackOverflowException` is. It does not reuse that name, because a name shared with C# behaves
as C#'s does.

## Two with non-obvious behavior

**`OverflowException` on a fraction rarely names the operand at fault.** Denominators multiply
every time two unlike fractions are added, so a long chain can outgrow an integer while no single
fraction is large.

**`SequenceChangedException` has a compile-time counterpart.** Where the compiler can see a set
being changed inside a walk of itself, it reports an error instead. The exception covers the cases
it cannot see, such as a set reached through a parameter.

## When to throw and when to yield an optional

The rule the library follows: **throw when the caller has made a claim that turned out false;
yield an optional when the answer may not exist.**

`Value()` on an empty optional throws, because reaching that line required proving to the compiler
that the value was present. `File.Read` on a missing file yields nothing, because nothing claimed
the file existed.

## Also on every exception

[`ToString()` and `Equals()`](every-value.md).
