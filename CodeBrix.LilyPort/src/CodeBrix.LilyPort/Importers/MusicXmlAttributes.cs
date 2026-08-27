/*
   This file is part of LilyPond, the GNU music typesetter.

   Copyright (C) 2005--2026 Han-Wen Nienhuys <hanwen@xs4all.nl>,
   Copyright (C) 2007--2026 Reinhold Kainhofer <reinhold@kainhofer.com>

   LilyPond is free software: you can redistribute it and/or modify
   it under the terms of the GNU General Public License as published by
   the Free Software Foundation, either version 3 of the License, or
   (at your option) any later version.

   LilyPond is distributed in the hope that it will be useful,
   but WITHOUT ANY WARRANTY; without even the implied warranty of
   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
   GNU General Public License for more details.

   You should have received a copy of the GNU General Public License
   along with LilyPond.  If not, see <http://www.gnu.org/licenses/>.
*/

using System;
using System.Collections.Generic;
using System.Globalization;

namespace CodeBrix.LilyPort.Importers; //was previously: python/musicxml.py (Attributes and Barline);

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port.

/// <summary>
/// A time signature, in whichever of the five shapes MusicXML allows.
/// </summary>
/// <remarks>
/// Upstream returns one python value for all five: the integer 0 for an empty
/// senza-misura signature, the string 'X' for an X-shaped one, a flat list of beats
/// ending in the beat type for an ordinary or complex signature, and a list of such
/// lists for a compound of them. C# cannot hold that in one type, so the five readings
/// are named here and every caller asks which one it has rather than testing a shape.
/// </remarks>
internal sealed class MusicXmlTimeSignature
{
    /// <summary>What kind of signature this is.</summary>
    internal enum SignatureKind
    {
        /// <summary>An empty senza-misura signature, upstream's integer 0.</summary>
        SenzaMisuraEmpty,

        /// <summary>An X-shaped senza-misura signature, upstream's string 'X'.</summary>
        SenzaMisuraCross,

        /// <summary>A simple or complex signature: beats, then the beat type.</summary>
        Simple,

        /// <summary>A compound of complex signatures.</summary>
        Compound,
    }

    /// <summary>Builds a senza-misura signature.</summary>
    /// <param name="kind">Which of the two senza-misura readings this is.</param>
    internal MusicXmlTimeSignature(SignatureKind kind) => Kind = kind;

    /// <summary>Builds a simple or complex signature.</summary>
    /// <param name="beats">The beats, with the beat type last.</param>
    internal MusicXmlTimeSignature(List<int> beats)
    {
        Kind = SignatureKind.Simple;
        Beats = beats;
    }

    /// <summary>Builds a compound signature.</summary>
    /// <param name="parts">The signatures it is made of.</param>
    internal MusicXmlTimeSignature(List<List<int>> parts)
    {
        Kind = SignatureKind.Compound;
        Parts = parts;
    }

    /// <summary>What kind of signature this is.</summary>
    internal SignatureKind Kind { get; }

    /// <summary>The beats, with the beat type last; null unless simple.</summary>
    internal List<int> Beats { get; }

    /// <summary>The parts; null unless compound.</summary>
    internal List<List<int>> Parts { get; }

    /// <summary>python's <c>list.copy</c> of whatever this signature is held as.</summary>
    /// <returns>The copy.</returns>
    /// <remarks>
    /// ⚠ SHALLOW, exactly as upstream's is: a compound signature's copy SHARES its inner
    /// lists with this one, so a caller that shifts the copy's durations shifts the
    /// original's too. That aliasing is what reaches the cached signature upstream, and
    /// it is reproduced rather than tidied away.
    /// </remarks>
    internal MusicXmlTimeSignature ShallowCopy()
        => Kind == SignatureKind.Compound
            ? new MusicXmlTimeSignature(new List<List<int>>(Parts))
            : new MusicXmlTimeSignature(new List<int>(Beats));
}

