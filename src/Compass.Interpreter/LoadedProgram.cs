using Compass.Compiler.Ast;
using Compass.Compiler.Semantics;
using Compass.Runtime;

namespace Compass.Interpreter;

/// <summary>
/// <para>A checked program, initialized once and kept, so that a host can call into it as
/// often as it likes against the state the last call left behind.</para>
/// <para><c>Interpreter.Run</c> answers the other question — run this program and tell
/// me what it returned — and is what <c>cm run</c> wants. A game calls <c>OnTick</c> ten
/// thousand times against one program whose top-level state was worked out once, which is this.
/// </para>
/// <para><b>This is not thread-safe, and it is not safe to call re-entrantly.</b> One call at a
/// time, from one thread at a time. Nothing here detects a breach of that: two threads calling
/// at once corrupt the shared environment quietly rather than raising, so a host that runs a
/// game loop on one thread and a network loop on another must marshal both onto one.</para>
/// <para><b>Stack.</b> A call runs on the thread that makes it, and one Compass call costs
/// about 4 KB of it. The default depth of 512 therefore needs roughly 2 MB, which is more than
/// an ordinary thread has: a host either calls on a thread it created with room, or lowers
/// <c>maximumDepth</c> to what its thread can hold. Reaching the depth raises
/// <c>RecursionTooDeepException</c>, which a host can catch; running out of real stack does
/// not, and ends the process.</para>
/// </summary>
public sealed class LoadedProgram
{
    private readonly Interpreter _interpreter;

    private LoadedProgram(Interpreter interpreter) => _interpreter = interpreter;

    /// <summary>
    /// <para>Initializes a program and holds it.</para>
    /// <para>Every shared field's initializer runs here, on the calling thread, so this wants
    /// the same thread the calls will come in on.</para>
    /// </summary>
    /// <param name="lowered">The lowered tree, as <c>Interpreter.Run</c> takes.</param>
    /// <param name="model">What the front end worked out about it.</param>
    /// <param name="output">Where the program writes, or the process's own output.</param>
    /// <param name="input">Where it reads, or the process's own input.</param>
    /// <param name="maximumDepth">
    /// How deep a call may go before <c>RecursionTooDeepException</c>. Null takes the language's
    /// default of 512, which is sized for a thread with about 2 MB to spare.
    /// </param>
    public static LoadedProgram Load(
        IReadOnlyList<CompilationUnit> lowered,
        SemanticModel model,
        TextWriter? output = null,
        TextReader? input = null,
        int? maximumDepth = null)
    {
        ArgumentNullException.ThrowIfNull(lowered);
        ArgumentNullException.ThrowIfNull(model);

        Interpreter interpreter = Interpreter.ForHost(
            model, output ?? Console.Out, input ?? Console.In, maximumDepth);

        interpreter.Initialize(lowered);

        return new LoadedProgram(interpreter);
    }

    /// <summary>
    /// Whether the program offers a function of this name and arity on a <c>shared model</c> of
    /// that name. A host asks before calling, since a script is free not to write one.
    /// </summary>
    public bool Offers(string modelName, string functionName, int arity)
    {
        ArgumentNullException.ThrowIfNull(modelName);
        ArgumentNullException.ThrowIfNull(functionName);

        return _interpreter.Callable(modelName, functionName, arity) is not null;
    }

