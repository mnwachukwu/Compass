using System.Text;
using Compass.Compiler;
using Compass.Compiler.Ast;
using Compass.Compiler.Diagnostics;
using Compass.Compiler.Parsing;
using Compass.Compiler.Semantics;
using Compass.Compiler.Text;
using Compass.Interpreter;

namespace Compass.Tests.Interpreting;

/// <summary>
/// <para>The second way a call can fail to end: it grows rather than runs.</para>
/// <para>A token stops a script that loops forever. It does nothing about one that loops a
/// bounded number of times and puts a megabyte in a set on each turn, which ends the server
/// just as surely and rather sooner.</para>
/// </summary>
[TestFixture]
public sealed class AllocationCeilingTests
{
    private static LoadedProgram Load(string source)
    {
        DiagnosticBag diagnostics = new();
        CompilationUnit unit = Parser.Parse(new SourceText(source, "rules.cm"), diagnostics);

        SemanticModel model = FrontEnd.Check(unit, diagnostics);

        Assert.That(
            diagnostics.Sorted().Select(d => d.Id), Is.Empty, "the program should check");

        return LoadedProgram.Load(
            [.. ClosureConversion.Convert(Lowering.Lower([unit], model), model)],
            model,
            new StringWriter(new StringBuilder()));
    }

    private const string Growing =
        """
        shared model Rules
            integer ticks = 0;

            public function Hoard()
                string[] kept = {};

                loop
                    kept.Insert("this is a run of text that takes up room");
                until false
            end function

            public function Modest()
                string[] kept = {};

                loop for _ = 1 to 10
                    kept.Insert("small");
                end loop

                Rules.ticks = Rules.ticks + kept.Count;
            end function

            public function OnTick()
                Rules.ticks = Rules.ticks + 1;
            end function

            public integer function Ticks()
                yield Rules.ticks;
            end function
        end model
        """;

    [Test]
    public void AScriptThatGrowsWithoutEndIsStopped()
    {
        LoadedProgram program = Load(Growing);

        ScriptAllocatedTooMuchException tooMuch =
            Assert.Throws<ScriptAllocatedTooMuchException>(
                () => program.Call(
                    "Rules", "Hoard", new CallLimits(MaximumBytes: 1024 * 1024)))!;

        Assert.Multiple(() =>
        {
            Assert.That(tooMuch.Limit, Is.EqualTo(1024 * 1024));
            Assert.That(tooMuch.File, Is.EqualTo("rules.cm"));
            Assert.That(tooMuch.Line, Is.GreaterThan(0));
        });
    }

    /// <summary>
    /// And a call that stays inside its ceiling is not disturbed, which is the half that would
    /// make this useless if it went wrong.
    /// </summary>
    [Test]
    public void AModestCallIsLeftAlone()
    {
        LoadedProgram program = Load(Growing);

        program.Call("Rules", "Modest", new CallLimits(MaximumBytes: 1024 * 1024));

        Assert.That(program.Call("Rules", "Ticks"), Is.EqualTo(10L));
    }

    /// <summary>A ceiling of zero is no ceiling, which is what an ordinary call has.</summary>
    [Test]
    public void NoCeilingMeansNoMeasuring()
    {
        LoadedProgram program = Load(Growing);

        program.Call("Rules", "Modest");

        Assert.That(program.Call("Rules", "Ticks"), Is.EqualTo(10L));
    }

    /// <summary>The ceiling is per call, so one call's allocation is not charged to the next.</summary>
    [Test]
    public void EachCallGetsItsOwnCeiling()
    {
        LoadedProgram program = Load(Growing);

        CallLimits limits = new(MaximumBytes: 1024 * 1024);

        // Ten calls that each stay well inside the ceiling. A total across them all would have
        // passed it long before the tenth.
        for (int i = 0; i < 10; i++)
        {
            program.Call("Rules", "Modest", limits);
        }

        Assert.That(program.Call("Rules", "Ticks"), Is.EqualTo(100L));
    }

    /// <summary>A script cannot catch its own ceiling any more than it can catch its stop.</summary>
    [Test]
    public void AScriptCannotCatchItsOwnCeiling()
    {
        LoadedProgram program = Load(
            """
            shared model Rules
                public function Stubborn()
                    string[] kept = {};

                    loop
                        try
                            kept.Insert("this is a run of text that takes up room");
                        catch Exception problem
                            Console.WriteLine(problem.Message());
                        end try
                    until false
                end function
            end model
            """);

        Assert.Throws<ScriptAllocatedTooMuchException>(
            () => program.Call(
                "Rules", "Stubborn", new CallLimits(MaximumBytes: 1024 * 1024)));
    }

    /// <summary>And the program is usable afterwards.</summary>
    [Test]
    public void TheProgramStillWorksAfterACeilingIsReached()
    {
        LoadedProgram program = Load(Growing);

        Assert.Throws<ScriptAllocatedTooMuchException>(
            () => program.Call(
                "Rules", "Hoard", new CallLimits(MaximumBytes: 1024 * 1024)));

        program.Call("Rules", "OnTick");

        Assert.That(program.Call("Rules", "Ticks"), Is.EqualTo(1L));
    }

    /// <summary>Both limits at once, which is how a host would actually set them.</summary>
    [Test]
    public void BothLimitsTogether()
    {
        LoadedProgram program = Load(Growing);

        using CancellationTokenSource stopping = new(TimeSpan.FromSeconds(30));

        // The ceiling is what this one reaches; the token is there as the other guard.
        Assert.Throws<ScriptAllocatedTooMuchException>(
            () => program.Call(
                "Rules",
                "Hoard",
                new CallLimits(stopping.Token, 1024 * 1024)));
    }
}
