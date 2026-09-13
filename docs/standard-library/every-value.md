# Every value

[← Back to the index](README.md)

Two members exist on every type: a primitive, a set, an enumeration member, a structure, a model
a program declared, and a model the language provides. Both come from `Model`, which every type
descends from.

**An optional answers neither.** A member call on an optional reads what it holds, which requires
proof that a value is there, so `ToString()` and `Equals()` are refused like any other member.
`==` between two optionals needs no proof;
[optionals.md](optionals.md#comparing-them) gives the rules.

| Section | Members |
|---|---|
| [Members](#members) | `ToString` `Equals` |
| [What Equals compares](#what-equals-compares) | `Equals` |
| [Reference.Equals](#referenceequals) | `Reference.Equals` |
| [Enumerations](#enumerations) | `ToInteger` |

## Members

| Member | Yields | What it does |
|---|---|---|
| `ToString()` | `string` | The value written out the way `Console.WriteLine` would write it |
| `Equals(anything)` | `boolean` | Whether two values are the same, compared by what they hold |

`Equals` accepts a value of **any** type. Comparing two values that could never be equal is not
refused; it answers `false`.

Both are `virtual`, so any type may write its own, structures included. Neither boxes: each
compiles to a direct call, as `5.ToString()` does in C#.

### What `ToString` says when nothing overrides it

| Type | Default |
|---|---|
| A structure | Field by field, as `Point { X = 1, Y = 2 }` |
| An enumeration | The member's name |
| A model | The type's name |

**A structure cannot contain itself**, so walking its fields always finishes. A model can take
part in a cycle. `==` handles that with cycle-safe bisimulation, which has no counterpart for
printing, so a model prints its type name unless it declares `ToString`.

A declared `ToString` is what a value prints everywhere: written out, printed on its own, joined to
a string with `+`, or inside a set. Each dispatches on the runtime type, so printing and calling
never disagree.

```
integer count = 3;
string name = "Ada";

Console.WriteLine(count.ToString());        # 3
Console.WriteLine(name.Equals("Ada"));      # true
Console.WriteLine(count.Equals(name));      # false
```

## What `Equals` compares

**What the value holds, not where it lives.** `==` asks the same question. It recurses: two models
are equal when every field is equal, and a field holding a model is compared the same way.

```
model Point
    public integer x;
    public integer y;

    public function Point(integer across, integer up)
        this.x = across;
        this.y = up;
    end function
end model

shared model Program
    function Main()
        Point here = new Point(1, 2);
        Point there = new Point(1, 2);

        Point alsoHere = here;

        Console.WriteLine(here.Equals(there));                 # true — same numbers
        Console.WriteLine(Reference.Equals(here, there));      # false — two objects
        Console.WriteLine(Reference.Equals(here, alsoHere));   # true — one object, two names
    end function
end model
```

**Cycles terminate.** A structure that reaches itself through a model, or a pair of models
pointing at one another, compares without looping: `Equals` records the pairs it is already
comparing and treats a repeat as equal.

## `Reference.Equals`

Reached through the model's name rather than through a value, because it is a question about two
things rather than about one.

| Member | Yields | What it does |
|---|---|---|
| `Reference.Equals(anything, anything)` | `boolean` | Whether both names reach the same object |

**It is the only way to ask that question.** Comparing by identity is the rarer thing to want, and
`==` answers about values, so the identity form is written out in full rather than given an
operator.

**A structure is refused.** A structure is copied when it is passed, so whether two of them are
the same object depends on where the compiler put them. The refusal is a compile error, not
`false` at run time.

## Enumerations

An enumeration member answers one more:

| Member | Yields | What it does |
|---|---|---|
| `ToInteger()` | `integer` | The ordinal behind the member |

```
enumeration Suit
    Hearts,
    Spades,
    Clubs = 10,
    Diamonds
end enumeration

shared model Program
    function Main()
        Console.WriteLine(Suit.Hearts.ToInteger());     # 0
        Console.WriteLine(Suit.Clubs.ToInteger());      # 10
        Console.WriteLine(Suit.Diamonds.ToInteger());   # 11 — one past the last one written
        Console.WriteLine(Suit.Hearts.ToString());      # Hearts
    end function
end model
```

**The conversion goes one way.** There is no `ToSuit(0)`: an integer naming no member would have
to produce a value that is not a `Suit`, and an enumeration holds only the members it declares.
