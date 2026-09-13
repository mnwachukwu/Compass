using Compass.Compiler.Ast;
using Compass.Compiler.Text;

namespace Compass.Compiler.Semantics;

/// <summary>
/// <para>What the resolver worked out about a syntax tree.</para>
/// <para>Held beside the tree rather than on it. Keeping the tree immutable means it can be
/// shared, reparsed independently, and cached — which is what an editor needs — and it costs
/// only a lookup where a field would have been a dereference.</para>
/// </summary>
public sealed class SemanticModel
{
    private readonly Dictionary<SyntaxNode, Symbol> _symbols = [];
    private readonly Dictionary<SyntaxNode, TypeSymbol> _types = [];
    private readonly Dictionary<
        SyntaxNode,
        IReadOnlyList<(ConversionOperation Operation, TypeSymbol Target)>> _conversions = [];

    private readonly Dictionary<SyntaxNode, BuiltInId> _builtIns = [];

    private readonly Dictionary<SyntaxNode, bool> _settledTests = [];

    /// <summary>
    /// <para>Which names were in force over which stretch of which file.</para>
    /// <para>Kept by span rather than by node because scopes and nodes do not line up: one
    /// <c>if</c> opens two of them, and neither is the statement. Held per file because a span
    /// carries an offset and no notion of where it came from.</para>
    /// </summary>
    private readonly Dictionary<SourceText, List<(SourceSpan Covering, NameScope Names)>> _scopes =
        [];

    /// <summary>The global namespace, holding everything declared at the top level.</summary>
    public NamespaceSymbol GlobalNamespace { get; } = new(string.Empty, parent: null);

    /// <summary>The entry point, once one has been found.</summary>
    public FunctionSymbol? EntryPoint { get; internal set; }

    /// <summary>
    /// <para>What a host registered for this compilation, or an empty catalog.</para>
    /// <para>Carried here so that every pass after the resolver reads the same one. A
    /// compilation is checked against the catalog it was given, and two compilations given
    /// different catalogs cannot see each other's types.</para>
    /// </summary>
    public ExternalCatalog Externals { get; internal set; } = ExternalCatalog.Empty;

    /// <summary>Records what a node refers to.</summary>
    internal void Bind(SyntaxNode node, Symbol symbol) => _symbols[node] = symbol;

    /// <summary>Records the names in force over a stretch of a file.</summary>
    internal void Opened(SourceText file, SourceSpan covering, NameScope names)
    {
        if (!_scopes.TryGetValue(file, out List<(SourceSpan, NameScope)>? opened))
        {
            _scopes[file] = opened = [];
        }

        opened.Add((covering, names));
    }

    /// <summary>
    /// <para>The names in force at a place in a file, or null where nothing was recorded — a
    /// point outside every function body, which is nowhere a bare name can be written.</para>
    /// <para>The narrowest stretch covering the offset, since scopes nest and the innermost is
    /// the one whose names shadow the rest. Scanned rather than indexed: this is asked once per
    /// question about one cursor, and a file has as many of these as it has blocks.</para>
    /// </summary>
    public NameScope? NamesAt(SourceText file, int offset)
    {
        if (file is null || !_scopes.TryGetValue(file, out List<(SourceSpan Covering, NameScope Names)>? opened))
        {
            return null;
        }

        NameScope? narrowest = null;
        int width = int.MaxValue;

        foreach ((SourceSpan covering, NameScope names) in opened)
        {
            if (offset < covering.Start.Offset || offset > covering.EndOffset)
            {
                continue;
            }

            if (covering.Length < width)
            {
                width = covering.Length;
                narrowest = names;
            }
        }

        return narrowest;
    }

    /// <summary>Records the type a node denotes.</summary>
    internal void BindType(SyntaxNode node, TypeSymbol type) => _types[node] = type;

    /// <summary>What a node refers to, or null if nothing was resolved for it.</summary>
    public Symbol? GetSymbol(SyntaxNode node) =>
        _symbols.TryGetValue(node, out Symbol? symbol) ? symbol : null;

    /// <summary>The type a node denotes, or null if none was recorded.</summary>
    public TypeSymbol? GetType(SyntaxNode node) =>
        _types.TryGetValue(node, out TypeSymbol? type) ? type : null;

    /// <summary>
    /// <para>Records which member the language provides a name resolved to.</para>
    /// <para>The type checker is the only pass that can decide this: it knows the receiver's
    /// type, what narrowing has proved about it, and whether the receiver's own type declares
    /// a member of the same name. Writing the answer down means the back end carries it out
    /// rather than deciding a second time from the value in hand, which is a different
    /// question with a different answer.</para>
    /// </summary>
    internal void BindBuiltIn(SyntaxNode node, BuiltInId id) => _builtIns[node] = id;

    /// <summary>
    /// The member the language provides that a name resolved to, or null when the name
    /// resolved to something a program declared.
    /// </summary>
    public BuiltInId? GetBuiltIn(SyntaxNode node) =>
        _builtIns.TryGetValue(node, out BuiltInId id) ? id : null;

