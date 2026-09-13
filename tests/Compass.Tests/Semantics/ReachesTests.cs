using Compass.Compiler;
using Compass.Compiler.Diagnostics;
using Compass.Compiler.Parsing;
using Compass.Compiler.Semantics;
using Compass.Compiler.Text;

namespace Compass.Tests.Semantics;

/// <summary>
/// <para>What the language says a program touches beyond itself.</para>
/// <para>A host embedding the compiler decides what to allow; this is the fact the decision is
/// made from. The point of writing it down in the compiler is that a host's rule cannot go
/// stale — the alternative is an engine listing the file members by hand, which keeps passing
/// while proving nothing the moment the language grows an eighteenth.</para>
/// </summary>
[TestFixture]
public sealed class ReachesTests
{
    /// <summary>
    /// <para>What every model the language provides reaches, written out.</para>
    /// <para><b>This table is the guard.</b> A model added to the catalog and left out of it
    /// fails the test below, so whoever adds one has to say what it touches rather than
    /// inheriting <c>Nothing</c> by default — which is the way a classification like this
    /// usually rots.</para>
    /// </summary>
    private static readonly Dictionary<string, Reaches> Expected = new(StringComparer.Ordinal)
    {
        ["File"] = Reaches.Files,
        ["Directory"] = Reaches.Files,

        // Only the three that read the clock; the rest of each is arithmetic on a value.
        ["DateTime"] = Reaches.Clock,
        ["Date"] = Reaches.Clock,
        ["Time"] = Reaches.Clock,

        // The host supplies both streams when it builds an interpreter, so what reading and
        // writing mean is already its decision rather than the language's.
        ["Console"] = Reaches.Nothing,

        ["Math"] = Reaches.Nothing,
        ["Random"] = Reaches.Nothing,
        ["TimeSpan"] = Reaches.Nothing,
        ["Model"] = Reaches.Nothing,
        ["Function"] = Reaches.Nothing,
        ["Exception"] = Reaches.Nothing,
        ["Reference"] = Reaches.Nothing,
        ["Integer"] = Reaches.Nothing,
        ["Real"] = Reaches.Nothing,
        ["Float"] = Reaches.Nothing,
        ["Fraction"] = Reaches.Nothing,
        ["Boolean"] = Reaches.Nothing,
        ["Character"] = Reaches.Nothing,
        ["String"] = Reaches.Nothing,
    };

    /// <summary>
    /// Every model in the catalog appears above. Adding one without deciding what it reaches
    /// is what this exists to stop.
    /// </summary>
    [Test]
    public void EveryModelSaysWhatItReaches()
    {
        string[] unclassified =
            [.. BuiltIns.Models.Select(m => m.Name)
                              .Where(name => !Expected.ContainsKey(name))
                              .Order(StringComparer.Ordinal)];

        Assert.That(
            unclassified,
            Is.Empty,
            "a model the language provides that nothing has said what it reaches");
    }

    /// <summary>And no member of one reaches further than its model was said to.</summary>
    [Test]
    public void NoMemberReachesFurtherThanItsModel()
    {
        List<string> wrong = [];

        foreach (BuiltInModelInfo model in BuiltIns.Models)
        {
            if (!Expected.TryGetValue(model.Name, out Reaches allowed))
            {
                continue;
            }

            foreach (BuiltInMember member in model.Members.Concat(model.Constructors))
            {
                if ((member.Reaches & ~allowed) != Reaches.Nothing)
                {
                    wrong.Add($"{model.Name}.{member.Name} reaches {member.Reaches}");
                }
            }
        }

        Assert.That(wrong, Is.Empty);
    }

    /// <summary>
    /// And every member of a model that reaches something says so. A <c>File</c> member added
    /// without the classification would otherwise read as touching nothing.
    /// </summary>
    [Test]
    public void EveryMemberOfAReachingModelSaysSo()
    {
        List<string> silent = [];

        foreach (BuiltInModelInfo model in BuiltIns.Models
                     .Where(m => Expected.GetValueOrDefault(m.Name) == Reaches.Files))
        {
            foreach (BuiltInMember member in model.Members)
            {
                if (member.Reaches == Reaches.Nothing)
                {
                    silent.Add($"{model.Name}.{member.Name}");
                }
            }
        }

        Assert.That(silent, Is.Empty, "a filesystem member that does not say it reaches one");
    }

    // ---- What a host asks ----------------------------------------------------------------

    private static Reaches ReachOf(string body)
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

        SemanticModel model = FrontEnd.Check(
            Parser.Parse(new SourceText(source, "test.cm"), diagnostics),
            diagnostics,
            requireEntryPoint: true);

        Assert.That(diagnostics.Sorted().Select(d => d.Id), Is.Empty);

        return model.Reaches;
    }

    [Test]
    public void AnOrdinaryProgramReachesNothing() => Assert.That(
        ReachOf("""
                    integer total = 0;

                    loop for i = 1 to 10
                        total = total + i;
                    end loop

                    Console.WriteLine(total);
        """),
        Is.EqualTo(Reaches.Nothing));

    [Test]
    public void AProgramThatReadsAFileSaysSo() => Assert.That(
        ReachOf("""
                    Console.WriteLine(File.Read("secrets.txt").Or("none"));
        """),
        Is.EqualTo(Reaches.Files));

    [Test]
    public void AProgramThatWalksAFolderSaysSo() => Assert.That(
        ReachOf("""
                    Console.WriteLine(Directory.Files(".").Or({}).Count);
        """),
        Is.EqualTo(Reaches.Files));

    [Test]
    public void AProgramThatReadsTheClockSaysSo() => Assert.That(
        ReachOf("""
                    Console.WriteLine(DateTime.Now.Year);
        """),
        Is.EqualTo(Reaches.Clock));

    /// <summary>Both at once, which is what the flags are for.</summary>
    [Test]
    public void TwoReachesCombine()
    {
        Reaches reached = ReachOf("""
                    File.Write("log.txt", "at " + DateTime.Now.Year);
        """);

        Assert.Multiple(() =>
        {
            Assert.That(reached.HasFlag(Reaches.Files), Is.True);
            Assert.That(reached.HasFlag(Reaches.Clock), Is.True);
        });
    }

    /// <summary>
    /// Arithmetic on a moment is not reading the clock. A program handed a date reaches
    /// nothing; only asking the machine what time it is does.
    /// </summary>
    [Test]
    public void UsingAMomentIsNotReadingTheClock() => Assert.That(
        ReachOf("""
                    DateTime landing = new DateTime(1969, 7, 20, 20, 17, 0);

                    Console.WriteLine(landing.AddDays(1).Year);
        """),
        Is.EqualTo(Reaches.Nothing));
}
