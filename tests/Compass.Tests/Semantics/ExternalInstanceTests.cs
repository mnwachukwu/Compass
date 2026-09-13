using System.Text;
using Compass.Compiler;
using Compass.Compiler.Ast;
using Compass.Compiler.Diagnostics;
using Compass.Compiler.Parsing;
using Compass.Compiler.Semantics;
using Compass.Compiler.Text;

namespace Compass.Tests.Semantics;

/// <summary>
/// <para>A type the host owns, whose values a program holds and calls members on.</para>
/// <para>The value is an opaque CLR object. Nothing in the language knows what is inside one,
/// which is the whole arrangement: the host hands one out, the program carries it around and
/// hands it back.</para>
/// </summary>
[TestFixture]
public sealed class ExternalInstanceTests
{
    /// <summary>What a host hands out. The language never looks inside this.</summary>
    private sealed class Person(string name)
    {
        public string Name { get; } = name;

        public override string ToString() => $"Person({Name})";
    }

    /// <summary>
    /// A <c>Player</c> with an instance member, and a <c>World</c> that hands one out. The
    /// two-pass build is what lets <c>World.Named</c> say it yields a <c>Player</c>.
    /// </summary>
    private static ExternalCatalog Catalog(List<string> said) => ExternalCatalog.Of(
        ["Player", "World"],
        catalog =>
        {
            ModelSymbol player = catalog.SymbolFor("Player")!;

            return
            [
                new BuiltInModelInfo(
                    "Player",
                    "Standard",
                    MayBeExtended: false,
                    Members:
                    [
                        // Reached through a value, which is the default.
                        new BuiltInMember(
                            "Say",
                            null,
                            [PrimitiveType.String],
                            Binding: (receiver, arguments) =>
                            {
                                said.Add($"{((Person)receiver!).Name}: {arguments[0]}");

                                return null;
                            }),
                        new BuiltInMember(
                            "Name",
                            PrimitiveType.String,
                            [],
                            Binding: (receiver, _) => ((Person)receiver!).Name),
                    ]),

                new BuiltInModelInfo(
                    "World",
                    "Standard",
                    MayBeExtended: false,
                    Members:
                    [
                        new BuiltInMember(
                            "Named",
                            player,
                            [PrimitiveType.String],
                            Reach: Reached.ThroughTheName,
                            Binding: (_, arguments) => new Person((string)arguments[0]!)),

                        // Yields Player?, which the language wraps with no machinery of its own.
                        new BuiltInMember(
                            "Missing",
                            new OptionalType(player),
                            [],
                            Reach: Reached.ThroughTheName,
                            Binding: (_, _) => null),
                    ],
                    HasNoInstances: true),
            ];
        });

    private static (string Printed, IReadOnlyList<string> Ids) Run(
        string source, ExternalCatalog externals)
    {
        DiagnosticBag diagnostics = new();
        CompilationUnit unit = Parser.Parse(new SourceText(source, "test.cm"), diagnostics);

        SemanticModel model = FrontEnd.Check(
            unit, diagnostics, requireEntryPoint: true, externals: externals);

        IReadOnlyList<string> ids = [.. diagnostics.Sorted().Select(d => d.Id)];

        if (diagnostics.HasErrors)
        {
            return (string.Empty, ids);
        }

        StringBuilder printed = new();

        Compass.Interpreter.Interpreter.Run(
            [.. ClosureConversion.Convert(Lowering.Lower([unit], model), model)],
            model,
            new StringWriter(printed));

        return (printed.ToString(), ids);
    }

    [Test]
    public void AValueTheHostHandedOutAnswersItsMembers()
    {
        List<string> said = [];

        (_, IReadOnlyList<string> ids) = Run(
            """
            shared model Program
                function Main()
                    Player who = World.Named("Ada");
                    who.Say("hello");
                end function
            end model
            """,
            Catalog(said));

        Assert.That(ids, Is.Empty);
        Assert.That(said, Is.EqualTo(new[] { "Ada: hello" }));
    }

