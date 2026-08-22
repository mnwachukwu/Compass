using Compass.Compiler.Ast;
using Compass.Compiler.Diagnostics;
using Compass.Compiler.Parsing;
using Compass.Compiler.Semantics;
using Compass.Compiler.Text;

namespace Compass.Tests;

/// <summary>
/// <para>That an opinion is reported and changes nothing else.</para>
/// <para>Every other test about a diagnostic asks whether it was reported. This one asks what
/// happens afterwards, which for the newest severity is the whole point of it: a program the
/// language has a view about is still a program, and it runs. Nothing else would notice if an
/// opinion started blocking compilation, because a test that only reads the bag passes either
/// way.</para>
/// </summary>
[TestFixture]
public sealed class DiagnosticSeverityTests : LexerTestBase
{
    /// <summary>
    /// Two opinions and nothing else: `using Standard;` brings what is already there, and the
    /// empty string is what `WriteLine` writes with no argument at all.
    /// </summary>
    private const string OnlyOpinions = """
        using Standard;

        shared model Program
            function Main()
                Console.WriteLine("");
                Console.WriteLine("it ran");
            end function
        end model
        """;

    [Test]
    public void AProgramCarryingOnlyOpinionsCompilesAndRuns()
    {
        DiagnosticBag diagnostics = new();

        CompilationUnit unit = Parser.Parse(new SourceText(OnlyOpinions, "<test>"), diagnostics);
        SemanticModel model = Resolver.Resolve(unit, diagnostics, requireEntryPoint: true);
        TypeChecker.Check(unit, model, diagnostics);
        DefiniteAssignment.Analyze(unit, model, diagnostics);

        StringWriter output = new();

        Assert.Multiple(() =>
        {
            Assert.That(
                diagnostics.Select(d => d.Id),
                Is.EquivalentTo(new[] { "CM0230", "CM0340" }),
                "the program should draw these two opinions and nothing else");

            Assert.That(
                diagnostics.Select(d => d.Severity),
                Has.All.EqualTo(DiagnosticSeverity.Opinion));

            Assert.That(
                diagnostics.HasErrors,
                Is.False,
                "an opinion must not make a compilation fail");
        });

        Compass.Interpreter.Interpreter.Run(Lowering.Lower(unit, model), model, output);

        Assert.That(
            output.ToString().ReplaceLineEndings("\n"),
            Is.EqualTo("\nit ran\n"),
            "the program runs exactly as written; an opinion changes no behavior");
    }
}
