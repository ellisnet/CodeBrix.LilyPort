// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Globalization;
using System.Numerics;

namespace CodeBrix.LilyPort.Importers; //was previously: python's `fractions' module, as musicxml2ly.py uses it;

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port.

/// <summary>
/// python's <c>fractions.Fraction</c>, in the slice <c>musicxml2ly</c> is written
/// against.
/// </summary>
/// <remarks>
/// MusicXML measures durations in divisions of a quarter note and LilyPond writes them
/// as note values, so every length in the converter is an EXACT rational and stays one
/// through a chain of additions and multiplications before it is asked what note value
/// it is. Upstream gets that from the standard library; a double would answer
/// <c>3/4 + 1/6</c> with something that is not <c>11/12</c>, and the note that came out
/// would be wrong rather than merely imprecise.
/// <para>
/// Arbitrary precision, as python's is: a `<c>divisions</c>' of a million and a
/// hundred-part tuplet multiply out further than <see cref="long"/> reaches, and an
/// overflow would be silent.
/// </para>
/// </remarks>
internal readonly struct PythonFraction : IEquatable<PythonFraction>, IComparable<PythonFraction>
{
    /// <summary>Builds a fraction, reduced, with the sign on the numerator.</summary>
    /// <param name="numerator">The numerator.</param>
    /// <param name="denominator">The denominator.</param>
    internal PythonFraction(BigInteger numerator, BigInteger denominator)
    {
        if (denominator.IsZero)
        {
            throw new DivideByZeroException("Fraction(%s, 0)");
        }

        if (denominator.Sign < 0)
        {
            numerator = -numerator;
            denominator = -denominator;
        }

        BigInteger divisor = BigInteger.GreatestCommonDivisor(
            BigInteger.Abs(numerator), denominator);
        if (!divisor.IsOne && !divisor.IsZero)
        {
            numerator /= divisor;
            denominator /= divisor;
        }

        Numerator = numerator;
        Denominator = numerator.IsZero ? BigInteger.One : denominator;
    }

    /// <summary>Gets the numerator, carrying the sign.</summary>
    internal BigInteger Numerator { get; }

    /// <summary>Gets the denominator, always positive.</summary>
    internal BigInteger Denominator { get; }

    /// <summary>Zero.</summary>
    internal static readonly PythonFraction Zero = new PythonFraction(0, 1);

    /// <summary>One.</summary>
    internal static readonly PythonFraction One = new PythonFraction(1, 1);

    /// <summary>Builds a whole number.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The fraction.</returns>
    internal static PythonFraction FromLong(long value) => new PythonFraction(value, 1);

    /// <summary>
    /// python's <c>Fraction(float)</c> — the EXACT binary value, not an approximation
    /// of it.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <returns>The fraction.</returns>
    /// <remarks>
    /// ⚠ python is exact here and it matters: <c>Fraction(0.1)</c> is
    /// 3602879701896397/36028797018963968, not 1/10, and a converter that quietly
    /// rounded would disagree with upstream on any file whose <c>divisions</c> made a
    /// length land off a binary boundary. The doubles that reach this are whole or
    /// half numbers in practice, where the two readings agree.
    /// </remarks>
    internal static PythonFraction FromDouble(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            throw new OverflowException("cannot convert " + value.ToString(CultureInfo.InvariantCulture));
        }

        if (value == Math.Floor(value))
        {
            return new PythonFraction(new BigInteger(value), 1);
        }

        //The mantissa and exponent of the double itself, so the fraction IS the value.
        long bits = BitConverter.DoubleToInt64Bits(value);
        bool negative = bits < 0;
        int exponent = (int)((bits >> 52) & 0x7FF);
        long mantissa = bits & 0xFFFFFFFFFFFFFL;
        if (exponent == 0)
        {
            exponent++;
        }
        else
        {
            mantissa |= 1L << 52;
        }

        exponent -= 1075;
        BigInteger numerator = mantissa;
        BigInteger denominator = BigInteger.One;
        if (exponent > 0)
        {
            numerator <<= exponent;
        }
        else
        {
            denominator <<= -exponent;
        }

        return new PythonFraction(negative ? -numerator : numerator, denominator);
    }

    /// <summary>Adds.</summary>
    /// <param name="a">The left operand.</param>
    /// <param name="b">The right operand.</param>
    /// <returns>The sum.</returns>
    public static PythonFraction operator +(PythonFraction a, PythonFraction b)
        => new PythonFraction(
            (a.Numerator * b.Denominator) + (b.Numerator * a.Denominator),
            a.Denominator * b.Denominator);

    /// <summary>Subtracts.</summary>
    /// <param name="a">The left operand.</param>
    /// <param name="b">The right operand.</param>
    /// <returns>The difference.</returns>
    public static PythonFraction operator -(PythonFraction a, PythonFraction b)
        => new PythonFraction(
            (a.Numerator * b.Denominator) - (b.Numerator * a.Denominator),
            a.Denominator * b.Denominator);

    /// <summary>Negates.</summary>
    /// <param name="a">The operand.</param>
    /// <returns>The negation.</returns>
    public static PythonFraction operator -(PythonFraction a)
        => new PythonFraction(-a.Numerator, a.Denominator);

    /// <summary>Multiplies.</summary>
    /// <param name="a">The left operand.</param>
    /// <param name="b">The right operand.</param>
    /// <returns>The product.</returns>
    public static PythonFraction operator *(PythonFraction a, PythonFraction b)
        => new PythonFraction(a.Numerator * b.Numerator, a.Denominator * b.Denominator);

    /// <summary>Divides.</summary>
    /// <param name="a">The left operand.</param>
    /// <param name="b">The right operand.</param>
    /// <returns>The quotient.</returns>
    public static PythonFraction operator /(PythonFraction a, PythonFraction b)
        => new PythonFraction(a.Numerator * b.Denominator, a.Denominator * b.Numerator);

    /// <summary>Compares.</summary>
    /// <param name="a">The left operand.</param>
    /// <param name="b">The right operand.</param>
    /// <returns>Whether the left is smaller.</returns>
    public static bool operator <(PythonFraction a, PythonFraction b) => a.CompareTo(b) < 0;

    /// <summary>Compares.</summary>
    /// <param name="a">The left operand.</param>
    /// <param name="b">The right operand.</param>
    /// <returns>Whether the left is bigger.</returns>
    public static bool operator >(PythonFraction a, PythonFraction b) => a.CompareTo(b) > 0;

    /// <summary>Compares.</summary>
    /// <param name="a">The left operand.</param>
    /// <param name="b">The right operand.</param>
    /// <returns>Whether the left is no bigger.</returns>
    public static bool operator <=(PythonFraction a, PythonFraction b) => a.CompareTo(b) <= 0;

    /// <summary>Compares.</summary>
    /// <param name="a">The left operand.</param>
    /// <param name="b">The right operand.</param>
    /// <returns>Whether the left is no smaller.</returns>
    public static bool operator >=(PythonFraction a, PythonFraction b) => a.CompareTo(b) >= 0;

    /// <summary>Compares for equality.</summary>
    /// <param name="a">The left operand.</param>
    /// <param name="b">The right operand.</param>
    /// <returns>Whether the two are equal.</returns>
    public static bool operator ==(PythonFraction a, PythonFraction b) => a.Equals(b);

    /// <summary>Compares for inequality.</summary>
    /// <param name="a">The left operand.</param>
    /// <param name="b">The right operand.</param>
    /// <returns>Whether the two differ.</returns>
    public static bool operator !=(PythonFraction a, PythonFraction b) => !a.Equals(b);

    /// <summary>Compares for equality.</summary>
    /// <param name="other">The other fraction.</param>
    /// <returns>Whether the two are equal.</returns>
    public bool Equals(PythonFraction other)
        => Numerator == other.Numerator && Denominator == other.Denominator;

    /// <summary>Compares for equality.</summary>
    /// <param name="obj">The other object.</param>
    /// <returns>Whether the two are equal.</returns>
    public override bool Equals(object obj)
        => obj is PythonFraction other && Equals(other);

    /// <summary>Hashes.</summary>
    /// <returns>The hash.</returns>
    public override int GetHashCode()
        => Numerator.GetHashCode() ^ Denominator.GetHashCode();

    /// <summary>Orders.</summary>
    /// <param name="other">The other fraction.</param>
    /// <returns>The ordering.</returns>
    public int CompareTo(PythonFraction other)
        => (Numerator * other.Denominator).CompareTo(other.Numerator * Denominator);

    /// <summary>Gets whether this is a whole number.</summary>
    internal bool IsWhole => Denominator.IsOne;

    /// <summary>Gets whether this is zero.</summary>
    internal bool IsZero => Numerator.IsZero;

    /// <summary>Gets the sign.</summary>
    internal int Sign => Numerator.Sign;

    /// <summary>python's <c>int(f)</c> — truncation TOWARD ZERO, not flooring.</summary>
    /// <returns>The whole part.</returns>
    internal int ToInt() => (int)(Numerator / Denominator);

    /// <summary>python's <c>int(f)</c>, where the value may not fit an int.</summary>
    /// <returns>The whole part.</returns>
    internal long ToLong() => (long)(Numerator / Denominator);

    /// <summary>python's <c>float(f)</c>.</summary>
    /// <returns>The value.</returns>
    internal double ToDouble() => (double)Numerator / (double)Denominator;

    /// <summary>python's <c>f // g</c> — FLOOR division, giving a whole number.</summary>
    /// <param name="divisor">The divisor.</param>
    /// <returns>The floor of the quotient.</returns>
    internal BigInteger FloorDivide(PythonFraction divisor)
    {
        BigInteger n = Numerator * divisor.Denominator;
        BigInteger d = Denominator * divisor.Numerator;
        if (d.Sign < 0)
        {
            n = -n;
            d = -d;
        }

        BigInteger q = BigInteger.DivRem(n, d, out BigInteger remainder);
        //⚠ .NET truncates toward zero and python floors; they differ by one exactly
        //when the division does not come out even and the signs disagree.
        if (remainder.Sign < 0)
        {
            q -= 1;
        }

        return q;
    }

    /// <summary>python's <c>abs(f)</c>.</summary>
    /// <returns>The magnitude.</returns>
    internal PythonFraction Abs()
        => Numerator.Sign < 0 ? new PythonFraction(-Numerator, Denominator) : this;

    /// <summary>
    /// python's <c>str(Fraction)</c>: a whole number prints bare, anything else as
    /// <c>n/d</c>.
    /// </summary>
    /// <returns>The text.</returns>
    public override string ToString()
        => Denominator.IsOne
            ? Numerator.ToString(CultureInfo.InvariantCulture)
            : Numerator.ToString(CultureInfo.InvariantCulture)
              + "/" + Denominator.ToString(CultureInfo.InvariantCulture);
}
