using System.Text;
using Compass.Compiler;
using Compass.Compiler.Ast;
using Compass.Compiler.Diagnostics;
using Compass.Compiler.Parsing;
using Compass.Compiler.Semantics;
using Compass.Compiler.Text;
using Compass.Interpreter;
using Compass.Runtime;

namespace Compass.Tests.Interpreting;

/// <summary>
/// <para>What failing means on either side of the boundary.</para>
/// <para>Three questions: can a script catch what a host binding threw, can a host find out
/// where a script failed, and can a failure on either side leave the interpreter unusable for
/// the next call.</para>
/// </summary>
[TestFixture]
public sealed class ExternalFailureTests
{
    /// <summary>
    /// A <c>World</c> with three ways of failing: one the language names, one it does not, and
    /// one that works, so a later call can prove the interpreter survived.
    /// </summary>
    private static ExternalCatalog Failing() => new(
    [
        new BuiltInModelInfo(
            "World",
            "Standard",
            MayBeExtended: false,
            Members:
            [
                new BuiltInMember(
                    "Refuse",
                    null,
                    [],
                    Reach: Reached.ThroughTheName,
                    Binding: (_, _) => throw new ArgumentException("the host said no")),

                new BuiltInMember(
                    "Break",
                    null,
                    [],
                    Reach: Reached.ThroughTheName,
                    Binding: (_, _) => throw new InvalidTimeZoneException("a bug in the host")),

                new BuiltInMember(
                    "Fine",
                    PrimitiveType.Integer,
                    [],
                    Reach: Reached.ThroughTheName,
                    Binding: (_, _) => 7L),
            ],
            HasNoInstances: true),
    ]);

    private static LoadedProgram Load(string source, StringBuilder printed)
    {
        DiagnosticBag diagnostics = new();
        CompilationUnit unit = Parser.Parse(new SourceText(source, "rules.cm"), diagnostics);

        SemanticModel model = FrontEnd.Check(unit, diagnostics, externals: Failing());

        Assert.That(
            diagnostics.Sorted().Select(d => d.Id), Is.Empty, "the program should check");

        return LoadedProgram.Load(
            [.. ClosureConversion.Convert(Lowering.Lower([unit], model), model)],
            model,
            new StringWriter(printed));
    }

    // ---- A host member that throws -------------------------------------------------------

    /// <summary>
    /// A binding throwing a name the language has is the program's to catch, so a script can
    /// handle a host refusing something.
    /// </summary>
    [Test]
    public void AScriptCatchesWhatTheHostRefused()
    {
        StringBuilder printed = new();

        LoadedProgram program = Load(
            """
            shared model Rules
                public function OnTick()
                    try
                        World.Refuse();
                        Console.WriteLine("not reached");
                    catch ArgumentException problem
                        Console.WriteLine("caught: " + problem.Message());
                    end try
                end function
            end model
            """,
            printed);

        program.Call("Rules", "OnTick");

        Assert.That(printed.ToString().Trim(), Is.EqualTo("caught: the host said no"));
    }

    /// <summary>
    /// And a binding failing in a way the language has no name for is the host's own bug. It
    /// travels past every catch the script wrote, which is the rule <c>catch Exception</c>
    /// already follows about a failure in the implementation.
    /// </summary>
    [Test]
    public void ABugInTheHostIsNotTheScriptsToSwallow()
    {
        StringBuilder printed = new();

        LoadedProgram program = Load(
            """
            shared model Rules
                public function OnTick()
                    try
                        World.Break();
                    catch Exception problem
                        Console.WriteLine("swallowed: " + problem.Message());
                    end try
                end function
            end model
            """,
            printed);

        ExternalFailureException failure =
            Assert.Throws<ExternalFailureException>(() => program.Call("Rules", "OnTick"))!;

        Assert.Multiple(() =>
        {
            Assert.That(failure.MemberName, Is.EqualTo("Break"));
            Assert.That(failure.InnerException, Is.TypeOf<InvalidTimeZoneException>());
            Assert.That(printed.ToString(), Does.Not.Contain("swallowed"));
        });
    }

