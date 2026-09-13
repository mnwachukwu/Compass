using System.Text;
using Compass.Compiler;
using Compass.Compiler.Ast;
using Compass.Compiler.Diagnostics;
using Compass.Compiler.Emit;
using Compass.Compiler.Parsing;
using Compass.Compiler.Semantics;
using Compass.Compiler.Text;

namespace Compass.Tests.Semantics;

/// <summary>
/// <para>A shared model the host registered, whose members run host code.</para>
/// <para>These run the program rather than only checking it, because the point of a member
/// with a binding is what happens when it is called.</para>
/// </summary>
[TestFixture]
public sealed class ExternalMemberTests
{
    private static TypeSymbol Text => PrimitiveType.String;

    private static TypeSymbol Whole => PrimitiveType.Integer;

    /// <summary>
    /// A <c>World</c> with two members: one taking a string and yielding nothing, and one
    /// taking a whole number and yielding one back.
    /// </summary>
    private static (ExternalCatalog Catalog, List<string> Said) WorldSaying()
    {
        List<string> said = [];

        BuiltInModelInfo world = new(
            "World",
            "Standard",
            MayBeExtended: false,
            Members:
            [
                new BuiltInMember(
                    "SayTo",
                    null,
                    [Text],
                    Reach: Reached.ThroughTheName,
                    Binding: (_, arguments) =>
                    {
                        said.Add((string)arguments[0]!);

                        return null;
                    }),
                new BuiltInMember(
                    "Doubled",
                    Whole,
                    [Whole],
                    Reach: Reached.ThroughTheName,
                    Binding: (_, arguments) => (long)arguments[0]! * 2),
            ],
            HasNoInstances: true);

        return (new ExternalCatalog([world]), said);
    }

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
    public void AMemberOnARegisteredNameRunsTheHostsCode()
    {
        (ExternalCatalog catalog, List<string> said) = WorldSaying();

        (_, IReadOnlyList<string> ids) = Run(
            """
            shared model Program
                function Main()
                    World.SayTo("hello from a script");
                end function
            end model
            """,
            catalog);

        Assert.That(ids, Is.Empty);
        Assert.That(said, Is.EqualTo(new[] { "hello from a script" }));
    }

    [Test]
    public void WhatTheHostYieldsIsAnOrdinaryValue()
    {
        (ExternalCatalog catalog, _) = WorldSaying();

        (string printed, IReadOnlyList<string> ids) = Run(
            """
            shared model Program
                function Main()
                    Console.WriteLine(World.Doubled(21) + 0);
                end function
            end model
            """,
            catalog);

        Assert.That(ids, Is.Empty);
        Assert.That(printed.Trim(), Is.EqualTo("42"));
    }

    [Test]
    public void TheSignatureIsChecked()
    {
        (ExternalCatalog catalog, _) = WorldSaying();

        (_, IReadOnlyList<string> ids) = Run(
            """
            shared model Program
                function Main()
                    World.SayTo(5);
                end function
            end model
            """,
            catalog);

        Assert.That(ids, Is.Not.Empty, "a whole number is not a string");
    }

    [Test]
    public void AMemberTheHostDidNotRegisterIsNotThere()
    {
        (ExternalCatalog catalog, _) = WorldSaying();

        (_, IReadOnlyList<string> ids) = Run(
            """
            shared model Program
                function Main()
                    World.Explode();
                end function
            end model
            """,
            catalog);

        Assert.That(ids, Is.Not.Empty);
    }

    /// <summary>
    /// The sandbox property §4.3 asks to be stated somewhere durable: a program reaches what
    /// the host registered and nothing else, so one catalog's members are invisible to a
    /// compilation given another.
    /// </summary>
    [Test]
    public void NothingReachesAMemberFromAnotherCatalog()
    {
        (ExternalCatalog catalog, _) = WorldSaying();

        (_, IReadOnlyList<string> withIt) = Run(
            """
            shared model Program
                function Main()
                    World.SayTo("x");
                end function
            end model
            """,
            catalog);

        (_, IReadOnlyList<string> without) = Run(
            """
            shared model Program
                function Main()
                    World.SayTo("x");
                end function
            end model
            """,
            ExternalCatalog.Empty);

        Assert.Multiple(() =>
        {
            Assert.That(withIt, Is.Empty);
            Assert.That(without, Is.Not.Empty);
        });
    }

    /// <summary>
    /// §4.2: a program using host members must be refused by the back end rather than emitted
    /// and left to fail where nobody can read why.
    /// </summary>
    [Test]
    public void TheBackEndRefusesAProgramThatCallsOut()
    {
        (ExternalCatalog catalog, _) = WorldSaying();

        DiagnosticBag diagnostics = new();

        CompilationUnit unit = Parser.Parse(
            new SourceText(
                """
                shared model Program
                    function Main()
                        World.SayTo("hello");
                    end function
                end model
                """,
                "test.cm"),
            diagnostics);

        SemanticModel model = FrontEnd.Check(
            unit, diagnostics, requireEntryPoint: true, externals: catalog);

        Assert.That(diagnostics.HasErrors, Is.False, "it checks");

        DiagnosticBag building = new();

        Assert.That(ExternalMembers.Refuse(model, building), Is.True);
        Assert.That(
            building.Sorted().Select(d => d.Id), Does.Contain("CM0124"));
    }
}