    [Test]
    public void AValueMemberYieldsAnOrdinaryString()
    {
        (string printed, IReadOnlyList<string> ids) = Run(
            """
            shared model Program
                function Main()
                    Console.WriteLine(World.Named("Grace").Name());
                end function
            end model
            """,
            Catalog([]));

        Assert.That(ids, Is.Empty);
        Assert.That(printed.Trim(), Is.EqualTo("Grace"));
    }

    /// <summary>The value travels through a program's own function and comes back whole.</summary>
    [Test]
    public void AHostValuePassesThroughAProgramsOwnCode()
    {
        List<string> said = [];

        (_, IReadOnlyList<string> ids) = Run(
            """
            shared model Program
                function Main()
                    Program.Greet(World.Named("Alan"));
                end function

                function Greet(Player who)
                    who.Say("passed through");
                end function
            end model
            """,
            Catalog(said));

        Assert.That(ids, Is.Empty);
        Assert.That(said, Is.EqualTo(new[] { "Alan: passed through" }));
    }

    /// <summary>
    /// Equality is identity. Two values the host handed out separately are two players, even
    /// where everything about them matches.
    /// </summary>
    [Test]
    public void TwoValuesAreEqualOnlyWhenTheyAreTheSameOne()
    {
        (string printed, IReadOnlyList<string> ids) = Run(
            """
            shared model Program
                function Main()
                    Player one = World.Named("Ada");
                    Player again = one;
                    Player other = World.Named("Ada");

                    Console.WriteLine(one == again);
                    Console.WriteLine(one == other);
                end function
            end model
            """,
            Catalog([]));

        Assert.That(ids, Is.Empty);
        Assert.That(printed.Replace("\r", string.Empty).Trim(), Is.EqualTo("true\nfalse"));
    }

    /// <summary>An absent host value is an ordinary empty optional, proven at run time.</summary>
    [Test]
    public void AnOptionalOfAHostTypeBehavesLikeAnyOther()
    {
        (string printed, IReadOnlyList<string> ids) = Run(
            """
            shared model Program
                function Main()
                    Player? nobody = World.Missing();

                    Console.WriteLine(nobody.HasValue());

                    if nobody.HasValue()
                        Console.WriteLine("someone");
                    else
                        Console.WriteLine("no one");
                    end if
                end function
            end model
            """,
            Catalog([]));

        Assert.That(ids, Is.Empty);
        Assert.That(printed.Replace("\r", string.Empty).Trim(), Is.EqualTo("false\nno one"));
    }

    /// <summary>Printing one asks the host's own type, which is where a host controls it.</summary>
    [Test]
    public void PrintingOneAsksTheHostsType()
    {
        (string printed, IReadOnlyList<string> ids) = Run(
            """
            shared model Program
                function Main()
                    Console.WriteLine(World.Named("Ada"));
                end function
            end model
            """,
            Catalog([]));

        Assert.That(ids, Is.Empty);
        Assert.That(printed.Trim(), Is.EqualTo("Person(Ada)"));
    }

    [Test]
    public void AMemberTheHostDidNotPutOnTheTypeIsNotThere()
    {
        (_, IReadOnlyList<string> ids) = Run(
            """
            shared model Program
                function Main()
                    World.Named("Ada").Fly();
                end function
            end model
            """,
            Catalog([]));

        Assert.That(ids, Is.Not.Empty);
    }

    [Test]
    public void RegisteringToStringIsRefused() => Assert.That(
        () => new ExternalCatalog(
        [
            new BuiltInModelInfo(
                "Player",
                "Standard",
                MayBeExtended: false,
                Members:
                [
                    new BuiltInMember("ToString", PrimitiveType.String, [], Binding: (_, _) => ""),
                ]),
        ]),
        Throws.ArgumentException);

    [Test]
    public void AMemberWithNothingToRunIsRefused() => Assert.That(
        () => new ExternalCatalog(
        [
            new BuiltInModelInfo(
                "Player",
                "Standard",
                MayBeExtended: false,
                Members: [new BuiltInMember("Say", null, [])]),
        ]),
        Throws.ArgumentException);
}