    private readonly Dictionary<SyntaxNode, BuiltInMember> _externalMembers = [];

    /// <summary>
    /// Records that a name resolved to a member the host registered. The member itself rather
    /// than an id, since the binding it carries is what tells one from another.
    /// </summary>
    internal void BindExternal(SyntaxNode node, BuiltInMember member) =>
        _externalMembers[node] = member;

    /// <summary>
    /// The member the host registered that a name resolved to, or null where it resolved to
    /// something the language or the program provides.
    /// </summary>
    public BuiltInMember? GetExternal(SyntaxNode node) =>
        _externalMembers.GetValueOrDefault(node);

    /// <summary>
    /// <para>Every place a program named a member the host registered.</para>
    /// <para>Read by the back end, which cannot emit a call to a delegate that exists only in
    /// the compiling process. A program using one runs and does not build.</para>
    /// </summary>
    public IReadOnlyCollection<SyntaxNode> ExternalMemberUses => _externalMembers.Keys;

    /// <summary>
    /// <para>What this program touches beyond itself, taken from the members it names.</para>
    /// <para>For a host deciding whether to run somebody else's script. The language cannot
    /// deny one of these while a program runs — a built-in dispatches straight from its id,
    /// with nothing in between — so a host that cares refuses the program before it loads.
    /// Answering here rather than leaving a host to recognise the members itself is what stops
    /// that check going stale: a member added to the language is classified with it, and a
    /// host's rule is derived rather than transcribed.</para>
    /// <para>Answerable as soon as the front end has run. What a host registered is not
    /// counted — a host knows what its own bindings reach.</para>
    /// </summary>
    public Reaches Reaches
    {
        get
        {
            Reaches reached = Semantics.Reaches.Nothing;

            foreach (BuiltInId id in _builtIns.Values)
            {
                reached |= BuiltIns.ReachOf(id);
            }

            return reached;
        }
    }

    /// <summary>
    /// <para>Records that a type test's answer follows from the types alone.</para>
    /// <para>Some tests cannot be answered by looking at the value: a set does not carry its
    /// element type and a function does not carry its signature. They do not need to be, since
    /// the declared types settle them — but only this pass knows that, so it writes the answer
    /// down rather than leaving the back end to work out something it cannot see.</para>
    /// </summary>
    internal void SettleTest(SyntaxNode node, bool answer) => _settledTests[node] = answer;

    /// <summary>
    /// The answer a type test was settled with, or null when it is a real question about the
    /// value and has to be asked at run time.
    /// </summary>
    public bool? GetSettledTest(SyntaxNode node) =>
        _settledTests.TryGetValue(node, out bool answer) ? answer : null;

    /// <summary>
    /// <para>Records what a value has to do to reach where it sits, in order.</para>
    /// <para>Written down by the type checker, because it is the only pass that knows both
    /// what a value is and what is expected of it. Lowering then makes the conversion a real
    /// node rather than working the question out a second time.</para>
    /// <para><b>A sequence, because one value may have two things to do.</b> <c>real? tally =
    /// 3</c> widens the integer and then wraps the result, and either step alone leaves the slot
    /// holding something its type says it does not.</para>
    /// </summary>
    internal void RecordConversion(
        SyntaxNode node,
        IReadOnlyList<(ConversionOperation Operation, TypeSymbol Target)> steps) =>
        _conversions[node] = steps;

    /// <summary>
    /// What a node has to do to reach its place, in the order it has to do it — empty where it
    /// needs nothing. Each step carries the type it produces rather than leaving it to be
    /// derived, since the node's own type is what it was <em>before</em> converting.
    /// </summary>
    public IReadOnlyList<(ConversionOperation Operation, TypeSymbol Target)> GetConversion(
        SyntaxNode node) =>
        _conversions.TryGetValue(
            node, out IReadOnlyList<(ConversionOperation, TypeSymbol)>? found) ? found : [];

    /// <summary>Every type declared anywhere, for tooling and for tests.</summary>
    public IEnumerable<TypeSymbol> AllTypes()
    {
        Stack<NamespaceSymbol> pending = new();
        pending.Push(GlobalNamespace);

        while (pending.Count > 0)
        {
            NamespaceSymbol current = pending.Pop();

            foreach (TypeSymbol type in current.Types.Values)
            {
                yield return type;

                if (type is DeclaredTypeSymbol declared)
                {
                    foreach (TypeSymbol nested in NestedTypes(declared))
                    {
                        yield return nested;
                    }
                }
            }

            foreach (NamespaceSymbol child in current.Namespaces.Values)
            {
                pending.Push(child);
            }
        }

        static IEnumerable<TypeSymbol> NestedTypes(DeclaredTypeSymbol type)
        {
            foreach (List<Symbol> group in type.Members.Values)
            {
                foreach (Symbol member in group)
                {
                    if (member is DeclaredTypeSymbol nested)
                    {
                        yield return nested;

                        foreach (TypeSymbol deeper in NestedTypes(nested))
                        {
                            yield return deeper;
                        }
                    }
                }
            }
        }
    }
}