    // ---- A script that fails, reaching the host ------------------------------------------

    /// <summary>
    /// §Phase 4: a host reading a log has no editor open, so the position travels with the
    /// failure.
    /// </summary>
    [Test]
    public void AFailedScriptSaysWhereItFailed()
    {
        StringBuilder printed = new();

        LoadedProgram program = Load(
            """
            shared model Rules
                public function OnTick()
                    Rules.Deeper();
                end function

                function Deeper()
                    integer zero = 0;
                    Console.WriteLine(10 / zero);
                end function
            end model
            """,
            printed);

        ScriptFailedException failed =
            Assert.Throws<ScriptFailedException>(() => program.Call("Rules", "OnTick"))!;

        Assert.Multiple(() =>
        {
            Assert.That(failed.TypeName, Is.EqualTo("DivideByZeroException"));
            Assert.That(failed.File, Is.EqualTo("rules.cm"));

            // The line inside Deeper, not the line in OnTick that called it.
            Assert.That(failed.Line, Is.EqualTo(8));
            Assert.That(failed.Message, Does.Contain("rules.cm(8,"));
        });
    }

    /// <summary>An exception the program declared arrives with its own name, not the carrier's.</summary>
    [Test]
    public void AnExceptionTheScriptDeclaredKeepsItsName()
    {
        StringBuilder printed = new();

        LoadedProgram program = Load(
            """
            model NotAllowed extends Exception
                public function NotAllowed(string why)
                    base(why);
                end function
            end model

            shared model Rules
                public function OnTick()
                    throw new NotAllowed("the rule says no");
                end function
            end model
            """,
            printed);

        ScriptFailedException failed =
            Assert.Throws<ScriptFailedException>(() => program.Call("Rules", "OnTick"))!;

        Assert.Multiple(() =>
        {
            Assert.That(failed.TypeName, Is.EqualTo("NotAllowed"));
            Assert.That(failed.Text, Is.EqualTo("the rule says no"));
            Assert.That(failed.Line, Is.GreaterThan(0));
        });
    }

    // ---- Nothing is left behind ----------------------------------------------------------

    /// <summary>
    /// §Phase 4: a host member must not be able to tear the interpreter into a state the next
    /// call inherits. Both kinds of failure, then an ordinary call that has to work.
    /// </summary>
    [Test]
    public void TheInterpreterSurvivesAFailureOnEitherSide()
    {
        StringBuilder printed = new();

        LoadedProgram program = Load(
            """
            shared model Rules
                integer ticks = 0;

                public function Refused()
                    World.Refuse();
                end function

                public function Broken()
                    World.Break();
                end function

                public function Failed()
                    integer zero = 0;
                    Console.WriteLine(10 / zero);
                end function

                public function OnTick()
                    Rules.ticks = Rules.ticks + 1;
                end function

                public integer function Ticks()
                    yield Rules.ticks;
                end function
            end model
            """,
            printed);

        Assert.Throws<ScriptFailedException>(() => program.Call("Rules", "Refused"));
        Assert.Throws<ExternalFailureException>(() => program.Call("Rules", "Broken"));
        Assert.Throws<ScriptFailedException>(() => program.Call("Rules", "Failed"));

        // Three failures in, the program still runs and its state is the state it had.
        program.Call("Rules", "OnTick");
        program.Call("Rules", "OnTick");

        Assert.That(program.Call("Rules", "Ticks"), Is.EqualTo(2L));
    }

    /// <summary>
    /// A host failure that a script does not catch still leaves the binding's own values alone,
    /// so a later call reaching the same member works.
    /// </summary>
    [Test]
    public void AWorkingMemberStillWorksAfterOneFailed()
    {
        StringBuilder printed = new();

        LoadedProgram program = Load(
            """
            shared model Rules
                public function Broken()
                    World.Break();
                end function

                public integer function Good()
                    yield World.Fine();
                end function
            end model
            """,
            printed);

        Assert.Throws<ExternalFailureException>(() => program.Call("Rules", "Broken"));

        Assert.That(program.Call("Rules", "Good"), Is.EqualTo(7L));
    }
}