    /// <summary>
    /// <para>Calls a function the program declared, with values already in Compass's own
    /// shapes: a <c>long</c> for an integer, a <c>decimal</c> for a real, a <c>string</c>, a
    /// <c>bool</c>, null for an absent optional, or an opaque value of a registered type.
    /// </para>
    /// <para>What comes back is the same kind of thing, or null where the function yields
    /// nothing.</para>
    /// </summary>
    /// <exception cref="MissingFunctionException">
    /// The program declares no such function. <see cref="Offers"/> answers that without
    /// throwing.
    /// </exception>
    public object? Call(
        string modelName, string functionName, params object?[] arguments)
    {
        ArgumentNullException.ThrowIfNull(modelName);
        ArgumentNullException.ThrowIfNull(functionName);
        ArgumentNullException.ThrowIfNull(arguments);

        FunctionDecl declaration =
            _interpreter.Callable(modelName, functionName, arguments.Length)
            ?? throw new MissingFunctionException(modelName, functionName, arguments.Length);

        try
        {
            return _interpreter.InvokeCallable(declaration, arguments);
        }
        catch (CompassThrow uncaught)
        {
            // An exception the program declared and nothing caught. CompassThrow is the
            // interpreter's own carrier and says nothing to a host, so it is unwrapped into
            // the program's exception again, with the line it left from.
            throw Escaped(
                uncaught.Thrown.Type.Name, uncaught.Thrown.Message ?? string.Empty, cause: null);
        }
        catch (Exception raised) when (Escapable(raised))
        {
            throw Escaped(raised.GetType().Name, raised.Message, raised);
        }
    }

    /// <summary>
    /// Whether a failure is the program's rather than the host's. An
    /// <see cref="ExternalFailureException"/> is the host's own binding failing and is handed
    /// back as it is, since wrapping it as a script fault would blame the wrong side.
    /// </summary>
    private static bool Escapable(Exception raised) =>
        raised is not ExternalFailureException
        && raised is not MissingFunctionException
        && BuiltInExceptions.IsBuiltIn(raised);

    private ScriptFailedException Escaped(string typeName, string text, Exception? cause)
    {
        (string file, int line, int column) = _interpreter.Position;

        return new ScriptFailedException(typeName, text, file, line, column, cause);
    }
}

/// <summary>
/// <para>A script failed, and nothing in it caught the failure.</para>
/// <para>Carries where it happened, because the person reading this is reading a log on a
/// server rather than looking at the file: a game operator has no editor open and no way to
/// find the line from the message alone.</para>
/// <para>Every failure a script causes arrives as this one, so that the position is there
/// whatever went wrong. Which failure it was is <see cref="TypeName"/>, and where the language
/// raised it itself the original is the inner exception — so a host wanting to treat one kind
/// differently matches on that rather than on the text:</para>
/// <code>
/// catch (ScriptFailedException failed)
///     when (failed.InnerException is RecursionTooDeepException)
/// </code>
/// </summary>
public sealed class ScriptFailedException(
    string typeName, string text, string file, int line, int column, Exception? cause = null)
    : Exception($"{file}({line},{column}): {typeName}: {text}", cause)
{
    /// <summary>The name of the exception the program threw.</summary>
    public string TypeName { get; } = typeName;

    /// <summary>The message it carried.</summary>
    public string Text { get; } = text;

    /// <summary>The file the innermost running statement was in.</summary>
    public string File { get; } = file;

    /// <summary>Its line, counted from one.</summary>
    public int Line { get; } = line;

    /// <summary>Its column, counted from one.</summary>
    public int Column { get; } = column;
}

/// <summary>
/// <para>A member the host registered threw something the language has no name for.</para>
/// <para>That is a fault in the host's own code rather than in the script, so it travels out
/// past every <c>catch</c> the script wrote and arrives here. A binding meaning to signal
/// something a script should handle throws an exception the language names — the same names
/// .NET uses — and a script catches that as it would any other.</para>
/// </summary>
public sealed class ExternalFailureException(string memberName, Exception cause)
    : Exception($"'{memberName}' failed: {cause?.Message}", cause)
{
    /// <summary>The registered member whose binding threw.</summary>
    public string MemberName { get; } = memberName;
}

/// <summary>
/// A host asked for a function the program does not declare. Separate from the language's own
/// exceptions because it is a fault in the host's expectations rather than in the script.
/// </summary>
public sealed class MissingFunctionException(
    string modelName, string functionName, int arity)
    : InvalidOperationException(
        $"This program declares no '{modelName}.{functionName}' taking {arity} argument(s).")
{
    /// <summary>The shared model that was asked for.</summary>
    public string ModelName { get; } = modelName;

    /// <summary>The function that was asked for.</summary>
    public string FunctionName { get; } = functionName;

    /// <summary>How many arguments it was asked for with.</summary>
    public int Arity { get; } = arity;
}