/// <summary>The attributes element.</summary>
internal sealed class MusicXmlAttributes : MusicXmlMeasureElement
{
    private readonly Dictionary<string, MusicXmlNode> _dict
        = new Dictionary<string, MusicXmlNode>();

    private MusicXmlTimeSignature _timeSignatureCache;

    /// <summary>The attributes element this one was read from, before splitting.</summary>
    internal MusicXmlAttributes OriginalTag { get; set; }

    /// <summary>Carries forward what an earlier attributes element established.</summary>
    /// <param name="values">What was in force.</param>
    internal void SetAttributesFromPrevious(Dictionary<string, MusicXmlNode> values)
    {
        foreach (KeyValuePair<string, MusicXmlNode> entry in values)
        {
            _dict[entry.Key] = entry.Value;
        }
    }

    /// <summary>Reads this element's own children into the running set.</summary>
    internal void ReadSelf()
    {
        foreach (MusicXmlNode child in GetAllChildren())
        {
            _dict[child.GetName()] = child;
        }
    }

    /// <summary>Everything in force at this point.</summary>
    internal Dictionary<string, MusicXmlNode> Dict => _dict;

    /// <summary>One attribute in force at this point.</summary>
    /// <param name="name">The element name.</param>
    /// <returns>The element, or null.</returns>
    internal MusicXmlNode GetNamedAttribute(string name)
        => _dict.TryGetValue(name, out MusicXmlNode value) ? value : null;

    private static PythonFraction SingleTimeSigToFraction(List<int> sig)
    {
        if (sig.Count < 2)
        {
            return PythonFraction.Zero;
        }

        int n = 0;
        for (int i = 0; i < sig.Count - 1; i++)
        {
            n += sig[i];
        }

        return new PythonFraction(n, sig[sig.Count - 1]);
    }

    /// <summary>How long a measure under this signature is.</summary>
    /// <returns>The length, or -1 for senza misura.</returns>
    internal PythonFraction GetMeasureLength()
    {
        MusicXmlTimeSignature sig = GetTimeSignature();
        if (sig != null
            && sig.Kind == MusicXmlTimeSignature.SignatureKind.SenzaMisuraEmpty)
        {
            return PythonFraction.FromLong(-1);
        }

        if (sig == null
            || (sig.Kind == MusicXmlTimeSignature.SignatureKind.Simple
                && sig.Beats.Count == 0))
        {
            return PythonFraction.One;
        }

        if (sig.Kind == MusicXmlTimeSignature.SignatureKind.Compound)
        {
            //Complex time signature.
            PythonFraction length = PythonFraction.Zero;
            foreach (List<int> part in sig.Parts)
            {
                length += SingleTimeSigToFraction(part);
            }

            return length;
        }

        if (sig.Kind == MusicXmlTimeSignature.SignatureKind.SenzaMisuraCross)
        {
            //Upstream reaches `len(sig)' on the string 'X', which is 1, so the
            //`len(sig) == 0' test is false and the value falls through to
            //`single_time_sig_to_fraction('X')' -- whose own `len(sig) < 2' test is
            //then true, giving 0.
            return PythonFraction.Zero;
        }

        //Simple (maybe compound) time signature of the form `[beat, ..., type]'.
        return SingleTimeSigToFraction(sig.Beats);
    }

