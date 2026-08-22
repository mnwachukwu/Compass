using System.Reflection;
using System.Reflection.Emit;
using Compass.Compiler.Ast;
using Compass.Compiler.Semantics;
using Compass.Runtime;

namespace Compass.Compiler.Emit;

/// <summary>
/// <para>The types the language provides that hold something rather than describe it: a moment, a
/// day, a time of day, a length of time, a generator, and the two ways to reach a disk.</para>
/// <para><b>Every one is a call into the runtime, and never the framework member of the same
/// name.</b> <c>DateTime.Year</c> answers in 32 bits where a Compass <c>integer</c> is 64;
/// <c>AddDays</c> asks for binary floating point where a <c>real</c> counts in tens; a listing
/// comes back in whatever order the file system felt like. Each of those is a place two engines
/// could quietly part company, and <see cref="CompassMoments"/> and <see cref="CompassFiles"/> are
/// where the answer is decided once.</para>
/// <para><b>The shape is uniform because the runtime was written for it.</b> Every method is
/// static and takes what it works on as its first argument, so emitting one is: push the receiver
/// if there is one, push the arguments, call. Nothing here converts anything — that was the point
/// of putting the crossings in the runtime.</para>
/// </summary>
public sealed partial class CilEmitter
{
    /// <summary>
    /// One member of a provided type: the value it was written on, the arguments, then the call.
    /// </summary>
    private void EmitProvidedMember(
        MemberExpr member,
        IReadOnlyList<Expression> arguments,
        BuiltInId id)
    {
        // A generator is the one shape here that is an object rather than a value, and the one
        // reachable two ways: through a program's own, or through the name, which means the one
        // the runtime keeps.
        if (CilBuiltIns.IsOnAGenerator(id))
        {
            if (IsThroughATypeName(member.Receiver))
            {
                _il.Emit(OpCodes.Call, SharedGenerator);
            }
            else
            {
                EmitExpression(member.Receiver);
            }

            EmitArguments(arguments);
            _il.Emit(OpCodes.Callvirt, GeneratorMethod(id));

            return;
        }

        // Everything else is static. A member reached through a type name — 'DateTime.Now',
        // 'File.Read(path)' — has no value in front of it to push.
        if (!IsThroughATypeName(member.Receiver))
        {
            EmitExpression(member.Receiver);
        }

        EmitArguments(arguments);
        _il.Emit(OpCodes.Call, ProvidedMethod(id));
    }

    /// <summary>
    /// <para>Makes one: a moment, a day, a time of day, a length, or a generator.</para>
    /// <para>Every one but the generator goes through a runtime factory rather than a
    /// constructor, because the thirty-first of February has to be refused in the language's
    /// words — the platform names a parameter, which tells a reader nothing about the date they
    /// wrote.</para>
    /// </summary>
    private void EmitProvidedConstruction(NewExpr construction, BuiltInId id)
    {
        EmitArguments(construction.Arguments);

        switch (id)
        {
            case BuiltInId.RandomNew:
                _il.Emit(OpCodes.Newobj, GeneratorFromNothing);
                return;

            case BuiltInId.RandomNewSeeded:
                _il.Emit(OpCodes.Newobj, GeneratorFromSeed);
                return;

            default:
                _il.Emit(OpCodes.Call, ProvidedMethod(id));
                return;
        }
    }

