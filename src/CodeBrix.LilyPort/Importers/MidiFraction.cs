// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Globalization;
using System.Numerics;
using System.Text;

namespace CodeBrix.LilyPort.Importers; //was previously: python's fractions.Fraction, as midi2ly.py uses it;

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port.

/// <summary>
/// An exact rational, doing for the MIDI import what python's <c>Fraction</c> does for
/// <c>midi2ly</c>.
/// </summary>
/// <remarks>
/// ⚠ THE EXACTNESS IS THE POINT, not a nicety. midi2ly decides whether to write a
/// tempo as a plain number or as a Scheme rational with a decimal comment by asking
/// whether the value's DENOMINATOR is one, and whether the rounded decimal it would
/// print is the same number; both questions have different answers in binary floating
/// point. Flower's own <c>Rational</c> is not used here — it carries a biased-by-one
/// denominator and an infinity, which are the engine's needs and not python's.
/// </remarks>
internal readonly struct MidiFraction : IEquatable<MidiFraction>
{
    internal MidiFraction(BigInteger numerator, BigInteger denominator)
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

        BigInteger g = BigInteger.GreatestCommonDivisor(
            BigInteger.Abs(numerator), denominator);
        if (!g.IsZero && !g.IsOne)
        {
            numerator /= g;
            denominator /= g;
        }

        Numerator = numerator;
        Denominator = denominator;
    }

    /// <summary>Gets the numerator, in lowest terms.</summary>
    internal BigInteger Numerator { get; }

    /// <summary>Gets the denominator, in lowest terms and always positive.</summary>
    internal BigInteger Denominator { get; }

    /// <summary>A whole number.</summary>
    /// <param name="value">The number.</param>
    /// <returns>The fraction.</returns>
    internal static MidiFraction FromLong(long value) => new MidiFraction(value, 1);

    /// <summary>python's <c>a / b</c> over two fractions.</summary>
    /// <param name="a">The dividend.</param>
    /// <param name="b">The divisor.</param>
    /// <returns>The quotient.</returns>
    public static MidiFraction operator /(MidiFraction a, MidiFraction b)
        => new MidiFraction(a.Numerator * b.Denominator, a.Denominator * b.Numerator);

    /// <summary>python's <c>a * b</c> over two fractions.</summary>
    /// <param name="a">The first factor.</param>
    /// <param name="b">The second factor.</param>
    /// <returns>The product.</returns>
    public static MidiFraction operator *(MidiFraction a, MidiFraction b)
        => new MidiFraction(a.Numerator * b.Numerator, a.Denominator * b.Denominator);

    /// <summary>python's <c>n * f</c>.</summary>
    /// <param name="n">The whole number.</param>
    /// <param name="f">The fraction.</param>
    /// <returns>The product.</returns>
    public static MidiFraction operator *(long n, MidiFraction f)
        => FromLong(n) * f;

    /// <summary>Whether one fraction is less than another.</summary>
    /// <param name="a">The first.</param>
    /// <param name="b">The second.</param>
    /// <returns>Whether it is less.</returns>
    public static bool operator <(MidiFraction a, MidiFraction b)
        => a.Numerator * b.Denominator < b.Numerator * a.Denominator;

    /// <summary>Whether one fraction is greater than another.</summary>
    /// <param name="a">The first.</param>
    /// <param name="b">The second.</param>
    /// <returns>Whether it is greater.</returns>
    public static bool operator >(MidiFraction a, MidiFraction b)
        => b < a;

    /// <summary>Whether two fractions are the same number.</summary>
    /// <param name="a">The first.</param>
    /// <param name="b">The second.</param>
    /// <returns>Whether they are equal.</returns>
    public static bool operator ==(MidiFraction a, MidiFraction b) => a.Equals(b);

    /// <summary>Whether two fractions are different numbers.</summary>
    /// <param name="a">The first.</param>
    /// <param name="b">The second.</param>
    /// <returns>Whether they differ.</returns>
    public static bool operator !=(MidiFraction a, MidiFraction b) => !a.Equals(b);

    /// <inheritdoc/>
    public bool Equals(MidiFraction other)
        => Numerator == other.Numerator && Denominator == other.Denominator;

    /// <inheritdoc/>
    public override bool Equals(object obj)
        => obj is MidiFraction other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode()
        => (Numerator, Denominator).GetHashCode();

    /// <summary>Gets whether this is a whole number.</summary>
    internal bool IsWhole => Denominator.IsOne;

    /// <summary>python's <c>float(f)</c>.</summary>
    /// <returns>The value.</returns>
    internal double ToDouble() => (double)Numerator / (double)Denominator;

    /// <summary>
    /// python's <c>str(Fraction)</c>: the numerator alone when the denominator is one.
    /// </summary>
    /// <returns>The text.</returns>
    public override string ToString()
        => IsWhole
            ? Numerator.ToString(CultureInfo.InvariantCulture)
            : Numerator.ToString(CultureInfo.InvariantCulture) + "/"
                + Denominator.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// python's <c>f == Decimal(text)</c> — an EXACT comparison against the number the
    /// text spells, not against the double it would round to.
    /// </summary>
    /// <param name="text">A decimal literal, as <see cref="FormatG"/> produced it.</param>
    /// <returns>Whether the two are the same number.</returns>
    internal bool EqualsDecimalText(string text)
    {
        MidiFraction? parsed = ParseDecimal(text);
        return parsed != null && Equals(parsed.Value);
    }

    /// <summary>Reads a decimal literal as an exact fraction, the way Decimal does.</summary>
    /// <param name="text">The literal.</param>
    /// <returns>The fraction, or null when the text is not one.</returns>
    private static MidiFraction? ParseDecimal(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        int exponent = 0;
        int e = text.IndexOfAny(new[] { 'e', 'E' });
        string mantissa = text;
        if (e >= 0)
        {
            mantissa = text.Substring(0, e);
            if (!int.TryParse(
                text.Substring(e + 1), NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture, out exponent))
            {
                return null;
            }
        }

        bool negative = mantissa.StartsWith("-", StringComparison.Ordinal);
        if (negative || mantissa.StartsWith("+", StringComparison.Ordinal))
        {
            mantissa = mantissa.Substring(1);
        }

        int dot = mantissa.IndexOf('.');
        string digits = mantissa;
        if (dot >= 0)
        {
            exponent -= mantissa.Length - dot - 1;
            digits = mantissa.Remove(dot, 1);
        }

        if (digits.Length == 0)
        {
            return null;
        }

        foreach (char c in digits)
        {
            if (c < '0' || c > '9')
            {
                return null;
            }
        }

        BigInteger value = BigInteger.Parse(digits, CultureInfo.InvariantCulture);
        if (negative)
        {
            value = -value;
        }

        return exponent >= 0
            ? new MidiFraction(value * BigInteger.Pow(10, exponent), 1)
            : new MidiFraction(value, BigInteger.Pow(10, -exponent));
    }

    /// <summary>
    /// python's <c>format(value, 'g')</c> — C's <c>%g</c> at six significant digits,
    /// with the trailing zeros and any bare point removed.
    /// </summary>
    /// <param name="value">The number.</param>
    /// <returns>The text.</returns>
    /// <remarks>
    /// .NET's own "G" format is NOT this: it switches to exponent notation on a
    /// different rule and spells the exponent differently. midi2ly puts this text in a
    /// comment AND compares the number back against it, so getting it wrong would
    /// change both the comment and the decision above it.
    /// </remarks>
    internal static string FormatG(double value)
    {
        if (double.IsNaN(value))
        {
            return "nan";
        }

        if (double.IsInfinity(value))
        {
            return value > 0 ? "inf" : "-inf";
        }

        const int precision = 6;
        if (value == 0.0)
        {
            return "0";
        }

        int exponent = (int)Math.Floor(Math.Log10(Math.Abs(value)));

        //%g rounds first and may carry the exponent, so read it back off the rounded
        //text rather than trusting the logarithm.
        string probe = Math.Abs(value).ToString(
            "E" + (precision - 1).ToString(CultureInfo.InvariantCulture),
            CultureInfo.InvariantCulture);
        int at = probe.IndexOf('E');
        if (at > 0 && int.TryParse(
            probe.Substring(at + 1), NumberStyles.AllowLeadingSign,
            CultureInfo.InvariantCulture, out int rounded))
        {
            exponent = rounded;
        }

        if (exponent < -4 || exponent >= precision)
        {
            string mantissa = Trim(probe.Substring(0, at));
            string sign = exponent < 0 ? "-" : "+";
            int magnitude = Math.Abs(exponent);
            return (value < 0 ? "-" : string.Empty) + mantissa + "e" + sign
                + magnitude.ToString("00", CultureInfo.InvariantCulture);
        }

        int decimals = Math.Max(0, precision - 1 - exponent);
        string fixedText = value.ToString(
            "F" + decimals.ToString(CultureInfo.InvariantCulture),
            CultureInfo.InvariantCulture);
        return Trim(fixedText);
    }

    /// <summary>Drops the trailing zeros, and the point if nothing follows it.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The trimmed text.</returns>
    private static string Trim(string text)
    {
        if (text.IndexOf('.') < 0)
        {
            return text;
        }

        StringBuilder trimmed = new StringBuilder(text.TrimEnd('0'));
        if (trimmed.Length > 0 && trimmed[trimmed.Length - 1] == '.')
        {
            trimmed.Length -= 1;
        }

        return trimmed.ToString();
    }
}