    /// <summary>Reads a signature out of a time-shaped element.</summary>
    /// <param name="mxl">The element.</param>
    /// <returns>The signature.</returns>
    internal MusicXmlTimeSignature GetSignature(MusicXmlNode mxl)
    {
        List<List<int>> signature = new List<List<int>>();
        List<int> currentSig = new List<int>();
        foreach (MusicXmlNode child in mxl.GetAllChildren())
        {
            if (child is MusicXmlBeats)
            {
                currentSig = new List<int>();
                foreach (string beat in child.GetText().Trim().Split('+'))
                {
                    currentSig.Add(int.Parse(beat, CultureInfo.InvariantCulture));
                }
            }
            else if (child is MusicXmlBeatType)
            {
                currentSig.Add(int.Parse(child.GetText(), CultureInfo.InvariantCulture));
                signature.Add(currentSig);
                currentSig = new List<int>();
            }
        }

        if (signature.Count == 0)
        {
            //Upstream indexes `signature[0]' here, which raises IndexError on an
            //empty list -- and IndexError is not one of the two exceptions the
            //caller catches, so the script ends without writing a file.
            throw new ImportAbortedException("list index out of range");
        }

        return signature.Count == 1
            ? new MusicXmlTimeSignature(signature[0])
            : new MusicXmlTimeSignature(signature);
    }

    /// <summary>The time signature in force.</summary>
    /// <returns>The signature, or null when the document names none.</returns>
    /// <remarks>
    /// ⚠ THE CACHE IS PART OF THE SPECIFICATION, not an optimisation: upstream only
    /// fills it on the paths that succeed, so a signature that fell back to 4/4 is
    /// recomputed (and re-reported) on every read.
    /// </remarks>
    internal MusicXmlTimeSignature GetTimeSignature()
    {
        if (_timeSignatureCache != null)
        {
            return _timeSignatureCache;
        }

        try
        {
            MusicXmlNode mxl = GetNamedAttribute("time");
            if (mxl == null)
            {
                return null;
            }

            MusicXmlNode senza = mxl.GetMaybeExistNamedChild("senza-misura");
            if (senza != null)
            {
                if (senza.GetText() == "X")
                {
                    _timeSignatureCache = new MusicXmlTimeSignature(
                        MusicXmlTimeSignature.SignatureKind.SenzaMisuraCross);
                    return _timeSignatureCache;
                }

                _timeSignatureCache = new MusicXmlTimeSignature(
                    MusicXmlTimeSignature.SignatureKind.SenzaMisuraEmpty);
                return _timeSignatureCache;
            }

            _timeSignatureCache = GetSignature(mxl);
            return _timeSignatureCache;
        }
        catch (Exception exception)
            when (exception is KeyNotFoundException || exception is FormatException
                  || exception is OverflowException)
        {
            Message("Unable to interpret time signature! Falling back to 4/4.");
            return new MusicXmlTimeSignature(new List<int> { 4, 4 });
        }
    }

    /// <summary>The interchangeable signature, if the document names one.</summary>
    /// <returns>The signature, or null.</returns>
    internal MusicXmlTimeSignature GetAlternateTimeSignature()
    {
        try
        {
            MusicXmlNode mxl = GetNamedAttribute("time");
            if (mxl == null)
            {
                return null;
            }

            MusicXmlNode alternate = mxl.GetMaybeExistNamedChild("interchangeable");
            return alternate == null ? null : GetSignature(alternate);
        }
        catch (Exception exception)
            when (exception is KeyNotFoundException || exception is FormatException
                  || exception is OverflowException)
        {
            Message("Unable to interpret <interchangeable> element, ignoring.");
            return null;
        }
    }

    /// <summary>How the interchangeable signature relates to the main one.</summary>
    /// <returns>The relation, or null.</returns>
    internal string GetAlternateTimeSignatureStyle()
    {
        MusicXmlNode mxl = GetNamedAttribute("time");
        if (mxl == null)
        {
            return null;
        }

        MusicXmlNode alternate = mxl.GetMaybeExistNamedChild("interchangeable");
        if (alternate == null)
        {
            return null;
        }

        MusicXmlNode style = alternate.GetMaybeExistNamedChild("time-relation");
        return style == null ? "parentheses" : style.GetText();
    }

