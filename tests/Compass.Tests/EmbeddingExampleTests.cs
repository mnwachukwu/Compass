using Compass.Compiler;
using Compass.Compiler.Ast;
using Compass.Compiler.Diagnostics;
using Compass.Compiler.Parsing;
using Compass.Compiler.Semantics;
using Compass.Compiler.Text;
using Compass.Interpreter;

namespace Compass.Tests;

/// <summary>
/// <para>The embedding example from the README, as code that compiles and runs.</para>
/// <para><b>A transcription, not a reading.</b> Nothing here opens the README, so this cannot
/// tell whether the two still say the same thing. What it catches is the other direction, and
/// the likelier one: an embedding API renamed or resignatured without the README following.
/// Change one, look at the other.</para>
/// <para>The specification’s own examples are held the stronger way, by being compiled out
/// of the document. These are C# rather than Compass, so there is nothing to compile them
/// with.</para>
/// </summary>
[TestFixture]
public sealed class EmbeddingExampleTests
{
    private sealed class Person(string name)
    {
        public string Name { get; } = name;
    }

    private static object? Speak(Person who, string what) => who.Name + what;

    private static object? Find(string name) => new Person(name);

    private static string Refuse(string why) => why;

    [Test]
    public void TheEmbeddingExampleCompilesAndRuns()
    {
        const string source =
            """
            shared model Rules
                public function OnTick()
                    Console.WriteLine(World.Named("Ada").Say("!"));
                end function
            end model
            """;

        // ---- Types the host provides ----------------------------------------------------
        ExternalCatalog catalog = ExternalCatalog.Of(["Player", "World"], types =>
        {
            ModelSymbol player = types.SymbolFor("Player")!;

            return
            [
                new BuiltInModelInfo("Player", "Standard", MayBeExtended: false, Members:
                [
                    new BuiltInMember("Say", PrimitiveType.String, [PrimitiveType.String],
                        Binding: (who, arguments) => Speak((Person)who!, (string)arguments[0]!)),
                ]),

                new BuiltInModelInfo("World", "Standard", MayBeExtended: false, HasNoInstances: true,
                    Members:
                    [
                        new BuiltInMember("Named", player, [PrimitiveType.String],
                            Reach: Reached.ThroughTheName,
                            Binding: (_, arguments) => Find((string)arguments[0]!)),
                    ]),
            ];
        });

        // ---- Checking and running -------------------------------------------------------
        DiagnosticBag diagnostics = new();
        CompilationUnit unit = Parser.Parse(new SourceText(source, "rules.cm"), diagnostics);
        SemanticModel model = FrontEnd.Check(unit, diagnostics, externals: catalog);

        Assert.That(diagnostics.HasErrors, Is.False);

        // ---- Bounding a call, and asking what a program reaches -------------------------
        if (model.Reaches != Reaches.Nothing)
        {
            Assert.Fail(Refuse($"this module reaches {model.Reaches}"));
        }

        // ---- A program the host keeps ---------------------------------------------------
        StringWriter printed = new();

        LoadedProgram program = LoadedProgram.Load(
            [.. ClosureConversion.Convert(Lowering.Lower([unit], model), model)],
            model,
            printed);

        using CancellationTokenSource budget = new(TimeSpan.FromMilliseconds(50));

        if (program.Offers("Rules", "OnTick", 0))
        {
            try
            {
                program.Call(
                    "Rules",
                    "OnTick",
                    new CallLimits(budget.Token, MaximumBytes: 4 * 1024 * 1024));
            }
            catch (ScriptStoppedException)
            {
                Assert.Fail("ran too long");
            }
            catch (ScriptAllocatedTooMuchException)
            {
                Assert.Fail("grew too large");
            }
        }

        Assert.That(printed.ToString().Trim(), Is.EqualTo("Ada!"));
    }
}
