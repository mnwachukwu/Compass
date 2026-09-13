using System.Collections.Frozen;
using Compass.Compiler.Ast;
using Compass.Compiler.Lexing;

namespace Compass.Compiler.Semantics;

/// <summary>
/// <para>What actually runs when a program calls a member the host registered.</para>
/// <para><paramref name="receiver"/> is the value to the left of the dot, or null where the
/// member was reached through the type's name. <paramref name="arguments"/> are already
/// evaluated, in the order written.</para>
/// <para>The value handed back is a Compass value: a <c>string</c>, a boxed <c>long</c>,
/// <c>decimal</c> or <c>bool</c>, a set, or an opaque object of a registered type. Null is
/// how an empty optional is written, so a binding whose result is not an optional must not
/// return one.</para>
/// <para>A delegate rather than an interface with a method per member, so that a host may
/// close over whatever it needs and an importer reading an assembly can produce one without
/// declaring a type per entry.</para>
/// </summary>
public delegate object? ExternalBinding(object? receiver, IReadOnlyList<object?> arguments);

/// <summary>
/// <para>The types a host supplies to a compilation, beside the ones the language owns.</para>
/// <para>A host embedding the compiler registers a type here and a program may then name it,
/// hold values of it, and call its members. A program may never declare one, extend one, or
/// construct one unless the catalog offers a constructor.</para>
/// <para><b>Passed in, never static.</b> Two compilations running at once may be given
/// different catalogs, and neither can see the other's — the same reasoning
/// <c>SourceReader</c> follows about where text comes from. <see cref="BuiltIns"/> is static
/// because what the language owns never varies; what a host owns varies per host.</para>
/// <para>The symbols are built once, here, rather than per compilation. That is safe for the
/// same reason it is safe for <see cref="BuiltInTypes.Standard"/>: none of them is given a
/// container, so none is tied to the compilation that read it. Two compilations handed one
/// catalog share its symbols and agree about them; two handed different catalogs share
/// nothing.</para>
/// </summary>
public sealed class ExternalCatalog
{
    /// <summary>A catalog with nothing in it, which is what a compilation has by default.</summary>
    public static ExternalCatalog Empty { get; } = new([]);

    private FrozenDictionary<string, BuiltInModelInfo> _byName;

    private readonly FrozenDictionary<string, ModelSymbol> _symbols;

    /// <summary>
    /// <para>Builds a catalog, refusing anything the language has no way to model.</para>
    /// <para>Refusing at registration is the whole point: a host author meets the boundary
    /// here, with a message naming what they wrote, rather than finding a type that half
    /// works once a script names it.</para>
    /// </summary>
    /// <exception cref="ArgumentException">
    /// A name is not an identifier, is a reserved word, is one the language already owns, or
    /// is registered twice.
    /// </exception>
    public ExternalCatalog(IReadOnlyList<BuiltInModelInfo> models)
        : this(models, (models ?? []).Select(m => m?.Name ?? string.Empty))
    {
    }

    /// <summary>
    /// <para>Builds a catalog whose members may name its own types.</para>
    /// <para>A member's signature often has to name a type the same catalog registers —
    /// <c>World.PlayerNamed</c> yields a <c>Player</c> — and a signature needs the symbol,
    /// which does not exist until the catalog does. So the names are settled first and
    /// <paramref name="describe"/> is handed a catalog that can already answer
    /// <see cref="SymbolFor"/> for every one of them.</para>
    /// <para>The catalog it is handed has no members yet. Reading <see cref="Models"/> from
    /// inside <paramref name="describe"/> answers empty, and is not what it is for.</para>
    /// </summary>
    public static ExternalCatalog Of(
        IReadOnlyList<string> typeNames,
        Func<ExternalCatalog, IReadOnlyList<BuiltInModelInfo>> describe)
    {
        ArgumentNullException.ThrowIfNull(typeNames);
        ArgumentNullException.ThrowIfNull(describe);

        // One catalog, described after its names are settled. Building a second from the
        // description would give it a second set of symbols, and a member yielding a Player
        // from the first would not fit a Player named against the second.
        ExternalCatalog catalog = new([], typeNames);

        catalog.Settle(describe(catalog));

        return catalog;
    }

    /// <summary>Fills in the members, once the names they may mention are known.</summary>
    private void Settle(IReadOnlyList<BuiltInModelInfo> models)
    {
        ArgumentNullException.ThrowIfNull(models);

        Dictionary<string, BuiltInModelInfo> byName = new(StringComparer.Ordinal);

        foreach (BuiltInModelInfo model in models)
        {
            ArgumentNullException.ThrowIfNull(model);
            RefuseMembers(model);

            if (!TypeNames.Contains(model.Name))
            {
                throw new ArgumentException(
                    $"'{model.Name}' was described but not named as one of this catalog's types.",
                    nameof(models));
            }

            if (!byName.TryAdd(model.Name, model))
            {
                throw new ArgumentException(
                    $"'{model.Name}' is described twice.", nameof(models));
            }
        }

        Models = [.. models];
        _byName = byName.ToFrozenDictionary(StringComparer.Ordinal);
    }