    /// <summary>What the clef element says, read out into its six values.</summary>
    /// <returns>The clef information, or null when there is no clef element.</returns>
    internal MusicXmlClefInfo GetClefInformation()
    {
        MusicXmlNode mxl = GetMaybeExistNamedChild("clef");
        if (mxl == null)
        {
            return null;
        }

        MusicXmlClefInfo info = new MusicXmlClefInfo();
        MusicXmlNode sign = mxl.GetMaybeExistNamedChild("sign");
        if (sign != null)
        {
            info.Sign = sign.GetText();
        }

        MusicXmlNode line = mxl.GetMaybeExistNamedChild("line");
        if (line != null)
        {
            info.Line = int.Parse(line.GetText(), CultureInfo.InvariantCulture);
        }

        MusicXmlNode octave = mxl.GetMaybeExistNamedChild("clef-octave-change");
        info.OctaveChange = octave != null
            ? int.Parse(octave.GetText(), CultureInfo.InvariantCulture)
            : 0;

        info.Color = mxl.Attribute("color");
        info.FontSize = mxl.Attribute("font-size");
        info.PrintObject = mxl.Attribute("print-object", "yes") == "yes";
        return info;
    }

    /// <summary>What the key element says.</summary>
    /// <returns>The key signature, or null when there is no key element.</returns>
    internal MusicXmlKeyInfo GetKeySignature()
    {
        MusicXmlNode key = GetNamedAttribute("key");
        if (key == null)
        {
            return null;
        }

        MusicXmlKeyInfo info = new MusicXmlKeyInfo();
        info.Color = key.Attribute("color");
        info.FontSize = key.Attribute("font-size");
        info.Visible = key.Attribute("print-object", "yes") == "yes";

        MusicXmlNode fifthsElement = key.GetMaybeExistNamedChild("fifths");
        if (fifthsElement != null)
        {
            MusicXmlNode modeNode = key.GetMaybeExistNamedChild("mode");
            string mode = modeNode?.GetText();
            if (string.IsNullOrEmpty(mode) || mode == "none")
            {
                mode = "major";
            }

            //TODO: Shall we try to convert the key-octave, too?
            info.Fifths = int.Parse(fifthsElement.GetText(), CultureInfo.InvariantCulture);
            info.Mode = mode;
        }
        else
        {
            List<MusicXmlKeyAlteration> alterations = new List<MusicXmlKeyAlteration>();
            string currentStep = "0";
            foreach (MusicXmlNode child in key.GetAllChildren())
            {
                if (child is MusicXmlKeyStep)
                {
                    currentStep = child.GetText().Trim();
                }
                else if (child is MusicXmlKeyAlter)
                {
                    alterations.Add(new MusicXmlKeyAlteration
                    {
                        Step = currentStep,
                        Alter = MusicXmlUtilities.InterpretAlterElement(child),
                    });
                }
                else if (child is MusicXmlKeyOctave)
                {
                    int nr = int.Parse(
                        child.Attribute("number", "-1"), CultureInfo.InvariantCulture);
                    if (nr > 0 && nr <= alterations.Count)
                    {
                        //MusicXML Octave 4 is middle C -> shift to 0
                        alterations[nr - 1].Octave =
                            int.Parse(child.GetText(), CultureInfo.InvariantCulture) - 4;
                    }
                    else
                    {
                        child.Message(
                            "Key alteration octave given for a non-existing alteration nr. "
                            + nr.ToString(CultureInfo.InvariantCulture)
                            + ", available numbers: "
                            + alterations.Count.ToString(CultureInfo.InvariantCulture)
                            + "!");
                    }
                }
            }

            info.Alterations = alterations;
        }

        return info;
    }

    /// <summary>What the key element says about cancelling the previous key.</summary>
    /// <returns>The count and where it is drawn, or null.</returns>
    internal (int Cancel, string Location)? GetCancellation()
    {
        MusicXmlNode key = GetNamedAttribute("key");
        if (key == null)
        {
            return null;
        }

        MusicXmlNode cancelElement = key.GetMaybeExistNamedChild("cancel");
        if (cancelElement != null)
        {
            int cancel = int.Parse(cancelElement.GetText(), CultureInfo.InvariantCulture);
            string location = cancelElement.Attribute("location", "left");
            return (cancel, location);
        }

        return null;
    }