    /// <summary>
    /// <para>The runtime method behind one member.</para>
    /// <para>Named here rather than at each use because several share a name and are told apart
    /// by what they take — <c>Format</c> has four forms and <c>MakeSpan</c> and <c>MakeTime</c>
    /// two each — so choosing the overload is one question answered in one place.</para>
    /// </summary>
    private static MethodInfo ProvidedMethod(BuiltInId id) => id switch
    {
        // ---- Making one ---------------------------------------------------------------------
        BuiltInId.DateTimeNewDate => Moment(nameof(CompassMoments.MakeDay), L, L, L),
        BuiltInId.DateTimeNewMoment => Moment(nameof(CompassMoments.MakeMoment), L, L, L, L, L, L),
        BuiltInId.DateNew => Moment(nameof(CompassMoments.MakeDate), L, L, L),
        BuiltInId.TimeNewToMinute => Moment(nameof(CompassMoments.MakeTime), L, L),
        BuiltInId.TimeNewToSecond => Moment(nameof(CompassMoments.MakeTime), L, L, L),
        BuiltInId.TimeSpanNewTime => Moment(nameof(CompassMoments.MakeSpan), L, L, L),
        BuiltInId.TimeSpanNewSpan => Moment(nameof(CompassMoments.MakeSpan), L, L, L, L),

        // ---- A moment -----------------------------------------------------------------------
        BuiltInId.DateTimeNow => Moment(nameof(CompassMoments.Now)),
        BuiltInId.DateTimeToday => Moment(nameof(CompassMoments.Today)),

        BuiltInId.DateTimeYear => Moment(nameof(CompassMoments.Year), M),
        BuiltInId.DateTimeMonth => Moment(nameof(CompassMoments.Month), M),
        BuiltInId.DateTimeDay => Moment(nameof(CompassMoments.Day), M),
        BuiltInId.DateTimeHour => Moment(nameof(CompassMoments.Hour), M),
        BuiltInId.DateTimeMinute => Moment(nameof(CompassMoments.Minute), M),
        BuiltInId.DateTimeSecond => Moment(nameof(CompassMoments.Second), M),
        BuiltInId.DateTimeDayOfWeek => Moment(nameof(CompassMoments.DayOfWeek), M),
        BuiltInId.DateTimeDayOfYear => Moment(nameof(CompassMoments.DayOfYear), M),

        BuiltInId.DateTimeAddDays => Moment(nameof(CompassMoments.AddDays), M, R),
        BuiltInId.DateTimeAddHours => Moment(nameof(CompassMoments.AddHours), M, R),
        BuiltInId.DateTimeAddMinutes => Moment(nameof(CompassMoments.AddMinutes), M, R),
        BuiltInId.DateTimeAddSeconds => Moment(nameof(CompassMoments.AddSeconds), M, R),
        BuiltInId.DateTimeAddYears => Moment(nameof(CompassMoments.AddYears), M, L),
        BuiltInId.DateTimeAddMonths => Moment(nameof(CompassMoments.AddMonths), M, L),

        BuiltInId.DateTimeCompareTo => Moment(nameof(CompassMoments.CompareMoments), M, M),
        BuiltInId.DateTimeAdd => Moment(nameof(CompassMoments.Add), M, S),
        BuiltInId.DateTimeSubtract => Moment(nameof(CompassMoments.Subtract), M, M),
        BuiltInId.DateTimeSubtractSpan => Moment(nameof(CompassMoments.SubtractSpan), M, S),

        BuiltInId.DateTimeDatePart => Moment(nameof(CompassMoments.DatePart), M),
        BuiltInId.DateTimeTimePart => Moment(nameof(CompassMoments.TimePart), M),
        BuiltInId.DateTimeFromDate => Moment(nameof(CompassMoments.FromDate), D),
        BuiltInId.DateTimeFromDateAndTime => Moment(nameof(CompassMoments.FromDateAndTime), D, T),

        // ---- A length of time ---------------------------------------------------------------
        BuiltInId.TimeSpanZero => Moment(nameof(CompassMoments.Zero)),
        BuiltInId.TimeSpanFromDays => Moment(nameof(CompassMoments.FromDays), R),
        BuiltInId.TimeSpanFromHours => Moment(nameof(CompassMoments.FromHours), R),
        BuiltInId.TimeSpanFromMinutes => Moment(nameof(CompassMoments.FromMinutes), R),
        BuiltInId.TimeSpanFromSeconds => Moment(nameof(CompassMoments.FromSeconds), R),

        BuiltInId.TimeSpanDays => Moment(nameof(CompassMoments.Days), S),
        BuiltInId.TimeSpanHours => Moment(nameof(CompassMoments.Hours), S),
        BuiltInId.TimeSpanMinutes => Moment(nameof(CompassMoments.Minutes), S),
        BuiltInId.TimeSpanSeconds => Moment(nameof(CompassMoments.Seconds), S),

        BuiltInId.TimeSpanTotalDays => Moment(nameof(CompassMoments.TotalDays), S),
        BuiltInId.TimeSpanTotalHours => Moment(nameof(CompassMoments.TotalHours), S),
        BuiltInId.TimeSpanTotalMinutes => Moment(nameof(CompassMoments.TotalMinutes), S),
        BuiltInId.TimeSpanTotalSeconds => Moment(nameof(CompassMoments.TotalSeconds), S),

        BuiltInId.TimeSpanNegate => Moment(nameof(CompassMoments.Negate), S),
        BuiltInId.TimeSpanDuration => Moment(nameof(CompassMoments.Duration), S),
        BuiltInId.TimeSpanAdd => Moment(nameof(CompassMoments.AddSpan), S, S),
        BuiltInId.TimeSpanSubtract => Moment(nameof(CompassMoments.SubtractSpans), S, S),
        BuiltInId.TimeSpanCompareTo => Moment(nameof(CompassMoments.CompareSpans), S, S),

        // ---- A day --------------------------------------------------------------------------
        BuiltInId.DateToday => Moment(nameof(CompassMoments.TodayOnly)),
        BuiltInId.DateFromMoment => Moment(nameof(CompassMoments.DateFromMoment), M),

        BuiltInId.DateYear => Moment(nameof(CompassMoments.DateYear), D),
        BuiltInId.DateMonth => Moment(nameof(CompassMoments.DateMonth), D),
        BuiltInId.DateDay => Moment(nameof(CompassMoments.DateDay), D),
        BuiltInId.DateDayOfWeek => Moment(nameof(CompassMoments.DateDayOfWeek), D),
        BuiltInId.DateDayOfYear => Moment(nameof(CompassMoments.DateDayOfYear), D),

        BuiltInId.DateAddDays => Moment(nameof(CompassMoments.DateAddDays), D, L),
        BuiltInId.DateAddMonths => Moment(nameof(CompassMoments.DateAddMonths), D, L),
        BuiltInId.DateAddYears => Moment(nameof(CompassMoments.DateAddYears), D, L),
        BuiltInId.DateAtTime => Moment(nameof(CompassMoments.DateAtTime), D, T),
        BuiltInId.DateCompareTo => Moment(nameof(CompassMoments.CompareDates), D, D),

        // ---- A time of day ------------------------------------------------------------------
        BuiltInId.TimeNow => Moment(nameof(CompassMoments.TimeNow)),
        BuiltInId.TimeFromMoment => Moment(nameof(CompassMoments.TimeFromMoment), M),

        BuiltInId.TimeHour => Moment(nameof(CompassMoments.TimeHour), T),
        BuiltInId.TimeMinute => Moment(nameof(CompassMoments.TimeMinute), T),
        BuiltInId.TimeSecond => Moment(nameof(CompassMoments.TimeSecond), T),

        BuiltInId.TimeAddHours => Moment(nameof(CompassMoments.TimeAddHours), T, R),
        BuiltInId.TimeAddMinutes => Moment(nameof(CompassMoments.TimeAddMinutes), T, R),
        BuiltInId.TimeToTimeSpan => Moment(nameof(CompassMoments.TimeToSpan), T),
        BuiltInId.TimeCompareTo => Moment(nameof(CompassMoments.CompareTimes), T, T),

        // ---- Written by a pattern, and read back from text ----------------------------------
        BuiltInId.DateTimeFormat => Moment(nameof(CompassMoments.Format), M, X),
        BuiltInId.TimeSpanFormat => Moment(nameof(CompassMoments.Format), S, X),
        BuiltInId.DateFormat => Moment(nameof(CompassMoments.Format), D, X),
        BuiltInId.TimeFormat => Moment(nameof(CompassMoments.Format), T, X),

        BuiltInId.DateTimeParse => Moment(nameof(CompassMoments.ParseMoment), X),
        BuiltInId.DateTimeParseExact => Moment(nameof(CompassMoments.ParseMomentExactly), X, X),
        BuiltInId.TimeSpanParse => Moment(nameof(CompassMoments.ParseSpan), X),
        BuiltInId.TimeSpanParseExact => Moment(nameof(CompassMoments.ParseSpanExactly), X, X),
        BuiltInId.DateParse => Moment(nameof(CompassMoments.ParseDate), X),
        BuiltInId.DateParseExact => Moment(nameof(CompassMoments.ParseDateExactly), X, X),
        BuiltInId.TimeParse => Moment(nameof(CompassMoments.ParseTime), X),
        BuiltInId.TimeParseExact => Moment(nameof(CompassMoments.ParseTimeExactly), X, X),

        // ---- Files and folders --------------------------------------------------------------
        BuiltInId.FileRead => Files(nameof(CompassFiles.Read), X),
        BuiltInId.FileReadLines => Files(nameof(CompassFiles.ReadLines), X),
        BuiltInId.FileWrite => Files(nameof(CompassFiles.Write), X, X),
        BuiltInId.FileWriteLines => Files(nameof(CompassFiles.WriteLines), X, typeof(ICompassSet)),
        BuiltInId.FileAppend => Files(nameof(CompassFiles.Append), X, X),
        BuiltInId.FileExists => Files(nameof(CompassFiles.Exists), X),
        BuiltInId.FileDelete => Files(nameof(CompassFiles.Delete), X),
        BuiltInId.FileCopy => Files(nameof(CompassFiles.Copy), X, X),
        BuiltInId.FileMove => Files(nameof(CompassFiles.Move), X, X),
        BuiltInId.FileSize => Files(nameof(CompassFiles.Size), X),
        BuiltInId.FileChanged => Files(nameof(CompassFiles.Changed), X),

        BuiltInId.DirectoryCurrent => Files(nameof(CompassFiles.Current)),
        BuiltInId.DirectoryExists => Files(nameof(CompassFiles.FolderExists), X),
        BuiltInId.DirectoryCreate => Files(nameof(CompassFiles.CreateFolder), X),
        BuiltInId.DirectoryDelete => Files(nameof(CompassFiles.DeleteFolder), X),
        BuiltInId.DirectoryFiles => Files(nameof(CompassFiles.Files), X),
        BuiltInId.DirectoryFolders => Files(nameof(CompassFiles.Folders), X),

        _ => throw new InvalidOperationException($"No runtime method stands behind '{id}'."),
    };

