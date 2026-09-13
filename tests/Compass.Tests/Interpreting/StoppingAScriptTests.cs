using System.Diagnostics;
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
/// <para>Stopping a call that will not finish on its own.</para>
/// <para>Only the interpreter can do this: a host cannot interrupt a thread running
/// <c>loop while true</c>, since .NET has no way to abort one and a watchdog's only remedy
/// would be ending the process. So the mechanism is the language's and the decision — how long
/// is too long — is the host's, which is the split <c>maximumDepth</c> already follows.</para>
/// </summary>
[TestFixture]
public sealed class StoppingAScriptTests
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

    private const string Runaway =
        """
        shared model Rules
            integer ticks = 0;

            public function Forever()
                loop
                    Rules.ticks = Rules.ticks + 1;
                until false
            end function

            public function Counting()
                loop for _ = 1 to 100000000
                    Rules.ticks = Rules.ticks + 1;
                end loop
            end function

            public function Recursing()
                Rules.Recursing();
            end function

            public function OnTick()
                Rules.ticks = Rules.ticks + 1;
            end function

            public integer function Ticks()
                yield Rules.ticks;
            end function
        end model
        """;

    /// <summary>A loop with no way out stops when the host says so.</summary>
    [Test]
    public void ALoopThatNeverEndsIsStopped()
    {
        LoadedProgram program = Load(Runaway);

        using CancellationTokenSource stopping = new(TimeSpan.FromMilliseconds(100));

        ScriptStoppedException stopped = Assert.Throws<ScriptStoppedException>(
            () => program.Call("Rules", "Forever", stopping.Token))!;

        Assert.Multiple(() =>
        {
            Assert.That(stopped.File, Is.EqualTo("rules.cm"));
            Assert.That(stopped.Line, Is.GreaterThan(0));
        });
    }

    /// <summary>A counting loop too, which is the other shape a back edge takes.</summary>
    [Test]
    public void ACountingLoopIsStopped()
    {
        LoadedProgram program = Load(Runaway);

        using CancellationTokenSource stopping = new(TimeSpan.FromMilliseconds(100));

        Assert.Throws<ScriptStoppedException>(
            () => program.Call("Rules", "Counting", stopping.Token));
    }

    /// <summary>And recursion, which is stopped at a call's entry rather than a back edge.</summary>
    [Test]
    public void RunawayRecursionIsStoppedBeforeItRunsOutOfDepth()
    {
        LoadedProgram program = Load(Runaway);

        using CancellationTokenSource stopping = new();

        stopping.Cancel();

        Assert.Throws<ScriptStoppedException>(
            () => program.Call("Rules", "Recursing", stopping.Token));
    }

    /// <summary>
    /// A stop is an <see cref="OperationCanceledException"/>, so the ordinary catch a host
    /// already writes around cancellable work takes it.
    /// </summary>
    [Test]
    public void AStopIsAnOrdinaryCancellation()
    {
        LoadedProgram program = Load(Runaway);

        using CancellationTokenSource stopping = new(TimeSpan.FromMilliseconds(100));

        Assert.Throws<ScriptStoppedException>(
            () => program.Call("Rules", "Forever", stopping.Token));

        // The same call again, caught the way a host would write it.
        using CancellationTokenSource again = new(TimeSpan.FromMilliseconds(100));

        bool caught = false;

        try
        {
            program.Call("Rules", "Forever", again.Token);
        }
        catch (OperationCanceledException)
        {
            caught = true;
        }

        Assert.That(caught, Is.True);
    }

    /// <summary>
    /// <b>A script cannot decline to stop.</b> The exception is not a name the language has, so
    /// nothing a program writes can take it — including a clause naming the root.
    /// </summary>
    [Test]
    public void AScriptCannotCatchItsOwnStop()
    {
        LoadedProgram program = Load(
            """
            shared model Rules
                public function Stubborn()
                    loop
                        try
                            Console.WriteLine();
                        catch Exception problem
                            Console.WriteLine(problem.Message());
                        end try
                    until false
                end function
            end model
            """);

        using CancellationTokenSource stopping = new(TimeSpan.FromMilliseconds(100));

        Assert.Throws<ScriptStoppedException>(
            () => program.Call("Rules", "Stubborn", stopping.Token));
    }

    /// <summary>And the program is usable afterwards, as it is after any other failure.</summary>
    [Test]
    public void TheProgramStillWorksAfterAStop()
    {
        LoadedProgram program = Load(Runaway);

        using CancellationTokenSource stopping = new(TimeSpan.FromMilliseconds(100));

        Assert.Throws<ScriptStoppedException>(
            () => program.Call("Rules", "Forever", stopping.Token));

        long before = (long)program.Call("Rules", "Ticks")!;

        program.Call("Rules", "OnTick");

        Assert.That(program.Call("Rules", "Ticks"), Is.EqualTo(before + 1));
    }

    /// <summary>
    /// A call given no token runs to the end, so nothing about this changes what an ordinary
    /// call does. A token from an earlier call must not be inherited by a later one.
    /// </summary>
    [Test]
    public void ACallWithNoTokenIsNotStopped()
    {
        LoadedProgram program = Load(Runaway);

        using CancellationTokenSource stopping = new();

        stopping.Cancel();

        Assert.Throws<ScriptStoppedException>(
            () => program.Call("Rules", "Forever", stopping.Token));

        // The cancelled token is gone: this finishes.
        program.Call("Rules", "OnTick");

        Assert.That(program.Call("Rules", "Ticks"), Is.GreaterThan(0L));
    }

    /// <summary>
    /// Stopping happens promptly rather than eventually. A hundred-millisecond budget that took
    /// seconds to take effect would be a budget in name only.
    /// </summary>
    [Test]
    public void AStopTakesEffectPromptly()
    {
        LoadedProgram program = Load(Runaway);

        using CancellationTokenSource stopping = new(TimeSpan.FromMilliseconds(100));

        Stopwatch clock = Stopwatch.StartNew();

        Assert.Throws<ScriptStoppedException>(
            () => program.Call("Rules", "Forever", stopping.Token));

        clock.Stop();

        Assert.That(
            clock.ElapsedMilliseconds,
            Is.LessThan(5000),
            "a stopped loop should end near its budget, not run on");
    }
}