    /// <summary>What the transpose element says.</summary>
    /// <returns>The element, or null.</returns>
    internal MusicXmlNode GetTransposition() => GetNamedAttribute("transpose");
}

/// <summary>What a clef element says.</summary>
/// <remarks>
/// Upstream returns a six-element list; the names are this port's, and the ORDER of
/// that list is what PORT-COVERAGE records as replaced.
/// </remarks>
internal sealed class MusicXmlClefInfo
{
    /// <summary>The clef letter.</summary>
    internal string Sign { get; set; }

    /// <summary>Which staff line the clef sits on.</summary>
    internal int? Line { get; set; }

    /// <summary>How many octaves the clef transposes by.</summary>
    internal int OctaveChange { get; set; }

    /// <summary>The colour.</summary>
    internal string Color { get; set; }

    /// <summary>The font size.</summary>
    internal string FontSize { get; set; }

    /// <summary>Whether the clef is drawn at all.</summary>
    internal bool PrintObject { get; set; } = true;
}

/// <summary>One alteration of a non-traditional key signature.</summary>
internal sealed class MusicXmlKeyAlteration
{
    /// <summary>Which step is altered.</summary>
    internal string Step { get; set; }

    /// <summary>By how much.</summary>
    internal double Alter { get; set; }

    /// <summary>In which octave, when the document says.</summary>
    internal int? Octave { get; set; }
}

/// <summary>What a key element says.</summary>
/// <remarks>
/// Upstream returns a four-tuple whose first member is EITHER a (fifths, mode) pair
/// OR an alterations list. The two readings are named apart here;
/// <see cref="IsTraditional"/> is the test that replaces upstream's shape test.
/// </remarks>
internal sealed class MusicXmlKeyInfo
{
    /// <summary>How many sharps or flats, when the key is a traditional one.</summary>
    internal int? Fifths { get; set; }

    /// <summary>The mode, when the key is a traditional one.</summary>
    internal string Mode { get; set; }

    /// <summary>The alterations, when the key is not a traditional one.</summary>
    internal List<MusicXmlKeyAlteration> Alterations { get; set; }

    /// <summary>Whether this key is named in the Circle of Fifths.</summary>
    internal bool IsTraditional => Fifths.HasValue;

    /// <summary>The colour.</summary>
    internal string Color { get; set; }

    /// <summary>The font size.</summary>
    internal string FontSize { get; set; }

    /// <summary>Whether the signature is drawn at all.</summary>
    internal bool Visible { get; set; } = true;
}

/// <summary>What a barline element became.</summary>
/// <remarks>
/// Upstream returns a list of two lists: the markers in the order bar line, backward
/// marker, forward marker; and the fermata elements attached to the bar line. The two
/// are named apart here.
/// </remarks>
internal sealed class MusicXmlBarlineResult
{
    /// <summary>The bar line and its markers, in the order they must be emitted.</summary>
    internal List<LilyExpression> Markers { get; } = new List<LilyExpression>();

    /// <summary>The fermatas attached to the bar line, at most two.</summary>
    internal List<MusicXmlNode> Fermatas { get; set; } = new List<MusicXmlNode>();
}