    private static MethodInfo GeneratorMethod(BuiltInId id) => id switch
    {
        BuiltInId.RandomNext => Generator(nameof(CompassRandom.Next)),
        BuiltInId.RandomNextBelow => Generator(nameof(CompassRandom.Next), L),
        BuiltInId.RandomNextBetween => Generator(nameof(CompassRandom.Next), L, L),
        BuiltInId.RandomNextDouble => Generator(nameof(CompassRandom.NextReal)),

        _ => throw new InvalidOperationException($"No generator method stands behind '{id}'."),
    };

    // The types these are written in, short because the table above is long and reads better as
    // a shape than as prose. L is an integer, R a real, X a string.
    private static readonly Type L = typeof(long);
    private static readonly Type R = typeof(decimal);
    private static readonly Type X = typeof(string);
    private static readonly Type M = typeof(DateTime);
    private static readonly Type S = typeof(TimeSpan);
    private static readonly Type D = typeof(DateOnly);
    private static readonly Type T = typeof(TimeOnly);

    private static MethodInfo Moment(string name, params Type[] taking) =>
        typeof(CompassMoments).GetMethod(name, taking)
        ?? throw new InvalidOperationException($"The runtime has no '{name}' taking those.");

    private static MethodInfo Files(string name, params Type[] taking) =>
        typeof(CompassFiles).GetMethod(name, taking)
        ?? throw new InvalidOperationException($"The runtime has no '{name}' taking those.");

    private static MethodInfo Generator(string name, params Type[] taking) =>
        typeof(CompassRandom).GetMethod(name, taking)
        ?? throw new InvalidOperationException($"A generator has no '{name}' taking those.");

    private static readonly MethodInfo SharedGenerator =
        typeof(CompassRandom).GetProperty(nameof(CompassRandom.Shared))!.GetMethod!;

    private static readonly System.Reflection.ConstructorInfo GeneratorFromNothing =
        typeof(CompassRandom).GetConstructor(Type.EmptyTypes)!;

    private static readonly System.Reflection.ConstructorInfo GeneratorFromSeed =
        typeof(CompassRandom).GetConstructor([typeof(long)])!;
}
