# Sets

[← Back to the index](README.md)

Everything a `T[]` answers, whatever `T` is. A Compass set **keeps its order** and **allows a
value twice**, so it is C#'s `List<T>` rather than the set of mathematics.
[`Distinct`](#distinct) produces the mathematical one.

A set's members mirror [a string's](text.md), so the two read alike.

| Section | Members |
|---|---|
| [Asking about it](#asking-about-it) | `Count` `Contains` `IndexOf` |
| [Changing it](#changing-it) | `Insert` `InsertAt` `Remove` `RemoveAt` `Clear` |
| [Taking a run](#taking-a-run) | `Subset` |
| [Two sets read together](#two-sets-read-together) | `Union` `Intersect` `Except` `Distinct` |
| [Joining](#joining) | `Join` |
| [Dropping the empties](#dropping-the-empties) | `Trim` `TrimStart` `TrimEnd` `TrimAll` |
| [Sets of sets](#sets-of-sets) | — |

Unlike a string, **a set can be changed**. `Insert` and `Remove` alter the set they are called on.
`Subset`, `Union`, and the rest yield a new set and leave the original alone. The tables below say
which is which.

## Asking about it

| Member | Yields | What it does |
|---|---|---|
| `Count` | `integer` | How many elements |
| `Contains(T what)` | `boolean` | Whether `what` is in it |
| `IndexOf(T what)` | `integer` | Where `what` first sits, or `-1` where it is absent |

```
integer[] scores = {70, 85, 85, 90};

Console.WriteLine(scores.Count);        # 4
Console.WriteLine(scores.Contains(85));   # true
Console.WriteLine(scores.IndexOf(85));    # 1 — the first one
```

## Changing it

These five change the set in place and are the only members that do.

| Member | Yields | What it does |
|---|---|---|
| `Insert(T what)` | nothing | Adds `what` to the end |
| `InsertAt(integer where, T what)` | nothing | Puts `what` in at `where` |
| `Remove(T what)` | `boolean` | Takes the first `what` out; whether there was one |
| `RemoveAt(integer where)` | nothing | Takes out whatever is at `where` |
| `Clear()` | nothing | Empties it |

`Remove` is the only mutator that yields anything, matching the `List<T>` it is built on. It
answers whether there was something to remove, so `Contains` need not be called first.

```
string[] queue = {};

queue.Insert("Ada");
queue.Insert("Grace");
queue.InsertAt(0, "Alan");

Console.WriteLine(queue.Join(", "));    # Alan, Ada, Grace
Console.WriteLine(queue.Remove("Ada")); # true
Console.WriteLine(queue.Remove("Ada")); # false — there was only one
```

**A set cannot be changed while a `loop each` is walking it.** Inserting, removing, or clearing
mid-walk raises `SequenceChangedException`, and where the compiler can see it happening it is an
error instead. Collect what to remove and do it afterwards.

## Taking a run

| Member | Yields | What it does |
|---|---|---|
| `Subset(integer start)` | `T[]` | A new set, from `start` to the end |
| `Subset(integer start, integer end)` | `T[]` | A new set, from `start` up to but not including `end` |

The end is exclusive, the same reading `until` has in a loop, so `Subset(0, n)` and
`Subset(n, count)` put the whole set back together.

```
integer[] all = {1, 2, 3, 4, 5};

Console.WriteLine(all.Subset(2).Join(","));      # 3,4,5
Console.WriteLine(all.Subset(1, 3).Join(","));   # 2,3
```

## Two sets read together

All four give back a new set and leave both originals alone.

| Member | Yields | What it does |
|---|---|---|
| `Union(T[] other)` | `T[]` | This set, then the other, end to end |
| `Intersect(T[] other)` | `T[]` | What is in both, in this set's order |
| `Except(T[] other)` | `T[]` | What this has that the other does not |
| `Distinct()` | `T[]` | One of each, keeping the first of every run |

**These are not the operations of the same name in mathematics.** A Compass set keeps order and
allows a value twice, so `Union` appends rather than merging: a value in both appears twice in the
answer. `Distinct` removes the repeats, and only when it is called.

`Intersect` and `Except` partition this set. Every element goes to exactly one of the two, repeats
included. Appending one result to the other does not rebuild the original, because each gathers
its own elements in this set's order and the two runs then sit end to end: `{1, 2, 3}` against
`{3, 4}` gives `3` and `1,2`, which join as `3,1,2`.

<a id="distinct"></a>

```
integer[] mine = {1, 2, 3};
integer[] yours = {3, 4};

Console.WriteLine(mine.Union(yours).Join(","));               # 1,2,3,3,4
Console.WriteLine(mine.Union(yours).Distinct().Join(","));    # 1,2,3,4
Console.WriteLine(mine.Intersect(yours).Join(","));           # 3
Console.WriteLine(mine.Except(yours).Join(","));              # 1,2
```

<a id="joining"></a>

## Joining

| Member | Yields | What it does |
|---|---|---|
| `Join(string separator)` | `string` | Every element written out, with `separator` between |

**Any set answers it, not only a set of strings.** Each element is written out the way it would be
on its own, so joining a set of numbers needs no loop and no conversion first.

```
integer[] scores = {70, 85, 90};
Console.WriteLine(scores.Join(" | "));   # 70 | 85 | 90
```

## Dropping the empties

**Only on a set of [optionals](optionals.md).** A `T[]` that cannot hold an absence has nothing to
trim, so these four members are not offered on one.

| Member | Yields | What it does |
|---|---|---|
| `Trim()` | `T?[]` | Empties off both ends |
| `TrimStart()` | `T?[]` | Empties off the front |
| `TrimEnd()` | `T?[]` | Empties off the end |
| `TrimAll()` | `T[]` | Every empty gone, anywhere |

**`TrimAll` is the one that changes the type.** Removing every empty leaves a set where nothing
can be absent, so it yields `T[]` and nothing in the result needs unwrapping. The other three take
from the ends only, so an empty can remain in the middle and the type stays `T?[]`.

```
integer?[] readings = {}; # ... filled from somewhere that may answer nothing

integer[] certain = readings.TrimAll();

# No unwrapping needed: nothing in 'certain' can be absent.
loop each reading in certain
    Console.WriteLine(reading + 1);
end loop
```

## Sets of sets

`integer[][]` is a set whose elements are sets. It needs no feature of its own: every member above
works on it, with `T` being `integer[]`.

```
integer[][] grid = {{1, 2}, {3, 4}};

Console.WriteLine(grid.Count);          # 2
Console.WriteLine(grid[0].Join(","));     # 1,2
```

## Also on every set

[`ToString()` and `Equals()`](every-value.md), as on every value. `Equals` compares element by
element, so two sets holding equal values in the same order are equal.