/// <summary>The barline element.</summary>
internal sealed class MusicXmlBarline : MusicXmlMeasureElement
{
    /// <summary>Turns this into the output-side bar line and its markers.</summary>
    /// <returns>The result.</returns>
    /// <remarks>
    /// The bar-line element comes BEFORE the backward marker so that it gets included
    /// into the repeat group (in <c>GroupRepeats</c>) and can control a repeat bar
    /// line's attributes.
    /// </remarks>
    internal MusicXmlBarlineResult ToLilyObject()
    {
        LilyExpression barLine = null;
        MusicXmlEndingMarker forwardEnding = null;
        MusicXmlRepeatMarker backwardRepeat = null;
        MusicXmlRepeatMarker forwardRepeat = null;
        MusicXmlEndingMarker backwardEnding = null;

        MusicXmlNode bartypeElement = GetMaybeExistNamedChild("bar-style");
        MusicXmlNode repeatElement = GetMaybeExistNamedChild("repeat");
        MusicXmlNode endingElement = GetMaybeExistNamedChild("ending");
        List<MusicXmlNode> fermataElements = GetNamedChildren("fermata");

        string bartype = null;
        string barlineColor = null;
        if (bartypeElement != null)
        {
            bartype = bartypeElement.GetText();
            barlineColor = bartypeElement.Attribute("color");
        }

        string direction = repeatElement?.Attribute("direction");
        if (direction != null)
        {
            MusicXmlRepeatMarker repeat = new MusicXmlRepeatMarker(State);
            repeat.Direction = direction == "forward" ? -1 : direction == "backward" ? 1 : 0;
            repeat.AtStart = repeat.Direction == -1
                             && When.HasValue && When.Value.IsZero;

            //The MusicXML standard has issues with specifying the type for repeats,
            //especially for back-to-back repeats; we do what Finale does, namely to
            //ignore the bar type for start and end repeats, setting them to
            //'thick-thin' and 'thin-thick', respectively. Since this is LilyPond's
            //default, we don't have to do anything.
            //
            //Similarly, the default back-to-back repeat type for Finale is
            //'thin-thick-thin', and we set up LilyPond to use this as the default,
            //too. The only case we actually handle is 'heavy-heavy'.
            bartype = bartype == "heavy-heavy" ? "dots-heavy-heavy-dots" : null;

            string times = repeatElement.Attribute("times");
            if (times != null
                && int.TryParse(times, NumberStyles.Integer, CultureInfo.InvariantCulture,
                                out int parsedTimes))
            {
                repeat.Times = parsedTimes;
            }

            if (repeat.Direction == -1)
            {
                forwardRepeat = repeat;
            }
            else
            {
                backwardRepeat = repeat;
            }
        }

        string endingType = endingElement?.Attribute("type");
        if (endingType != null)
        {
            MusicXmlEndingMarker ending = new MusicXmlEndingMarker(State);
            ending.Direction = endingType == "start" ? -1
                : endingType == "stop" || endingType == "discontinue" ? 1
                : 0;
            ending.MxlEvent = endingElement;

            string endingNumber = endingElement.Attribute("number");
            if (endingNumber != null)
            {
                ending.Volte = MusicXmlConversion.MusicXmlNumbersToVolte(endingNumber);
            }

            if (ending.Direction == -1)
            {
                backwardEnding = ending;
            }
            else
            {
                forwardEnding = ending;
            }
        }

        if (bartype != null || barlineColor != null)
        {
            LilyBarLine bar = new LilyBarLine(State);
            bar.BarType = bartype;
            bar.Color = barlineColor;
            barLine = bar;
        }

        //Synthesize RepeatEndingMarker objects if possible.
        MusicXmlMarker backward = backwardRepeat;
        if (backwardRepeat != null && forwardEnding != null)
        {
            backward = new MusicXmlRepeatEndingMarker(State, backwardRepeat, forwardEnding);
            forwardEnding = null;
        }

        MusicXmlMarker forward = forwardRepeat;
        if (forwardRepeat != null && backwardEnding != null)
        {
            forward = new MusicXmlRepeatEndingMarker(State, forwardRepeat, backwardEnding);
            backwardEnding = null;
        }

        MusicXmlBarlineResult result = new MusicXmlBarlineResult();
        foreach (LilyExpression item in new LilyExpression[]
                 { barLine, forwardEnding, backward, forward, backwardEnding })
        {
            if (item != null)
            {
                result.Markers.Add(item);
            }
        }

        result.Fermatas = fermataElements.Count > 2
            ? fermataElements.GetRange(0, 2)
            : fermataElements;
        return result;
    }
}
