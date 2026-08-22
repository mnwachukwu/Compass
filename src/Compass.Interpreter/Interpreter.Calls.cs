using System.Globalization;
using Compass.Compiler.Ast;
using Compass.Compiler.Semantics;
using Compass.Runtime;

namespace Compass.Interpreter;

public sealed partial class Interpreter
{
    /// <summary>
    /// <para>Runs a call.</para>
    /// <para>Three shapes reach here: a member call, which is the common one; a call on a
    /// function held in a variable; and <c>base(...)</c>, which runs a parent's constructor
    /// rather than calling anything.</para>
    /// </summary>
    private object? EvaluateCall(CallExpr call, Environment scope, Instance? receiver)
    {
        if (call.Callee is ReceiverExpr { Receiver: ReceiverKind.Base })
        {
            return RunBaseConstructor(call, scope, receiver);
        }

        if (call.Callee is MemberExpr member)
        {
            return EvaluateMemberCall(call, member, scope, receiver);
        }

        // A function held in a variable, including a local function or a lambda.
        object? callee = Evaluate(call.Callee, scope, receiver);
        List<object?> arguments = [.. call.Arguments.Select(a => Evaluate(a, scope, receiver))];

        return callee is FunctionValue function
            ? Invoke(function, call.Arguments, arguments)
            : throw new CompassRuntimeException("This is not something that can be called.");
    }

    private object? EvaluateMemberCall(
        CallExpr call,
        MemberExpr member,
        Environment scope,
        Instance? receiver)
    {
        // 'Or' before anything is evaluated, because its fallback must not run unless it is
        // wanted. Every other built-in takes its arguments as values and cannot tell how they
        // were arrived at; this one is the only place in the language, besides 'and' and 'or',
        // where whether an expression runs at all is part of what the member means.
        if (_model.GetBuiltIn(member) is BuiltInId.OptionalOr)
        {
            return EvaluateOr(call, member, scope, receiver);
        }

        List<object?> arguments = [.. call.Arguments.Select(a => Evaluate(a, scope, receiver))];

        // A type name on the left: either a built-in like Console, or a shared function.
        if (TypeNamedBy(member.Receiver) is not null)
        {
            // Which version of an overloaded name this is was settled while checking, weighing
            // what the arguments actually are. Looking it up again by name would find the
            // first one written, so Math.Abs on a real would run the version taking integers.
            if (_model.GetBuiltIn(member) is { } onType)
            {
                return Perform(onType, target: null, arguments).Value;
            }

            if (_model.GetSymbol(member) is FunctionSymbol callee
                && BodyOf(callee) is { } shared)
            {
                return Invoke(
                    new FunctionValue(shared.Parameters, shared.Body, null, _shared, null, shared.Name, _fileOf.GetValueOrDefault(shared, _file)),
                    call.Arguments,
                    arguments);
            }

            // A shared field holding a function value is called through, rather than dispatched
            // to, exactly as one reached through an instance is.
            if (_model.GetSymbol(member) is FieldSymbol onTypeField
                && _shared.Lookup(onTypeField)?.Value is FunctionValue heldOnType)
            {
                return Invoke(heldOnType, call.Arguments, arguments);
            }

            return null;
        }

        object? target = Evaluate(member.Receiver, scope, receiver);

        // "base.Method()" runs the parent's version rather than the overriding one.
        if (member.Receiver is ReceiverExpr { Receiver: ReceiverKind.Base }
            && receiver is not null
            && _model.GetSymbol(member) is FunctionSymbol parent
            && BodyOf(parent) is { } parentMethod)
        {
            return Invoke(
                new FunctionValue(parentMethod.Parameters, parentMethod.Body, null, _shared, receiver, parentMethod.Name, _fileOf.GetValueOrDefault(parentMethod, _file)),
                call.Arguments,
                arguments);
        }

        // The type checker already settled which member this name refers to, weighing the
        // receiver's type, what narrowing proved about it, and whether that type declares a
        // member of the same name. Deciding again from the value in hand would be answering a
        // different question.
        if (_model.GetBuiltIn(member) is { } id)
        {
            return Perform(id, target, arguments).Value;
        }

        if (target is Instance instance)
        {
            // Dispatch on the runtime type, so an override wins over the version the
            // declaring type wrote.
            if (FindMethod(instance.Type, _model.GetSymbol(member) as FunctionSymbol, member.MemberName, arguments.Count) is { } found
                && BodyOf(found) is { } method)
            {
                return Invoke(
                    new FunctionValue(method.Parameters, method.Body, null, _shared, instance, method.Name, _fileOf.GetValueOrDefault(method, _file)),
                    call.Arguments,
                    arguments);
            }

            // A field holding a function value is called through, rather than dispatched to.
            if (_model.GetSymbol(member) is FieldSymbol field
                && instance.Fields.TryGetValue(field, out object? stored)
                && stored is FunctionValue held)
            {
                return Invoke(held, call.Arguments, arguments);
            }
        }

        throw new CompassRuntimeException(
            $"'{member.MemberName}' cannot be called on this value.");
    }

