using System.Text;
using Compass.Compiler;
using Compass.Compiler.Ast;
using Compass.Compiler.Diagnostics;
using Compass.Compiler.Parsing;
using Compass.Compiler.Semantics;
using Compass.Compiler.Text;
using Compass.Interpreter;
using Compass.Runtime;

namespace Compass.Tests.Semantics;

/// <summary>
/// <para>Every type the boundary carries, in both directions.</para>
/// <para>The list is the one a host codes against: integer, real, string, boolean, optionals of
/// those, sets of those, and an opaque registered type. A gap here is a gap an engine finds by
/// writing a member that does not work.</para>
/// </summary>
[TestFixture]
public sealed class ExternalMarshallingTests
{
    private sealed class Person(string name)
    {
        public string Name { get; } = name;
    }

    private static SetType SetOf(TypeSymbol element) => new(element);

    private static ExternalCatalog Catalog(List<object?> received) => ExternalCatalog.Of(
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
                        // Read-only values: written with no parentheses, as DateTime.Year is.
                        new BuiltInMember(
                            "Name",
                            PrimitiveType.String,
                            [],
                            IsValue: true,
                            Binding: (receiver, _) => ((Person)receiver!).Name),

                        new BuiltInMember(
                            "Level",
                            PrimitiveType.Integer,
                            [],
                            IsValue: true,
                            Binding: (_, _) => 12L),
                    ]),

                new BuiltInModelInfo(
                    "World",
                    "Standard",
                    MayBeExtended: false,
                    Members:
                    [
                        new BuiltInMember(
                            "Named", player, [PrimitiveType.String],
                            Reach: Reached.ThroughTheName,
                            Binding: (_, arguments) => new Person((string)arguments[0]!)),

                        // Every argument type at once, recorded so the test can read them back.
                        new BuiltInMember(
                            "Take",
                            null,
                            [
                                PrimitiveType.Integer,
                                PrimitiveType.Real,
                                PrimitiveType.String,
                                PrimitiveType.Boolean,
                            ],
                            Reach: Reached.ThroughTheName,
                            Binding: (_, arguments) =>
                            {
                                received.AddRange(arguments);

                                return null;
                            }),

                        // A set going out.
                        new BuiltInMember(
                            "Names",
                            SetOf(PrimitiveType.String),
                            [],
                            Reach: Reached.ThroughTheName,
                            Binding: (_, _) =>
                                new CompassSet<object?>(["Ada", "Grace", "Alan"])),

                        // And a set coming in.
                        new BuiltInMember(
                            "Count",
                            PrimitiveType.Integer,
                            [SetOf(PrimitiveType.Integer)],
                            Reach: Reached.ThroughTheName,
                            Binding: (_, arguments) =>
                                (long)((ICompassSet)arguments[0]!).Count),

                        // An optional primitive out: null is absence, a value is presence.
                        new BuiltInMember(
                            "Setting",
                            new OptionalType(PrimitiveType.Integer),
                            [PrimitiveType.Boolean],
                            Reach: Reached.ThroughTheName,
                            Binding: (_, arguments) => (bool)arguments[0]! ? 99L : null),

                        // And one in, which a binding reads the same way.
                        new BuiltInMember(
                            "Describe",
                            PrimitiveType.String,
                            [new OptionalType(PrimitiveType.String)],
                            Reach: Reached.ThroughTheName,
                            Binding: (_, arguments) =>
                                arguments[0] is string given ? $"got {given}" : "got nothing"),
                    ],
                    HasNoInstances: true),
            ];
        });

    private static (string Printed, IReadOnlyList<string> Ids) Run(
        string body, ExternalCatalog externals)
    {
        string source =
            $$"""
            shared model Program
                function Main()
            {{body}}
                end function
            end model
            """;

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

        Interpreter.Interpreter.Run(
            [.. ClosureConversion.Convert(Lowering.Lower([unit], model), model)],
            model,
            new StringWriter(printed));

        return (printed.ToString(), ids);
    }

    /// <summary>A read-only value on a host type, written with no parentheses.</summary>
    [Test]
    public void AValueMemberIsReadWithoutParentheses()
    {
        (string printed, IReadOnlyList<string> ids) = Run(
            """
                    Player who = World.Named("Ada");

                    Console.WriteLine(who.Name);
                    Console.WriteLine(who.Level);
            """,
            Catalog([]));

        Assert.That(ids, Is.Empty);
        Assert.That(printed.Replace("\r", string.Empty).Trim(), Is.EqualTo("Ada\n12"));
    }

    /// <summary>Writing parentheses on one is reported, as it is for the language's own.</summary>
    [Test]
    public void CallingAValueMemberIsReported()
    {
        (_, IReadOnlyList<string> ids) = Run(
            """
                    Console.WriteLine(World.Named("Ada").Name());
            """,
            Catalog([]));

        Assert.That(ids, Does.Contain("CM0338"));
    }

    /// <summary>Every primitive the contract names, going in.</summary>
    [Test]
    public void EveryPrimitiveCrossesGoingIn()
    {
        List<object?> received = [];

        (_, IReadOnlyList<string> ids) = Run(
            """
                    World.Take(42, 3.5, "text", true);
            """,
            Catalog(received));

        Assert.That(ids, Is.Empty);
        Assert.That(received, Is.EqualTo(new object?[] { 42L, 3.5m, "text", true }));
    }

    /// <summary>A set the host hands out is an ordinary set.</summary>
    [Test]
    public void ASetComesBackAsASet()
    {
        (string printed, IReadOnlyList<string> ids) = Run(
            """
                    string[] names = World.Names();

                    Console.WriteLine(names.Count);
                    Console.WriteLine(names.Join(", "));

                    loop each name in names
                        Console.WriteLine(name);
                    end loop
            """,
            Catalog([]));

        Assert.That(ids, Is.Empty);
        Assert.That(
            printed.Replace("\r", string.Empty).Trim(),
            Is.EqualTo("3\nAda, Grace, Alan\nAda\nGrace\nAlan"));
    }

    /// <summary>And one a program built goes the other way.</summary>
    [Test]
    public void ASetGoesInAsASet()
    {
        (string printed, IReadOnlyList<string> ids) = Run(
            """
                    integer[] scores = {1, 2, 3, 4};

                    Console.WriteLine(World.Count(scores));
            """,
            Catalog([]));

        Assert.That(ids, Is.Empty);
        Assert.That(printed.Trim(), Is.EqualTo("4"));
    }

    /// <summary>
    /// An optional primitive, both ways. Null is absence on the wire, which is the one
    /// marshalling rule a host has to hold to.
    /// </summary>
    [Test]
    public void AnOptionalPrimitiveCrossesBothWays()
    {
        (string printed, IReadOnlyList<string> ids) = Run(
            """
                    integer? there = World.Setting(true);
                    integer? missing = World.Setting(false);

                    Console.WriteLine(there.Or(0));
                    Console.WriteLine(missing.HasValue());

                    string? nothing;

                    Console.WriteLine(World.Describe("a value"));
                    Console.WriteLine(World.Describe(nothing));
            """,
            Catalog([]));

        Assert.That(ids, Is.Empty);
        Assert.That(
            printed.Replace("\r", string.Empty).Trim(),
            Is.EqualTo("99\nfalse\ngot a value\ngot nothing"));
    }

    /// <summary>A set of a registered type, which is the shape a roster takes.</summary>
    [Test]
    public void ASetOfHostValuesHoldsThem()
    {
        (string printed, IReadOnlyList<string> ids) = Run(
            """
                    Player[] everyone = {World.Named("Ada"), World.Named("Grace")};

                    Console.WriteLine(everyone.Count);
                    Console.WriteLine(everyone[1].Name);
            """,
            Catalog([]));

        Assert.That(ids, Is.Empty);
        Assert.That(printed.Replace("\r", string.Empty).Trim(), Is.EqualTo("2\nGrace"));
    }
}
