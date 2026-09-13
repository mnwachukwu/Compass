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
/// <para>A program a host keeps and calls into, rather than one it runs and discards.</para>
/// <para>The thing being proved is that state written by one call is there for the next, since
/// that is what makes an event-driven script possible at all.</para>
/// </summary>
[TestFixture]
public sealed class LoadedProgramTests
{
    private static LoadedProgram Load(
        string source, StringBuilder printed, int? maximumDepth = null)
    {
        DiagnosticBag diagnostics = new();
        CompilationUnit unit = Parser.Parse(new SourceText(source, "test.cm"), diagnostics);

        SemanticModel model = FrontEnd.Check(unit, diagnostics);

        Assert.That(
            diagnostics.Sorted().Select(d => d.Id), Is.Empty, "the program should check");

        return LoadedProgram.Load(
            [.. ClosureConversion.Convert(Lowering.Lower([unit], model), model)],
            model,
            new StringWriter(printed),
            maximumDepth: maximumDepth);
    }

    /// <summary>A program with no <c>Main</c> at all, which is what a game module is.</summary>
    private const string Counting =
        """
        shared model Rules
            integer seen = 0;

            public function OnTick()
                Rules.seen = Rules.seen + 1;
            end function

            public integer function Seen()
                yield Rules.seen;
            end function

            public function Greet(string who)
                Console.WriteLine("hello, " + who);
            end function
        end model
        """;

    [Test]
    public void StateSurvivesFromOneCallToTheNext()
    {
        StringBuilder printed = new();
        LoadedProgram program = Load(Counting, printed);

        program.Call("Rules", "OnTick");
        program.Call("Rules", "OnTick");
        program.Call("Rules", "OnTick");

        Assert.That(program.Call("Rules", "Seen"), Is.EqualTo(3L));
    }

    [Test]
    public void ArgumentsGoInAndValuesComeOut()
    {
        StringBuilder printed = new();
        LoadedProgram program = Load(Counting, printed);

        program.Call("Rules", "Greet", "Ada");

        Assert.That(printed.ToString().Trim(), Is.EqualTo("hello, Ada"));
    }

    /// <summary>
    /// A module need not declare an entry point. Nothing a host calls is <c>Main</c>, so
    /// requiring one would be requiring a function nobody runs.
    /// </summary>
    [Test]
    public void AProgramWithNoEntryPointLoadsAndAnswers()
    {
        StringBuilder printed = new();

        Assert.That(Load(Counting, printed).Offers("Rules", "OnTick", 0), Is.True);
    }

    [Test]
    public void AskingWhatIsThereDoesNotThrow()
    {
        StringBuilder printed = new();
        LoadedProgram program = Load(Counting, printed);

        Assert.Multiple(() =>
        {
            Assert.That(program.Offers("Rules", "OnTick", 0), Is.True);
            Assert.That(program.Offers("Rules", "OnTick", 1), Is.False, "arity is part of it");
            Assert.That(program.Offers("Rules", "OnPlayerMoved", 3), Is.False);
            Assert.That(program.Offers("Nothing", "OnTick", 0), Is.False);
        });
    }

    [Test]
    public void CallingWhatIsNotThereSaysSo()
    {
        StringBuilder printed = new();
        LoadedProgram program = Load(Counting, printed);

        Assert.That(
            () => program.Call("Rules", "OnPlayerMoved", 1L, 2L),
            Throws.TypeOf<MissingFunctionException>());
    }

    /// <summary>
    /// A shared initializer runs once, at load, rather than on every call. Two calls seeing
    /// the counter reset would mean it had run twice.
    /// </summary>
    [Test]
    public void SharedStateIsInitializedOnce()
    {
        StringBuilder printed = new();
        LoadedProgram program = Load(Counting, printed);

        program.Call("Rules", "OnTick");

        Assert.That(program.Call("Rules", "Seen"), Is.EqualTo(1L));

        program.Call("Rules", "OnTick");

        Assert.That(program.Call("Rules", "Seen"), Is.EqualTo(2L));
    }

    /// <summary>
    /// What §6a asked for: a host bounds a runaway script at a depth it chose, and gets an
    /// exception it can catch rather than a process that ends.
    /// </summary>
    [Test]
    public void AHostMayBoundHowDeepAScriptGoes()
    {
        StringBuilder printed = new();

        LoadedProgram program = Load(
            """
            shared model Rules
                public function Forever()
                    Rules.Forever();
                end function
            end model
            """,
            printed,
            maximumDepth: 32);

        // Wrapped, so that the line it gave up on travels with it. The original is the inner
        // exception, which is what a host matches on to treat a runaway apart from other faults.
        Assert.That(
            () => program.Call("Rules", "Forever"),
            Throws.TypeOf<ScriptFailedException>()
                .With.InnerException.TypeOf<RecursionTooDeepException>());
    }

    /// <summary>
    /// And the interpreter is usable afterwards: the depth unwinds with the stack, so a script
    /// that ran away once does not leave every later call starting part-way down.
    /// </summary>
    [Test]
    public void ARunawayCallLeavesNothingBehind()
    {
        StringBuilder printed = new();

        LoadedProgram program = Load(
            """
            shared model Rules
                integer seen = 0;

                public function Forever()
                    Rules.Forever();
                end function

                public function OnTick()
                    Rules.seen = Rules.seen + 1;
                end function

                public integer function Seen()
                    yield Rules.seen;
                end function
            end model
            """,
            printed,
            maximumDepth: 32);

        // Wrapped, so that the line it gave up on travels with it. The original is the inner
        // exception, which is what a host matches on to treat a runaway apart from other faults.
        Assert.That(
            () => program.Call("Rules", "Forever"),
            Throws.TypeOf<ScriptFailedException>()
                .With.InnerException.TypeOf<RecursionTooDeepException>());

        program.Call("Rules", "OnTick");

        Assert.That(program.Call("Rules", "Seen"), Is.EqualTo(1L));
    }

    /// <summary>An instance method is not something a host can address, and says nothing.</summary>
    [Test]
    public void OnlyASharedModelsFunctionsAreOffered()
    {
        StringBuilder printed = new();

        LoadedProgram program = Load(
            """
            model Counter
                public integer count;

                public function Counter()
                    this.count = 0;
                end function

                public function Bump()
                    this.count = this.count + 1;
                end function
            end model

            shared model Rules
                public function OnTick()
                end function
            end model
            """,
            printed);

        Assert.Multiple(() =>
        {
            Assert.That(program.Offers("Counter", "Bump", 0), Is.False);
            Assert.That(program.Offers("Rules", "OnTick", 0), Is.True);
        });
    }
}
