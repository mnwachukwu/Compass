using Compass.Compiler.Ast;
using Compass.Compiler.Diagnostics;
using Compass.Compiler.Semantics;

namespace Compass.Compiler.Emit;

/// <summary>
/// <para>Refuses a program that calls a member the host registered, before the emitter is
/// handed it.</para>
/// <para>The emitter can reach any .NET method it can name in metadata. A host member is not
/// one: it is a delegate held by whatever process is doing the compiling, and an assembly
/// outlives that process. Emitting the call would produce something that builds and then fails
/// at the moment it runs, in a program nobody can read the reason out of.</para>
/// <para>So the answer is a diagnostic, and the rule is that a program using host members runs
/// and does not build. Phase 5 of host interop — reading a .NET assembly into the catalog —
/// is what would change this, because a member that came from metadata can go back into it.
/// </para>
/// </summary>
public static class ExternalMembers
{
    /// <summary>
    /// Reports every place a program named a host member, and answers whether any was found.
    /// </summary>
    public static bool Refuse(SemanticModel model, DiagnosticBag diagnostics)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(diagnostics);

        bool any = false;

        // Ordered by position so that a program with several reads top to bottom, rather than
        // in whatever order the nodes were recorded in.
        foreach (SyntaxNode node in model.ExternalMemberUses.OrderBy(n => n.Span.Start.Offset))
        {
            diagnostics.Report(
                DiagnosticDescriptors.CannotEmitExternalMember,
                node.Span,
                model.GetExternal(node)?.Name ?? "the member");

            any = true;
        }

        return any;
    }
}
