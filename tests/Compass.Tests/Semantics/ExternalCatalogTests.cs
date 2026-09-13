using Compass.Compiler;
using Compass.Compiler.Diagnostics;
using Compass.Compiler.Parsing;
using Compass.Compiler.Semantics;
using Compass.Compiler.Text;

namespace Compass.Tests.Semantics;

/// <summary>
/// <para>The seam a host registers types through.</para>
/// <para>The first test is the one that matters most: with nothing registered, a compilation
/// answers exactly as it did before the seam existed. Everything else here is about a catalog
/// that holds something, and none of it may change what an ordinary program sees.</para>
/// </summary>
[TestFixture]
public sealed class ExternalCatalogTests
{
    private static BuiltInModelInfo Player => new(
        "Player",
        "Standard",
        MayBeExtended: false,
        Members: []);

    private static SemanticModel Check(
        string source, DiagnosticBag diagnostics, ExternalCatalog? externals = null)
    {
        SourceText text = new(source, "test.cm");

        return FrontEnd.Check(
            Parser.Parse(text, diagnostics), diagnostics, externals: externals);
    }

    private static IReadOnlyList<string> Ids(DiagnosticBag diagnostics) =>
        [.. diagnostics.Sorted().Select(d => d.Id)];

    [Test]
    public void AnEmptyCatalogIsWhatACompilationHasByDefault()
    {
        DiagnosticBag diagnostics = new();

        SemanticModel model = Check(
            """
            shared model Program
                function Main()
                    Console.WriteLine("hello");
                end function
            end model
            """,
            diagnostics);

        Assert.Multiple(() =>
        {
            Assert.That(Ids(diagnostics), Is.Empty);
            Assert.That(model.Externals.IsEmpty, Is.True);
        });
    }

    /// <summary>
    /// A registered name resolves where a type belongs. Nothing is called on it here — that is
    /// what a later phase adds — but the name has to exist before anything can be.
    /// </summary>
    [Test]
    public void ARegisteredNameIsAType()
    {
        DiagnosticBag diagnostics = new();

        Check(
            """
            shared model Program
                function Main()
                    Player? who;
                    Console.WriteLine(who.HasValue());
                end function
            end model
            """,
            diagnostics,
            new ExternalCatalog([Player]));

        Assert.That(Ids(diagnostics), Is.Empty);
    }

    /// <summary>
    /// The answer Mirage's API shape depends on: an optional of a registered type needs no
    /// machinery of its own, because an optional wraps any type at all.
    /// </summary>
    [Test]
    public void AnOptionalOfARegisteredTypeIsAnOrdinaryOptional()
    {
        DiagnosticBag diagnostics = new();

        SemanticModel model = Check(
            """
            shared model Program
                function Main()
                    Player? who;
                    Console.WriteLine(who.HasValue());
                end function
            end model
            """,
            diagnostics,
            new ExternalCatalog([Player]));

        Assert.That(Ids(diagnostics), Is.Empty);
        Assert.That(model.Externals.SymbolFor("Player")!.IsValueType, Is.False);
    }

    [Test]
    public void AProgramMayNotExtendARegisteredType()
    {
        DiagnosticBag diagnostics = new();

        Check(
            """
            model Hero extends Player
            end model

            shared model Program
                function Main()
                end function
            end model
            """,
            diagnostics,
            new ExternalCatalog([Player]));

        Assert.That(Ids(diagnostics), Does.Contain("CM0123"));
    }

    [Test]
    public void AProgramDeclaringARegisteredNameShadowsItAndIsTold()
    {
        DiagnosticBag diagnostics = new();

        Check(
            """
            model Player
            end model

            shared model Program
                function Main()
                end function
            end model
            """,
            diagnostics,
            new ExternalCatalog([Player]));

        Assert.That(Ids(diagnostics), Does.Contain("CM0203"));
    }

    [Test]
    public void ARegisteredTypeWithNoConstructorCannotBeConstructed()
    {
        DiagnosticBag diagnostics = new();

        Check(
            """
            shared model Program
                function Main()
                    Player who = new Player();
                end function
            end model
            """,
            diagnostics,
            new ExternalCatalog([Player]));

        Assert.That(Ids(diagnostics), Does.Contain("CM0328"));
    }

    /// <summary>
    /// <para>Two compilations given different catalogs cannot see each other's types.</para>
    /// <para>This is what the catalog being an argument rather than a static buys, and a game
    /// server compiling two games at once is the case it is bought for.</para>
    /// </summary>
    [Test]
    public void TwoCatalogsDoNotSeeEachOther()
    {
        DiagnosticBag withPlayer = new();
        DiagnosticBag without = new();

        const string Source =
            """
            shared model Program
                function Main()
                    Player? who;
                    Console.WriteLine(who.HasValue());
                end function
            end model
            """;

        Check(Source, withPlayer, new ExternalCatalog([Player]));
        Check(Source, without, ExternalCatalog.Empty);

        Assert.Multiple(() =>
        {
            Assert.That(Ids(withPlayer), Is.Empty);
            Assert.That(Ids(without), Is.Not.Empty, "Player should be unknown here");
        });
    }

    // ---- What registration refuses --------------------------------------------------------

    [Test]
    public void ANameTheLanguageOwnsIsRefused() => Assert.That(
        () => new ExternalCatalog([Player with { Name = "Console" }]),
        Throws.ArgumentException);

    [Test]
    public void AReservedWordIsRefused() => Assert.That(
        () => new ExternalCatalog([Player with { Name = "model" }]),
        Throws.ArgumentException);

    [Test]
    public void ANameNoProgramCouldWriteIsRefused() => Assert.That(
        () => new ExternalCatalog([Player with { Name = "2Fast" }]),
        Throws.ArgumentException);

    [Test]
    public void OneNameRegisteredTwiceIsRefused() => Assert.That(
        () => new ExternalCatalog([Player, Player]),
        Throws.ArgumentException);
}