    private ExternalCatalog(IReadOnlyList<BuiltInModelInfo> models, IEnumerable<string> names)
    {
        ArgumentNullException.ThrowIfNull(models);

        Dictionary<string, BuiltInModelInfo> byName = new(StringComparer.Ordinal);

        foreach (BuiltInModelInfo model in models)
        {
            ArgumentNullException.ThrowIfNull(model);

            Refuse(model.Name);
            RefuseMembers(model);

            if (!byName.TryAdd(model.Name, model))
            {
                throw new ArgumentException(
                    $"'{model.Name}' is registered twice. A name belongs to one type.",
                    nameof(models));
            }
        }

        Models = [.. models];
        _byName = byName.ToFrozenDictionary(StringComparer.Ordinal);

        // The names rather than the described models, so that the two passes of 'Of' agree
        // about what exists even while the first one has described nothing.
        HashSet<string> declared = [.. names];

        foreach (string name in declared)
        {
            Refuse(name);
        }

        if (byName.Keys.Except(declared).FirstOrDefault() is { } stray)
        {
            throw new ArgumentException(
                $"'{stray}' was described but not named as one of this catalog's types.",
                nameof(models));
        }

        TypeNames = declared.ToFrozenSet(StringComparer.Ordinal);

        _symbols = declared
            .ToFrozenDictionary(
                name => name,
                // Every external type descends from Model, as every declared one does. It is
                // reached from the language's own shared symbol, which has no container either.
                name => new ModelSymbol(name, DeclarationModifiers.Public)
                {
                    BaseType = BuiltInTypes.Of("Model"),
                },
                StringComparer.Ordinal);

        Namespace = new NamespaceSymbol(NamespaceName, parent: null);

        foreach ((string name, ModelSymbol symbol) in _symbols)
        {
            symbol.Container = Namespace;
            Namespace.Types[name] = symbol;
        }
    }

    /// <summary>
    /// <para>The namespace the registered types sit in, which a lookup reaches after
    /// <c>Standard</c> and after everything the program declared.</para>
    /// <para>Parentless, and not part of any compilation's global namespace, so it is reachable
    /// by writing one of its names and not by qualifying. A host does not get to invent a
    /// prefix a program has to learn; phase 5 is where naming an assembly would come in.</para>
    /// </summary>
    public NamespaceSymbol Namespace { get; }

    /// <summary>
    /// What the namespace calls itself. It is never written in a program, and shows up only
    /// where a diagnostic names where a type came from.
    /// </summary>
    private const string NamespaceName = "External";

    /// <summary>Every type the host registered, in the order it registered them.</summary>
    public IReadOnlyList<BuiltInModelInfo> Models { get; private set; }

    /// <summary>The names of those types.</summary>
    public IReadOnlySet<string> TypeNames { get; }

    /// <summary>Whether nothing is registered, which is the ordinary case.</summary>
    public bool IsEmpty => Models.Count == 0;

    /// <summary>The type of that name, or null where the host registered no such name.</summary>
    public BuiltInModelInfo? FindModel(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        return _byName.GetValueOrDefault(name);
    }

    /// <summary>The symbol for a registered type, or null where there is no such type.</summary>
    public ModelSymbol? SymbolFor(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        return _symbols.GetValueOrDefault(name);
    }

    private static void Refuse(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            throw new ArgumentException("A registered type needs a name.", nameof(name));
        }

        if (!IsIdentifier(name))
        {
            throw new ArgumentException(
                $"'{name}' is not a name a program could write. A type's name begins with a "
                + "letter or an underscore and continues with letters, digits, and underscores.",
                nameof(name));
        }

        if (ReservedWords.Keywords.ContainsKey(name))
        {
            throw new ArgumentException(
                $"'{name}' is a reserved word, so no program could name this type.",
                nameof(name));
        }

        if (BuiltIns.IsBuiltInType(name))
        {
            throw new ArgumentException(
                $"'{name}' is a type the language owns. Registering it would give one name two "
                + "meanings, and a program naming it would reach whichever was looked at first.",
                nameof(name));
        }
    }

    private static bool IsIdentifier(string name) =>
        (char.IsLetter(name[0]) || name[0] == '_')
        && name.All(c => char.IsLetterOrDigit(c) || c == '_');

    /// <summary>
    /// <para>Refuses a member the language has no way to offer.</para>
    /// <para>Every value already answers <c>ToString</c> and <c>Equals</c>, so a second answer
    /// is not something the language can model — a host wanting its own text overrides
    /// <c>ToString</c> on the CLR type, which is where the renderer already looks.</para>
    /// </summary>
    private static void RefuseMembers(BuiltInModelInfo model)
    {
        foreach (BuiltInMember member in model.Members.Concat(model.Constructors))
        {
            ArgumentNullException.ThrowIfNull(member);

            if (string.IsNullOrEmpty(member.Name) || !IsIdentifier(member.Name))
            {
                throw new ArgumentException(
                    $"'{model.Name}' registers a member with a name no program could write.",
                    nameof(model));
            }

            if (member.Name is "ToString" or "Equals")
            {
                throw new ArgumentException(
                    $"'{model.Name}.{member.Name}' cannot be registered: every value already "
                    + $"answers {member.Name}. Override it on the CLR type instead.",
                    nameof(model));
            }

            if (member.Binding is null)
            {
                throw new ArgumentException(
                    $"'{model.Name}.{member.Name}' has nothing to run. A registered member "
                    + "carries the binding that performs it.",
                    nameof(model));
            }
        }
    }
}