    /// <summary>
    /// <para>Finds the version to run, nearest ancestor first. Starting at the runtime type is
    /// what makes an override take effect.</para>
    /// <para><b>Matched against the version the type checker chose, where it chose one.</b> Name
    /// and count alone is not enough once a model may declare a version its parent did not: a
    /// child's <c>Which(string)</c> has the same name and the same count as its parent's
    /// <c>Which(integer)</c>, and the nearest one is not the one the call was checked against.
    /// Which version a call means was settled while checking, weighing what the arguments
    /// actually are; this looks only for that version's override.</para>
    /// </summary>
    private static FunctionSymbol? FindMethod(
        DeclaredTypeSymbol type, FunctionSymbol? chosen, string name, int arity)
    {
        IEnumerable<DeclaredTypeSymbol> chain = type is ModelSymbol model
            ? model.SelfAndAncestors()
            : [type];

        foreach (DeclaredTypeSymbol current in chain)
        {
            IEnumerable<FunctionSymbol> versions = current.Lookup(name).OfType<FunctionSymbol>();

            if (versions.FirstOrDefault(
                    version => chosen is null
                        ? version.Parameters.Count == arity
                        : Conversions.SameParameters(version, chosen)) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    /// <summary>
    /// <para>An optional's <c>Or</c>, which reaches its fallback only where there is nothing to
    /// give back.</para>
    /// <para>An empty optional is null here, so the whole of the rule is the null-coalescing
    /// below — and the argument is an expression until that decides it is needed.</para>
    /// </summary>
    private object? EvaluateOr(
        CallExpr call,
        MemberExpr member,
        Environment scope,
        Instance? receiver) =>
        Evaluate(member.Receiver, scope, receiver)
        ?? Evaluate(call.Arguments[0], scope, receiver);

    private object? RunBaseConstructor(CallExpr call, Environment scope, Instance? receiver)
    {
        // Whose constructor this 'base' was written in, not what the instance turned out to
        // be. A three-deep chain asking the instance would find the same parent three times.
        if (receiver is null || _constructing is not ModelSymbol { BaseType: { } parent })
        {
            return null;
        }

        List<object?> arguments = [.. call.Arguments.Select(a => Evaluate(a, scope, receiver))];

        if (FindConstructor(parent, arguments.Count) is { } constructor
            && BodyOf(constructor) is { } body)
        {
            RunConstructor(body, receiver, arguments, parent);
            return null;
        }

        // Exception declares no constructor a program can see, so its one argument is taken
        // here. This is what makes base("...") in a declared exception reach Message().
        if (BuiltInMembers.IsException(parent) && arguments.Count == 1)
        {
            receiver.Message = AsText(arguments[0]);
        }

        return null;
    }

    // ---- The members the language provides -------------------------------------------------------

    /// <summary>
    /// <para>Carries out a built-in call and checks that what came back is what the catalog
    /// said would.</para>
    /// <para>The catalog is what the type checker believed; this is what actually happened.
    /// Nothing else compares the two, and the mistake is invisible from a program: a member
    /// declared to yield an integer that hands back a real prints the same characters, passes
    /// every recorded output, and only goes wrong somewhere far away where the value is used
    /// as a count.</para>
    /// </summary>
    private StrongBox<object?> Perform(BuiltInId id, object? target, List<object?> arguments)
    {
        StrongBox<object?> produced = PerformCore(id, target, arguments);

        if (BuiltInResults.Disagrees(id, produced.Value) is { } complaint)
        {
            throw new CompassRuntimeException(complaint);
        }

        return produced;
    }

    /// <summary>
    /// <para>Carries out a built-in call.</para>
    /// <para>A switch expression over the whole enumeration with no fallback arm, so that a
    /// member in the catalog with no implementation here is a build error rather than a call
    /// that quietly produces nothing.</para>
    /// <para>The target is the value on the left, and is null for a member reached through a
    /// model's name rather than through a value.</para>
    /// </summary>
    // CS8524 asks for a fallback arm covering values cast into the enumeration from outside
    // it. A fallback arm also satisfies CS8509, which is the warning that reports a catalog
    // member with no implementation here, so the narrower one is suppressed instead.
#pragma warning disable CS8524
    private StrongBox<object?> PerformCore(BuiltInId id, object? target, List<object?> arguments)
    {
        object? Argument(int index) => arguments.ElementAtOrDefault(index);
        decimal Real(int index) => Argument(index) is decimal d ? d : 0;
        double Float(int index) => Argument(index) is double f ? f : 0;
        long Integer(int index) => AsInteger(Argument(index));
        string Text(int index) => AsText(Argument(index));

        // An integer widens to a fraction on the way in, and the widening is recorded on the
        // argument rather than carried out here, so one that arrives whole is converted.
        Fraction Ratio(int index) => Argument(index) switch
        {
            Fraction fraction => fraction,
            long whole => Fraction.FromInteger(whole),
            _ => Fraction.Zero,
        };

        CompassSet<object?> Set() => (CompassSet<object?>)target!;

        CompassSet<object?> OtherSet(int index) => (CompassSet<object?>)Argument(index)!;
        string Subject() => (string)target!;

        CompassSet<object?> CharacterSet(int index) =>
            Argument(index) as CompassSet<object?> ?? [];

        // An optional the runtime answered with, put back into the shape this engine holds one
        // in: the value itself, or a null where there is none.
        static object? Held<T>(Optional<T> answered) =>
            answered.HasValue ? answered.Value : null;

        // The generator being asked: the one the program is holding, or the one the language
        // keeps for everything that did not ask for its own.
        CompassRandom Chance() => target as CompassRandom ?? _chance;

        DateTime Moment() => target is DateTime moment ? moment : default;
        DateTime OtherMoment(int index) => Argument(index) is DateTime other ? other : default;

        TimeSpan Length() => target is TimeSpan span ? span : default;
        TimeSpan Span(int index) => Argument(index) is TimeSpan given ? given : default;

        DateOnly Day() => target is DateOnly day ? day : default;
        DateOnly OtherDay(int index) => Argument(index) is DateOnly other ? other : default;

        TimeOnly OnTheClock() => target is TimeOnly clock ? clock : default;
        TimeOnly Clock(int index) => Argument(index) is TimeOnly given ? given : default;

        return id switch
        {
            // ---- Reached through a model's name ------------------------------------------

            BuiltInId.ConsoleWrite =>
                Then(() => _output.Write(ModelOperations.ToDisplayString(Argument(0)))),

            BuiltInId.ConsoleWriteLine =>
                Then(() => _output.WriteLine(arguments.Count == 0
                    ? string.Empty
                    : ModelOperations.ToDisplayString(arguments[0]))),

            // A null line means the input ran out, which is exactly what an absent optional
            // says, so nothing has to translate it.
            BuiltInId.ConsoleRead => new StrongBox<object?>(_input.ReadLine()),

            BuiltInId.ReferenceEquals =>
                new StrongBox<object?>(ReferenceEquals(Argument(0), Argument(1))),

            BuiltInId.MathPi => new StrongBox<object?>(CompassMath.Pi),
            BuiltInId.MathE => new StrongBox<object?>(CompassMath.E),

            // Where each number type runs out. The framework's own bounds, since these describe
            // what the value actually holds rather than anything the language decides.
            BuiltInId.IntegerMaxValue => new StrongBox<object?>(long.MaxValue),
            BuiltInId.IntegerMinValue => new StrongBox<object?>(long.MinValue),
            BuiltInId.RealMaxValue => new StrongBox<object?>(decimal.MaxValue),
            BuiltInId.RealMinValue => new StrongBox<object?>(decimal.MinValue),
            BuiltInId.FloatMaxValue => new StrongBox<object?>(double.MaxValue),
            BuiltInId.FloatMinValue => new StrongBox<object?>(double.MinValue),

            // The three a real has no answer for, and the same values a float's own arithmetic
            // produces — so comparing one against '1.0f / 0.0f' is true.
            BuiltInId.FloatInfinity => new StrongBox<object?>(double.PositiveInfinity),
            BuiltInId.FloatNegativeInfinity => new StrongBox<object?>(double.NegativeInfinity),
            BuiltInId.FloatNotANumber => new StrongBox<object?>(double.NaN),

            BuiltInId.StringEmpty => new StrongBox<object?>(string.Empty),

            BuiltInId.MathSqrt => new StrongBox<object?>(CompassMath.Sqrt(Real(0))),
            BuiltInId.MathCbrt => new StrongBox<object?>(CompassMath.Cbrt(Real(0))),
            BuiltInId.MathRoot => new StrongBox<object?>(CompassMath.Root(Real(0), Real(1))),
            BuiltInId.MathPow => new StrongBox<object?>(CompassMath.Pow(Real(0), Real(1))),
            BuiltInId.MathFactorial => new StrongBox<object?>(CompassMath.Factorial(Integer(0))),

            BuiltInId.MathLog => new StrongBox<object?>(CompassMath.Log(Real(0))),
            BuiltInId.MathLogInBase => new StrongBox<object?>(CompassMath.Log(Real(0), Real(1))),
            BuiltInId.MathLog10 => new StrongBox<object?>(CompassMath.Log10(Real(0))),
            BuiltInId.MathLog2 => new StrongBox<object?>(CompassMath.Log2(Real(0))),

            BuiltInId.MathSin => new StrongBox<object?>(CompassMath.Sin(Real(0))),
            BuiltInId.MathCos => new StrongBox<object?>(CompassMath.Cos(Real(0))),
            BuiltInId.MathTan => new StrongBox<object?>(CompassMath.Tan(Real(0))),
            BuiltInId.MathAsin => new StrongBox<object?>(CompassMath.Asin(Real(0))),
            BuiltInId.MathAcos => new StrongBox<object?>(CompassMath.Acos(Real(0))),
            BuiltInId.MathAtan => new StrongBox<object?>(CompassMath.Atan(Real(0))),
            BuiltInId.MathAtan2 => new StrongBox<object?>(CompassMath.Atan2(Real(0), Real(1))),

            BuiltInId.MathSinh => new StrongBox<object?>(CompassMath.Sinh(Real(0))),
            BuiltInId.MathCosh => new StrongBox<object?>(CompassMath.Cosh(Real(0))),
            BuiltInId.MathTanh => new StrongBox<object?>(CompassMath.Tanh(Real(0))),
            BuiltInId.MathAsinh => new StrongBox<object?>(CompassMath.Asinh(Real(0))),
            BuiltInId.MathAcosh => new StrongBox<object?>(CompassMath.Acosh(Real(0))),
            BuiltInId.MathAtanh => new StrongBox<object?>(CompassMath.Atanh(Real(0))),

            BuiltInId.MathAbsInteger => new StrongBox<object?>(CompassMath.Abs(Integer(0))),
            BuiltInId.MathAbsReal => new StrongBox<object?>(CompassMath.Abs(Real(0))),
            BuiltInId.MathAbsFraction => new StrongBox<object?>(CompassMath.Abs(Ratio(0))),

            BuiltInId.MathFloorReal => new StrongBox<object?>(CompassMath.Floor(Real(0))),
            BuiltInId.MathFloorFraction => new StrongBox<object?>(CompassMath.Floor(Ratio(0))),
            BuiltInId.MathCeilingReal => new StrongBox<object?>(CompassMath.Ceiling(Real(0))),
            BuiltInId.MathCeilingFraction => new StrongBox<object?>(CompassMath.Ceiling(Ratio(0))),

            BuiltInId.MathRoundReal => new StrongBox<object?>(CompassMath.Round(Real(0))),
            BuiltInId.MathRoundFraction => new StrongBox<object?>(CompassMath.Round(Ratio(0))),
            BuiltInId.MathRoundRealPlaces =>
                new StrongBox<object?>(CompassMath.Round(Real(0), Integer(1))),

            BuiltInId.MathMinInteger => new StrongBox<object?>(CompassMath.Min(Integer(0), Integer(1))),
            BuiltInId.MathMinReal => new StrongBox<object?>(CompassMath.Min(Real(0), Real(1))),
            BuiltInId.MathMinFraction => new StrongBox<object?>(CompassMath.Min(Ratio(0), Ratio(1))),
            BuiltInId.MathMaxInteger => new StrongBox<object?>(CompassMath.Max(Integer(0), Integer(1))),
            BuiltInId.MathMaxReal => new StrongBox<object?>(CompassMath.Max(Real(0), Real(1))),
            BuiltInId.MathMaxFraction => new StrongBox<object?>(CompassMath.Max(Ratio(0), Ratio(1))),

            // The same again on a float, reaching the binary half of each pair. Nothing is
            // converted on the way in or out: that is the point of having both.
            BuiltInId.MathSqrtFloat => new StrongBox<object?>(CompassMath.Sqrt(Float(0))),
            BuiltInId.MathCbrtFloat => new StrongBox<object?>(CompassMath.Cbrt(Float(0))),
            BuiltInId.MathRootFloat => new StrongBox<object?>(CompassMath.Root(Float(0), Float(1))),
            BuiltInId.MathPowFloat => new StrongBox<object?>(CompassMath.Pow(Float(0), Float(1))),

            BuiltInId.MathLogFloat => new StrongBox<object?>(CompassMath.Log(Float(0))),
            BuiltInId.MathLogInBaseFloat =>
                new StrongBox<object?>(CompassMath.Log(Float(0), Float(1))),
            BuiltInId.MathLog10Float => new StrongBox<object?>(CompassMath.Log10(Float(0))),
            BuiltInId.MathLog2Float => new StrongBox<object?>(CompassMath.Log2(Float(0))),

            BuiltInId.MathSinFloat => new StrongBox<object?>(CompassMath.Sin(Float(0))),
            BuiltInId.MathCosFloat => new StrongBox<object?>(CompassMath.Cos(Float(0))),
            BuiltInId.MathTanFloat => new StrongBox<object?>(CompassMath.Tan(Float(0))),
            BuiltInId.MathAsinFloat => new StrongBox<object?>(CompassMath.Asin(Float(0))),
            BuiltInId.MathAcosFloat => new StrongBox<object?>(CompassMath.Acos(Float(0))),
            BuiltInId.MathAtanFloat => new StrongBox<object?>(CompassMath.Atan(Float(0))),
            BuiltInId.MathAtan2Float =>
                new StrongBox<object?>(CompassMath.Atan2(Float(0), Float(1))),

            BuiltInId.MathSinhFloat => new StrongBox<object?>(CompassMath.Sinh(Float(0))),
            BuiltInId.MathCoshFloat => new StrongBox<object?>(CompassMath.Cosh(Float(0))),
            BuiltInId.MathTanhFloat => new StrongBox<object?>(CompassMath.Tanh(Float(0))),
            BuiltInId.MathAsinhFloat => new StrongBox<object?>(CompassMath.Asinh(Float(0))),
            BuiltInId.MathAcoshFloat => new StrongBox<object?>(CompassMath.Acosh(Float(0))),
            BuiltInId.MathAtanhFloat => new StrongBox<object?>(CompassMath.Atanh(Float(0))),

            BuiltInId.MathAbsFloat => new StrongBox<object?>(CompassMath.Abs(Float(0))),
            BuiltInId.MathFloorFloat => new StrongBox<object?>(CompassMath.Floor(Float(0))),
            BuiltInId.MathCeilingFloat => new StrongBox<object?>(CompassMath.Ceiling(Float(0))),
            BuiltInId.MathRoundFloat => new StrongBox<object?>(CompassMath.Round(Float(0))),
            BuiltInId.MathRoundFloatPlaces =>
                new StrongBox<object?>(CompassMath.Round(Float(0), Integer(1))),
            BuiltInId.MathMinFloat => new StrongBox<object?>(CompassMath.Min(Float(0), Float(1))),
            BuiltInId.MathMaxFloat => new StrongBox<object?>(CompassMath.Max(Float(0), Float(1))),

            BuiltInId.FractionCreate =>
                new StrongBox<object?>(new Fraction(Integer(0), Integer(1))),
            BuiltInId.FractionCreateWhole =>
                new StrongBox<object?>(Fraction.FromInteger(Integer(0))),

            BuiltInId.RandomNew => new StrongBox<object?>(new CompassRandom()),
            BuiltInId.RandomNewSeeded => new StrongBox<object?>(new CompassRandom(Integer(0))),

            // One set of members serves both shapes. Reached through a generator the program
            // holds, the target is that generator; reached through the name, there is none and
            // the one the language keeps answers instead.
            BuiltInId.RandomNext => new StrongBox<object?>(Chance().Next()),
            BuiltInId.RandomNextBelow => new StrongBox<object?>(Chance().Next(Integer(0))),
            BuiltInId.RandomNextBetween =>
                new StrongBox<object?>(Chance().Next(Integer(0), Integer(1))),
            BuiltInId.RandomNextDouble => new StrongBox<object?>(Chance().NextReal()),

            BuiltInId.DateTimeNewDate => new StrongBox<object?>(
                CompassMoments.MakeDay(Integer(0), Integer(1), Integer(2))),
            BuiltInId.DateTimeNewMoment => new StrongBox<object?>(CompassMoments.MakeMoment(
                Integer(0), Integer(1), Integer(2), Integer(3), Integer(4), Integer(5))),

            BuiltInId.DateTimeNow => new StrongBox<object?>(CompassMoments.Now()),
            BuiltInId.DateTimeToday => new StrongBox<object?>(CompassMoments.Today()),

            BuiltInId.DateTimeYear => new StrongBox<object?>(CompassMoments.Year(Moment())),
            BuiltInId.DateTimeMonth => new StrongBox<object?>(CompassMoments.Month(Moment())),
            BuiltInId.DateTimeDay => new StrongBox<object?>(CompassMoments.Day(Moment())),
            BuiltInId.DateTimeHour => new StrongBox<object?>(CompassMoments.Hour(Moment())),
            BuiltInId.DateTimeMinute => new StrongBox<object?>(CompassMoments.Minute(Moment())),
            BuiltInId.DateTimeSecond => new StrongBox<object?>(CompassMoments.Second(Moment())),
            BuiltInId.DateTimeDayOfWeek =>
                new StrongBox<object?>(CompassMoments.DayOfWeek(Moment())),
            BuiltInId.DateTimeDayOfYear =>
                new StrongBox<object?>(CompassMoments.DayOfYear(Moment())),

            BuiltInId.DateTimeAddDays =>
                new StrongBox<object?>(CompassMoments.AddDays(Moment(), Real(0))),
            BuiltInId.DateTimeAddHours =>
                new StrongBox<object?>(CompassMoments.AddHours(Moment(), Real(0))),
            BuiltInId.DateTimeAddMinutes =>
                new StrongBox<object?>(CompassMoments.AddMinutes(Moment(), Real(0))),
            BuiltInId.DateTimeAddSeconds =>
                new StrongBox<object?>(CompassMoments.AddSeconds(Moment(), Real(0))),
            BuiltInId.DateTimeAddYears =>
                new StrongBox<object?>(CompassMoments.AddYears(Moment(), Integer(0))),
            BuiltInId.DateTimeAddMonths =>
                new StrongBox<object?>(CompassMoments.AddMonths(Moment(), Integer(0))),

            BuiltInId.DateTimeCompareTo =>
                new StrongBox<object?>(CompassMoments.CompareMoments(Moment(), OtherMoment(0))),

            BuiltInId.DateTimeSubtract =>
                new StrongBox<object?>(CompassMoments.Subtract(Moment(), OtherMoment(0))),
            BuiltInId.DateTimeSubtractSpan =>
                new StrongBox<object?>(CompassMoments.SubtractSpan(Moment(), Span(0))),
            BuiltInId.DateTimeAdd =>
                new StrongBox<object?>(CompassMoments.Add(Moment(), Span(0))),

            BuiltInId.TimeSpanNewTime => new StrongBox<object?>(
                CompassMoments.MakeSpan(Integer(0), Integer(1), Integer(2))),
            BuiltInId.TimeSpanNewSpan => new StrongBox<object?>(
                CompassMoments.MakeSpan(Integer(0), Integer(1), Integer(2), Integer(3))),

            BuiltInId.TimeSpanZero => new StrongBox<object?>(CompassMoments.Zero()),
            BuiltInId.TimeSpanFromDays =>
                new StrongBox<object?>(CompassMoments.FromDays(Real(0))),
            BuiltInId.TimeSpanFromHours =>
                new StrongBox<object?>(CompassMoments.FromHours(Real(0))),
            BuiltInId.TimeSpanFromMinutes =>
                new StrongBox<object?>(CompassMoments.FromMinutes(Real(0))),
            BuiltInId.TimeSpanFromSeconds =>
                new StrongBox<object?>(CompassMoments.FromSeconds(Real(0))),

            BuiltInId.TimeSpanDays => new StrongBox<object?>(CompassMoments.Days(Length())),
            BuiltInId.TimeSpanHours => new StrongBox<object?>(CompassMoments.Hours(Length())),
            BuiltInId.TimeSpanMinutes => new StrongBox<object?>(CompassMoments.Minutes(Length())),
            BuiltInId.TimeSpanSeconds => new StrongBox<object?>(CompassMoments.Seconds(Length())),

            BuiltInId.TimeSpanTotalDays =>
                new StrongBox<object?>(CompassMoments.TotalDays(Length())),
            BuiltInId.TimeSpanTotalHours =>
                new StrongBox<object?>(CompassMoments.TotalHours(Length())),
            BuiltInId.TimeSpanTotalMinutes =>
                new StrongBox<object?>(CompassMoments.TotalMinutes(Length())),
            BuiltInId.TimeSpanTotalSeconds =>
                new StrongBox<object?>(CompassMoments.TotalSeconds(Length())),

            BuiltInId.TimeSpanAdd =>
                new StrongBox<object?>(CompassMoments.AddSpan(Length(), Span(0))),
            BuiltInId.TimeSpanSubtract =>
                new StrongBox<object?>(CompassMoments.SubtractSpans(Length(), Span(0))),
            BuiltInId.TimeSpanNegate => new StrongBox<object?>(CompassMoments.Negate(Length())),
            BuiltInId.TimeSpanDuration => new StrongBox<object?>(CompassMoments.Duration(Length())),
            BuiltInId.TimeSpanCompareTo =>
                new StrongBox<object?>(CompassMoments.CompareSpans(Length(), Span(0))),

            BuiltInId.DateNew => new StrongBox<object?>(
                CompassMoments.MakeDate(Integer(0), Integer(1), Integer(2))),
            BuiltInId.DateToday => new StrongBox<object?>(CompassMoments.TodayOnly()),
            BuiltInId.DateFromMoment =>
                new StrongBox<object?>(CompassMoments.DateFromMoment(OtherMoment(0))),

            BuiltInId.DateYear => new StrongBox<object?>(CompassMoments.DateYear(Day())),
            BuiltInId.DateMonth => new StrongBox<object?>(CompassMoments.DateMonth(Day())),
            BuiltInId.DateDay => new StrongBox<object?>(CompassMoments.DateDay(Day())),
            BuiltInId.DateDayOfWeek => new StrongBox<object?>(CompassMoments.DateDayOfWeek(Day())),
            BuiltInId.DateDayOfYear => new StrongBox<object?>(CompassMoments.DateDayOfYear(Day())),

            BuiltInId.DateAddDays =>
                new StrongBox<object?>(CompassMoments.DateAddDays(Day(), Integer(0))),
            BuiltInId.DateAddMonths =>
                new StrongBox<object?>(CompassMoments.DateAddMonths(Day(), Integer(0))),
            BuiltInId.DateAddYears =>
                new StrongBox<object?>(CompassMoments.DateAddYears(Day(), Integer(0))),

            BuiltInId.DateAtTime =>
                new StrongBox<object?>(CompassMoments.DateAtTime(Day(), Clock(0))),
            BuiltInId.DateCompareTo =>
                new StrongBox<object?>(CompassMoments.CompareDates(Day(), OtherDay(0))),

            BuiltInId.TimeNewToMinute => new StrongBox<object?>(
                CompassMoments.MakeTime(Integer(0), Integer(1))),
            BuiltInId.TimeNewToSecond => new StrongBox<object?>(
                CompassMoments.MakeTime(Integer(0), Integer(1), Integer(2))),
            BuiltInId.TimeNow => new StrongBox<object?>(CompassMoments.TimeNow()),
            BuiltInId.TimeFromMoment =>
                new StrongBox<object?>(CompassMoments.TimeFromMoment(OtherMoment(0))),

            BuiltInId.TimeHour => new StrongBox<object?>(CompassMoments.TimeHour(OnTheClock())),
            BuiltInId.TimeMinute => new StrongBox<object?>(CompassMoments.TimeMinute(OnTheClock())),
            BuiltInId.TimeSecond => new StrongBox<object?>(CompassMoments.TimeSecond(OnTheClock())),

            BuiltInId.TimeAddHours =>
                new StrongBox<object?>(CompassMoments.TimeAddHours(OnTheClock(), Real(0))),
            BuiltInId.TimeAddMinutes =>
                new StrongBox<object?>(CompassMoments.TimeAddMinutes(OnTheClock(), Real(0))),

            BuiltInId.TimeToTimeSpan =>
                new StrongBox<object?>(CompassMoments.TimeToSpan(OnTheClock())),
            BuiltInId.TimeCompareTo =>
                new StrongBox<object?>(CompassMoments.CompareTimes(OnTheClock(), Clock(0))),

            // ---- Reached through a value --------------------------------------------------

            BuiltInId.SetCount => new StrongBox<object?>((long)Set().Count),
            BuiltInId.SetInsert => Then(() => Set().Insert(Argument(0))),
            BuiltInId.SetInsertAt => Then(() => Set().InsertAt((int)Integer(0), Argument(1))),
            BuiltInId.SetRemove => new StrongBox<object?>(Set().Remove(Argument(0))),
            BuiltInId.SetRemoveAt => Then(() => Set().RemoveAt((int)Integer(0))),
            BuiltInId.SetContains => new StrongBox<object?>(Set().Contains(Argument(0))),
            BuiltInId.SetIndexOf => new StrongBox<object?>((long)Set().IndexOf(Argument(0))),
            BuiltInId.SetClear => Then(Set().Clear),

            // Both leave their two originals alone and hand back a new set, as Subset does.
            // Membership is asked of the other set, so it is the same structural question
            // '==' asks rather than a reference check.
            // Each of these is the set's own, so that the emitter calling the same method is
            // calling the same code rather than a second version of it that agrees today.
            BuiltInId.SetUnion => new StrongBox<object?>(Set().Union(OtherSet(0))),
            BuiltInId.SetIntersect => new StrongBox<object?>(Set().Intersect(OtherSet(0))),
            BuiltInId.SetExcept => new StrongBox<object?>(Set().Except(OtherSet(0))),
            BuiltInId.SetDistinct => new StrongBox<object?>(Set().Distinct()),

            BuiltInId.SetSubsetFrom => new StrongBox<object?>(Set().Subset((int)Integer(0))),
            BuiltInId.SetSubsetBetween => new StrongBox<object?>(
                Set().Subset((int)Integer(0), (int)Integer(1))),

            // An element of a set of optionals is the value itself, or null for an empty one, so
            // there is nothing to unwrap here and TrimAll is the plain filter. The runtime knows
            // both shapes, which is what keeps this and an emitted program agreeing.
            BuiltInId.SetTrim => new StrongBox<object?>(Set().Trim()),
            BuiltInId.SetTrimStart => new StrongBox<object?>(Set().TrimStart()),
            BuiltInId.SetTrimEnd => new StrongBox<object?>(Set().TrimEnd()),
            BuiltInId.SetTrimAll => new StrongBox<object?>(Set().TrimAll()),

            BuiltInId.SetJoin => new StrongBox<object?>(Set().Join(Text(0))),

            BuiltInId.StringCount => new StrongBox<object?>((long)Subject().Length),
            BuiltInId.StringContains =>
                new StrongBox<object?>(CompassText.Contains(Subject(), Text(0))),
            BuiltInId.StringIndexOf =>
                new StrongBox<object?>(CompassText.IndexOf(Subject(), Text(0))),
            BuiltInId.StringSubstring =>
                new StrongBox<object?>(CompassText.Substring(Subject(), Integer(0), Integer(1))),

            BuiltInId.StringSubsetFrom =>
                new StrongBox<object?>(CompassText.Subset(Subject(), Integer(0))),
            BuiltInId.StringSubsetBetween =>
                new StrongBox<object?>(CompassText.Subset(Subject(), Integer(0), Integer(1))),
            BuiltInId.StringInsert =>
                new StrongBox<object?>(CompassText.Insert(Subject(), Text(0))),
            BuiltInId.StringInsertAt =>
                new StrongBox<object?>(CompassText.InsertAt(Subject(), Integer(0), Text(1))),
            BuiltInId.StringRemove =>
                new StrongBox<object?>(CompassText.Remove(Subject(), Text(0))),
            BuiltInId.StringRemoveAt =>
                new StrongBox<object?>(CompassText.RemoveAt(Subject(), Integer(0))),
            BuiltInId.StringToCharacters =>
                new StrongBox<object?>(CompassText.ToCharactersUntyped(Subject())),

            BuiltInId.StringTrim => new StrongBox<object?>(CompassText.Trim(Subject())),
            BuiltInId.StringTrimText =>
                new StrongBox<object?>(CompassText.Trim(Subject(), Text(0))),
            BuiltInId.StringTrimSet =>
                new StrongBox<object?>(CompassText.Trim(Subject(), CharacterSet(0))),

            BuiltInId.StringTrimStart => new StrongBox<object?>(CompassText.TrimStart(Subject())),
            BuiltInId.StringTrimStartText =>
                new StrongBox<object?>(CompassText.TrimStart(Subject(), Text(0))),
            BuiltInId.StringTrimStartSet =>
                new StrongBox<object?>(CompassText.TrimStart(Subject(), CharacterSet(0))),

            BuiltInId.StringTrimEnd => new StrongBox<object?>(CompassText.TrimEnd(Subject())),
            BuiltInId.StringTrimEndText =>
                new StrongBox<object?>(CompassText.TrimEnd(Subject(), Text(0))),
            BuiltInId.StringTrimEndSet =>
                new StrongBox<object?>(CompassText.TrimEnd(Subject(), CharacterSet(0))),

            BuiltInId.StringSplit =>
                new StrongBox<object?>(CompassText.SplitUntyped(Subject(), Text(0))),

            BuiltInId.StringReplace =>
                new StrongBox<object?>(CompassText.Replace(Subject(), Text(0), Text(1))),

            BuiltInId.StringToUpper => new StrongBox<object?>(CompassText.ToUpper(Subject())),
            BuiltInId.StringToLower => new StrongBox<object?>(CompassText.ToLower(Subject())),
            BuiltInId.StringCapitalize =>
                new StrongBox<object?>(CompassText.Capitalize(Subject())),

            // An optional is the value itself, or nothing at all, so absence is a null target
            // rather than a wrapper to look inside.
            BuiltInId.OptionalHasValue => new StrongBox<object?>(target is not null),
            BuiltInId.OptionalOr => new StrongBox<object?>(target ?? Argument(0)),
            BuiltInId.OptionalValue => target is not null
                ? new StrongBox<object?>(target)
                : throw new EmptyOptionalException(),

            // Each reads as an optional, and this engine holds an empty one as a null — so
            // what the runtime answers is unwrapped on the way out rather than kept.
            BuiltInId.StringToInteger =>
                new StrongBox<object?>(Held(CompassText.ToInteger(Subject()))),
            BuiltInId.StringToReal =>
                new StrongBox<object?>(Held(CompassText.ToReal(Subject()))),
            BuiltInId.StringToFloat =>
                new StrongBox<object?>(Held(CompassText.ToFloat(Subject()))),
            BuiltInId.StringToBoolean =>
                new StrongBox<object?>(Held(CompassText.ToBoolean(Subject()))),
            BuiltInId.StringToCharacter =>
                new StrongBox<object?>(Held(CompassText.ToCharacter(Subject()))),

            BuiltInId.StringToFraction => new StrongBox<object?>(CompassText.ToFractionUntyped(Subject())),

            // The same readings reached through the type's own name, and deliberately the same
            // call underneath: two spellings of one question have to give one answer, and the
            // only way to be sure of that is for there to be one place it is answered.
            //
            // The text is an argument here rather than the receiver, since the receiver is a name
            // that holds nothing.
            BuiltInId.IntegerParse =>
                new StrongBox<object?>(Held(CompassText.ToInteger(Text(0)))),
            BuiltInId.RealParse =>
                new StrongBox<object?>(Held(CompassText.ToReal(Text(0)))),
            BuiltInId.FloatParse =>
                new StrongBox<object?>(Held(CompassText.ToFloat(Text(0)))),
            BuiltInId.BooleanParse =>
                new StrongBox<object?>(Held(CompassText.ToBoolean(Text(0)))),
            BuiltInId.CharacterParse =>
                new StrongBox<object?>(Held(CompassText.ToCharacter(Text(0)))),
            BuiltInId.FractionParse =>
                new StrongBox<object?>(CompassText.ToFractionUntyped(Text(0))),

            // ---- Files ----------------------------------------------------------------
            //
            // A file that is not there gives nothing back, so the ordinary question needs no
            // guard. Every other failure travels as the IOException it already is, which is
            // the type a program names after 'catch'.
            BuiltInId.FileRead => new StrongBox<object?>(CompassFiles.ReadUntyped(Text(0))),
            BuiltInId.FileReadLines =>
                new StrongBox<object?>(CompassFiles.ReadLinesUntyped(Text(0))),

            BuiltInId.FileWrite => Then(() => CompassFiles.Write(Text(0), Text(1))),
            BuiltInId.FileWriteLines => Then(() => CompassFiles.WriteLines(Text(0), OtherSet(1))),
            BuiltInId.FileAppend => Then(() => CompassFiles.Append(Text(0), Text(1))),

            BuiltInId.FileExists => new StrongBox<object?>(CompassFiles.Exists(Text(0))),

            BuiltInId.FileDelete => new StrongBox<object?>(CompassFiles.Delete(Text(0))),

            BuiltInId.FileCopy => Then(() => CompassFiles.Copy(Text(0), Text(1))),
            BuiltInId.FileMove => Then(() => CompassFiles.Move(Text(0), Text(1))),

            BuiltInId.FileSize => new StrongBox<object?>(CompassFiles.SizeUntyped(Text(0))),
            BuiltInId.FileChanged => new StrongBox<object?>(CompassFiles.ChangedUntyped(Text(0))),

            BuiltInId.DirectoryCurrent => new StrongBox<object?>(CompassFiles.Current()),
            BuiltInId.DirectoryExists => new StrongBox<object?>(CompassFiles.FolderExists(Text(0))),
            BuiltInId.DirectoryCreate => Then(() => CompassFiles.CreateFolder(Text(0))),
            BuiltInId.DirectoryDelete => new StrongBox<object?>(CompassFiles.DeleteFolder(Text(0))),

            BuiltInId.DirectoryFiles => new StrongBox<object?>(CompassFiles.FilesUntyped(Text(0))),
            BuiltInId.DirectoryFolders =>
                new StrongBox<object?>(CompassFiles.FoldersUntyped(Text(0))),

            // The halves of a moment, and the ways of building one from them.
            BuiltInId.DateTimeDatePart => new StrongBox<object?>(CompassMoments.DatePart(Moment())),
            BuiltInId.DateTimeTimePart => new StrongBox<object?>(CompassMoments.TimePart(Moment())),
            BuiltInId.DateTimeFromDate =>
                new StrongBox<object?>(CompassMoments.FromDate(OtherDay(0))),
            BuiltInId.DateTimeFromDateAndTime =>
                new StrongBox<object?>(CompassMoments.FromDateAndTime(OtherDay(0), Clock(1))),

            // Read back from text. Nothing is raised: an optional is what says the text did
            // not read, and text that does not read is the ordinary case rather than a fault.
            //
            // Invariant here too, so a value written on one machine reads on another. The
            // second form takes exactly the pattern given, which is how something written by
            // a pattern is read back by the same one.
            BuiltInId.DateTimeParse =>
                new StrongBox<object?>(CompassMoments.ParseMomentUntyped(Text(0))),
            BuiltInId.DateTimeParseExact =>
                new StrongBox<object?>(CompassMoments.ParseMomentExactlyUntyped(Text(0), Text(1))),

            BuiltInId.TimeSpanParse =>
                new StrongBox<object?>(CompassMoments.ParseSpanUntyped(Text(0))),
            BuiltInId.TimeSpanParseExact =>
                new StrongBox<object?>(CompassMoments.ParseSpanExactlyUntyped(Text(0), Text(1))),

            BuiltInId.DateParse => new StrongBox<object?>(CompassMoments.ParseDateUntyped(Text(0))),
            BuiltInId.DateParseExact =>
                new StrongBox<object?>(CompassMoments.ParseDateExactlyUntyped(Text(0), Text(1))),

            BuiltInId.TimeParse => new StrongBox<object?>(CompassMoments.ParseTimeUntyped(Text(0))),
            BuiltInId.TimeParseExact =>
                new StrongBox<object?>(CompassMoments.ParseTimeExactlyUntyped(Text(0), Text(1))),

            // Written by a pattern. Invariant, as everything else here is: a program prints
            // the same on every machine, and the pattern is what says otherwise.
            //
            // A pattern the runtime cannot read raises a FormatException, which is already the
            // one a Compass program catches — the two are the same type — so nothing has to
            // translate it.
            BuiltInId.IntegerFormat =>
                new StrongBox<object?>(CompassText.Format(AsInteger(target), Text(0))),
            BuiltInId.RealFormat =>
                new StrongBox<object?>(CompassText.Format(target is decimal r ? r : 0, Text(0))),
            BuiltInId.FloatFormat =>
                new StrongBox<object?>(CompassText.Format(target is double f ? f : 0, Text(0))),
            BuiltInId.FractionFormat => new StrongBox<object?>(
                CompassText.Format(((Fraction)target!).ToReal(), Text(0))),
            BuiltInId.DateTimeFormat =>
                new StrongBox<object?>(CompassMoments.Format(Moment(), Text(0))),
            BuiltInId.TimeSpanFormat =>
                new StrongBox<object?>(CompassMoments.Format(Length(), Text(0))),
            BuiltInId.DateFormat =>
                new StrongBox<object?>(CompassMoments.Format(Day(), Text(0))),
            BuiltInId.TimeFormat =>
                new StrongBox<object?>(CompassMoments.Format(OnTheClock(), Text(0))),

            BuiltInId.FractionToReal => new StrongBox<object?>(((Fraction)target!).ToReal()),
            BuiltInId.FractionReciprocal =>
                new StrongBox<object?>(((Fraction)target!).Reciprocal()),
            BuiltInId.FractionNumerator =>
                new StrongBox<object?>(((Fraction)target!).Numerator),
            BuiltInId.FractionDenominator =>
                new StrongBox<object?>(((Fraction)target!).Denominator),
            BuiltInId.FloatToFraction => new StrongBox<object?>(
                Fraction.FromFloat(target is double f ? f : 0)),
            BuiltInId.RealToFloat => new StrongBox<object?>(
                CompassArithmetic.ToFloat(target is decimal toward ? toward : 0)),
            BuiltInId.FloatToReal => new StrongBox<object?>(
                CompassArithmetic.ToReal(target is double back ? back : 0)),
            BuiltInId.IntegerToFloat => new StrongBox<object?>((double)AsInteger(target)),
            BuiltInId.FractionToFloat => new StrongBox<object?>(((Fraction)target!).ToFloat()),
            BuiltInId.EnumerationToInteger => new StrongBox<object?>(
                target is EnumValue enumeration ? enumeration.Ordinal : AsInteger(target)),

            // A declared exception carries its message on the instance; one the language
            // raises is a .NET exception and carries its own.
            BuiltInId.ExceptionMessage => new StrongBox<object?>(target switch
            {
                Instance instance => instance.Message ?? string.Empty,
                Exception error => error.Message,
                _ => string.Empty,
            }),

            BuiltInId.ModelToString =>
                new StrongBox<object?>(ModelOperations.ToDisplayString(target)),
            BuiltInId.ModelEquals =>
                new StrongBox<object?>(ModelOperations.DeepEquals(target, Argument(0))),
        };
    }
#pragma warning restore CS8524

    /// <summary>Runs something that produces no value, and reports that it produced none.</summary>
    private static StrongBox<object?> Then(Action action)
    {
        action();
        return new StrongBox<object?>(null);
    }

    private static string AsText(object? value) => ModelOperations.ToDisplayString(value);

}

/// <summary>
/// Wraps a result so that "produced nothing" can be told apart from "did not handle this".
/// </summary>
internal sealed class StrongBox<T>(T value)
{
    public T Value { get; } = value;
}
