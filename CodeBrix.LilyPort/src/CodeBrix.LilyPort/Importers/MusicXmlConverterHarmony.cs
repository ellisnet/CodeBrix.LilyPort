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
using System.Linq;

namespace CodeBrix.LilyPort.Importers; //was previously: scripts/musicxml2ly.py (the chord-name, fretboard, frame, figured-bass and lyric builders);

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port.

internal sealed partial class MusicXmlConverter
{
    private static readonly Dictionary<string, string> ChordKindDict
        = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "major", string.Empty },
            { "minor", ":m" },
            { "augmented", ":aug" },
            { "diminished", ":dim" },
            //Sevenths:
            { "dominant", ":7" },
            { "dominant-seventh", ":7" },
            { "major-seventh", ":maj7" },
            { "minor-seventh", ":m7" },
            { "diminished-seventh", ":dim7" },
            { "augmented-seventh", ":aug7" },
            { "half-diminished", ":m7.5-" },
            { "major-minor", ":maj7m" },
            //Sixths:
            { "major-sixth", ":6" },
            { "minor-sixth", ":m6" },
            //Ninths:
            { "dominant-ninth", ":9" },
            { "major-ninth", ":maj9" },
            { "minor-ninth", ":m9" },
            //11ths (usually as the basis for alteration):
            { "dominant-11th", ":11" },
            { "major-11th", ":maj11" },
            { "minor-11th", ":m11" },
            //13ths (usually as the basis for alteration):
            { "dominant-13th", ":13.11" },
            { "major-13th", ":maj13.11" },
            { "minor-13th", ":m13" },
            //Suspended:
            { "suspended-second", ":sus2" },
            { "suspended-fourth", ":sus4" },
            //Functional sixths: TODO (upstream's)
            //e.g., f-as-des; N6; D flat/F
            { "Neapolitan", "unsupported" },
            //e.g., as-c-(c)-fis; It+6; A flat 7
            { "Italian", "unsupported" },
            //e.g., as-c-d-fis; Fr+6; D7(flat 5)/A flat
            { "French", "unsupported" },
            //e.g., as-c-es-fis; Ger+6; enh A flat 7
            { "German", "unsupported" },
            //Other:
            //pedal-point bass; no symbol exists, needs text
            { "pedal", "unsupported" },
            { "power", ":1.5" },
            //e.g., d-as-c-f; Tr; Dm7(flat 5)
            { "Tristan", "unsupported" },
            { "other", ":1" },
            { "none", null },
        };

    private static readonly Dictionary<string, string> FiguredBassSuffixDict
        = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "sharp", "+" },
            { "flat", "-" },
            { "natural", "!" },
            { "double-sharp", "++" },
            { "flat-flat", "--" },
            { "sharp-sharp", "++" },
            { "slash", "/" },
        };

    /// <summary>Turns a chord's root or bass element into an output-side pitch.</summary>
    /// <param name="mxlChordPitch">The element.</param>
    /// <returns>The pitch.</returns>
    internal LilyChordPitch MusicXmlChordPitchToLily(MusicXmlChordPitch mxlChordPitch)
    {
        LilyChordPitch r = new LilyChordPitch(State);
        r.Alteration = mxlChordPitch.GetAlteration();
        r.Step = MusicXmlConversion.MusicXmlStepToLily(mxlChordPitch.GetStep()).Value;
        return r;
    }

    /// <summary>Which LilyPond chord modifier a MusicXML chord kind asks for.</summary>
    /// <param name="kind">The kind.</param>
    /// <returns>The modifier, or null when the kind cannot be written.</returns>
    internal string MusicXmlChordKindToLily(string kind)
    {
        string res = kind != null && ChordKindDict.TryGetValue(kind, out string found)
            ? found
            : "unknown";
        if (res == "unsupported")
        {
            State.Warning("Chord type '" + kind + "' is not supported.");
            return null;
        }

        if (res == "unknown")
        {
            State.Warning("Unknown chord type '" + kind + "'");
            return null;
        }

        return res;
    }

    /// <summary>The guitar string tunings a fretboard is read against.</summary>
    /// <param name="lines">How many strings the instrument has.</param>
    /// <returns>The tunings, highest string first.</returns>
    /// <remarks>
    /// ⚠ THE MEMO IS PART OF THE SPECIFICATION, AND IT IS ALSO AN UPSTREAM DEFECT. The
    /// test is whether the list EXISTS, not whether it is long enough, so the first frame
    /// the document carries fixes the length for every later one; a four-string diagram
    /// seen first makes a later six-string diagram index past the end. That is MEASURED:
    /// upstream crashes on <c>71c-ChordsFrets.xml --fb</c> with an IndexError and writes
    /// no file at all. Reproduced here, and recorded in
    /// tools/musicxml2lyprobe/DIVERGENCES.txt; the fix goes on top of a green parity
    /// baseline, not into the port that establishes it.
    /// </remarks>
    internal List<LilyPitch> MusicXmlGetStringTunings(int lines)
    {
        if (State.StringTunings == null)
        {
            if (lines == 0)
            {
                lines = 6;
            }

            //⚠ python's `[Pitch()] * lines' repeats ONE object's reference; every slot is
            //then overwritten below, so the sharing never reaches the output.
            LilyPitch shared = new LilyPitch(State);
            List<LilyPitch> tunings = Enumerable.Repeat(shared, lines).ToList();
            string[] names = { "E", "A", "D", "G", "B" };
            for (int i = 0; i < lines; i++)
            {
                LilyPitch p = new LilyPitch(State);
                p.Step = MusicXmlConversion.MusicXmlStepToLily(names[i % 5]).Value;
                //⚠ python's `2 * (x / 5)' is TRUE division, so the octave is a float.
                p.Octave = -2 + (i % 5 > 1 ? 1 : 0) + (2 * ((double)i / 5));
                p.Alteration = 0;
                p.ForceAbsolutePitch = true;
                tunings[i] = p;
            }

            tunings.Reverse();
            State.StringTunings = tunings;
        }

        return State.StringTunings
            .Take(Math.Min(lines, State.StringTunings.Count))
            .ToList();
    }

    /// <summary>Turns a frame element into an output-side fret diagram.</summary>
    /// <param name="frame">The element.</param>
    /// <returns>The event.</returns>
    internal LilyFretEvent MusicXmlFrameToLilyEvent(MusicXmlFrame frame)
    {
        LilyFretEvent ev = new LilyFretEvent(State);
        ev.Strings = frame.GetStrings();
        ev.Frets = frame.GetFrets();
        List<int> barre = new List<int>();
        List<int> openStrings = Enumerable.Range(1, ev.Strings).ToList();
        foreach (MusicXmlNode child in frame.GetNamedChildren("frame-note"))
        {
            MusicXmlFrameNote frameNote = (MusicXmlFrameNote)child;
            int fret = frameNote.GetFret();
            LilyFretElement el = fret <= 0
                ? new LilyFretElement(frameNote.GetStringNumber(), "o")
                : new LilyFretElement(frameNote.GetStringNumber(), fret);
            int fingering = frameNote.GetFingering();
            if (fingering >= 0)
            {
                el.SetFingering(fingering.ToString(CultureInfo.InvariantCulture));
            }

            ev.Elements.Add(el);
            if (!openStrings.Remove(frameNote.GetStringNumber()))
            {
                //python's `list.remove' raises when the value is absent; nothing catches
                //it, so the script ends without writing a file.
                throw new ImportAbortedException(
                    "frame note names string "
                    + frameNote.GetStringNumber().ToString(CultureInfo.InvariantCulture)
                    + " twice");
            }

            string b = frameNote.GetBarre();
            if (b == "start")
            {
                //Start string, then fret.
                barre.Add(el.StringNumber);
                barre.Add(el.FretText != null ? 0 : el.FretNumber);
            }
            else if (b == "stop")
            {
                //End string.
                barre.Insert(1, el.StringNumber);
            }
        }

        foreach (int stringNumber in openStrings)
        {
            ev.Elements.Add(new LilyFretElement(stringNumber, "x"));
        }

        //⚠ python sorts the LISTS elementwise; every entry's first member is a distinct
        //string number, so the comparison never reaches the second member — which is just
        //as well, since one entry's is an integer and another's is a string.
        ev.Elements.Sort((first, second) => first.StringNumber.CompareTo(second.StringNumber));
        ev.Elements.Reverse();
        if (barre.Count > 0)
        {
            ev.Barre = barre;
        }

        return ev;
    }

    /// <summary>Turns a harmony element into the fret diagrams it asks for.</summary>
    /// <param name="n">The element.</param>
    /// <returns>The events.</returns>
    internal List<LilyMusic> MusicXmlHarmonyToLily(MusicXmlNode n)
    {
        List<LilyMusic> res = new List<LilyMusic>();
        foreach (MusicXmlNode frame in n.GetNamedChildren("frame"))
        {
            LilyFretEvent ev = MusicXmlFrameToLilyEvent((MusicXmlFrame)frame);
            if (ev != null)
            {
                res.Add(ev);
            }
        }

        return res;
    }

    /// <summary>Turns a harmony element into the fretboard chord it asks for.</summary>
    /// <param name="n">The element.</param>
    /// <returns>The events.</returns>
    internal List<LilyMusic> MusicXmlHarmonyToLilyFretboards(MusicXmlNode n)
    {
        List<LilyMusic> res = new List<LilyMusic>();
        MusicXmlNode frame = n.GetMaybeExistNamedChild("frame");
        if (frame != null)
        {
            int strings = ((MusicXmlFrame)frame).GetStrings();
            if (strings == 0)
            {
                strings = 6;
            }

            List<LilyPitch> tunings = MusicXmlGetStringTunings(strings);
            LilyFretBoardEvent ev = new LilyFretBoardEvent(State);
            foreach (MusicXmlNode child in frame.GetNamedChildren("frame-note"))
            {
                MusicXmlFrameNote frameNote = (MusicXmlFrameNote)child;
                LilyFretBoardNote fbn = new LilyFretBoardNote(State);
                int stringNumber = frameNote.GetStringNumber();
                fbn.StringNumber = stringNumber.ToString(CultureInfo.InvariantCulture);
                int fingering = frameNote.GetFingering();
                if (fingering >= 0)
                {
                    fbn.Fingering = fingering.ToString(CultureInfo.InvariantCulture);
                }

                //⚠ AN UPSTREAM DEFECT, REPRODUCED, and the one this converter's own
                //corpus MEASURES (D64(a)): the memo in MusicXmlGetStringTunings tests
                //only whether the list EXISTS, so a four-string diagram seen first fixes
                //it at four entries and this read runs off the end. Upstream dies here
                //with `IndexError: list index out of range' and writes no file; the
                //fixture for `71c-ChordsFrets.xml --fb' records exactly that. See
                //tools/musicxml2lyprobe/DIVERGENCES.txt, candidate 1; the fix goes on top
                //of a green parity baseline, not into the port that establishes it.
                if (stringNumber - 1 >= tunings.Count || stringNumber - 1 < 0)
                {
                    throw ImportAbortedException.PythonTraceback(
                        "IndexError: list index out of range");
                }

                LilyPitch p = tunings[stringNumber - 1].Copy();
                p.AddSemitones(frameNote.GetFret());
                fbn.Pitch = p;
                ev.Append(fbn);
            }

            res.Add(ev);
        }

        return res;
    }

    /// <summary>Turns a harmony element into the chord name it asks for.</summary>
    /// <param name="n">The element.</param>
    /// <returns>The events.</returns>
    internal List<LilyMusic> MusicXmlHarmonyToLilyChordName(MusicXmlNode n)
    {
        List<LilyMusic> res = new List<LilyMusic>();
        MusicXmlNode root = n.GetMaybeExistNamedChild("root");
        if (root == null)
        {
            return res;
        }

        LilyChordNameEvent ev = new LilyChordNameEvent(State);
        ev.Root = MusicXmlChordPitchToLily((MusicXmlChordPitch)root);
        MusicXmlNode kind = n.GetMaybeExistNamedChild("kind");
        if (kind != null)
        {
            ev.Kind = MusicXmlChordKindToLily(kind.GetText());
            if (ev.Kind == null)
            {
                return res;
            }
        }

        MusicXmlNode bass = n.GetMaybeExistNamedChild("bass");
        if (bass != null)
        {
            ev.Bass = MusicXmlChordPitchToLily((MusicXmlChordPitch)bass);
        }

        MusicXmlNode inversion = n.GetMaybeExistNamedChild("inversion");
        if (inversion != null)
        {
            //TODO (upstream's): LilyPond does not support inversions, does it?
            //
            //Mail from Carl Sorensen on lilypond-devel, June 11, 2008:
            //4. LilyPond supports the first inversion in the form of added bass notes. So
            //the first inversion of C major would be c:/g. To get the second inversion of
            //C major, you would need to do e:6-3-^5 or e:m6-^5. However, both of these
            //techniques require you to know the chord and calculate either the fifth pitch
            //(for the first inversion) or the third pitch (for the second inversion) so
            //they may not be helpful for musicxml2ly.
            int inversionCount = int.Parse(
                inversion.GetText().Trim(), CultureInfo.InvariantCulture);
            if (inversionCount == 1)
            {
                //TODO (upstream's): Calculate the bass note for the inversion...
            }
        }

        foreach (MusicXmlNode degree in n.GetNamedChildren("degree"))
        {
            MusicXmlChordModification typed = (MusicXmlChordModification)degree;
            LilyChordModification d = new LilyChordModification(State);
            d.Type = typed.GetModificationType();
            d.Step = typed.GetValue();
            d.Alteration = typed.GetAlter();
            ev.AddModification(d);
        }

        //TODO (upstream's): convert the user-symbols attribute:
        //  major: a triangle, like Unicode 25B3
        //  minor: -, like Unicode 002D
        //  augmented: +, like Unicode 002B
        //  diminished: (degree), like Unicode 00B0
        //  half-diminished: (o with slash), like Unicode 00F8
        if (ev.Root != null)
        {
            res.Add(ev);
        }

        return res;
    }

    /// <summary>Turns a figure element into an output-side figured-bass note.</summary>
    /// <param name="n">The element.</param>
    /// <returns>The note.</returns>
    internal LilyFiguredBassNote MusicXmlFiguredBassNoteToLily(MusicXmlNode n)
    {
        LilyFiguredBassNote res = new LilyFiguredBassNote(State);
        MusicXmlNode prefix = n.GetMaybeExistNamedChild("prefix");
        if (prefix != null)
        {
            res.SetPrefix(
                FiguredBassSuffixDict.TryGetValue(prefix.GetText(), out string mapped)
                    ? mapped
                    : string.Empty);
        }

        MusicXmlNode number = n.GetMaybeExistNamedChild("figure-number");
        if (number != null)
        {
            res.SetNumber(number.GetText());
        }

        MusicXmlNode suffix = n.GetMaybeExistNamedChild("suffix");
        if (suffix != null)
        {
            res.SetSuffix(
                FiguredBassSuffixDict.TryGetValue(suffix.GetText(), out string mapped)
                    ? mapped
                    : string.Empty);
        }

        if (n.GetMaybeExistNamedChild("extend") != null)
        {
            //TODO (upstream's): Implement extender lines (unfortunately, in lilypond you
            //have to use \set useBassFigureExtenders = ##t, which turns them on globally,
            //while MusicXML has a property for each note... I'm not sure there is a proper
            //way to implement this cleanly.
        }

        return res;
    }

    /// <summary>Turns a figured-bass element into an output-side event.</summary>
    /// <param name="n">The element.</param>
    /// <returns>The event, or null when the element is not a figured bass.</returns>
    internal LilyFiguredBassEvent MusicXmlFiguredBassToLily(MusicXmlNode n)
    {
        if (!(n is MusicXmlFiguredBass figuredBass))
        {
            return null;
        }

        LilyFiguredBassEvent res = new LilyFiguredBassEvent(State);
        foreach (MusicXmlNode figure in n.GetNamedChildren("figure"))
        {
            LilyFiguredBassNote note = MusicXmlFiguredBassNoteToLily(figure);
            if (note != null)
            {
                res.Append(note);
            }
        }

        object duration = n.Get("duration");
        if (duration != null)
        {
            //Apply the duration to the event.
            PythonFraction length =
                new PythonFraction(ToBigInteger(duration), figuredBass.Divisions)
                * new PythonFraction(1, 4);
            res.SetRealDuration(length);
            res.SetDuration(LilyDuration.FromFraction(State, length));
        }

        if (n.Attribute("parentheses") == "yes")
        {
            res.SetParentheses(true);
        }

        return res;
    }

    /// <summary>python's <c>int</c> of a demarshalled duration.</summary>
    /// <param name="value">The value the schema map produced.</param>
    /// <returns>The value.</returns>
    private static System.Numerics.BigInteger ToBigInteger(object value)
        => value switch
        {
            System.Numerics.BigInteger big => big,
            int number => number,
            long number => number,
            _ => System.Numerics.BigInteger.Parse(
                System.Convert.ToString(value, CultureInfo.InvariantCulture),
                CultureInfo.InvariantCulture),
        };

    /// <summary>Turns a lyric element into the text LilyPond writes for it.</summary>
    /// <param name="lyrics">The element.</param>
    /// <returns>The text, with its hyphen or extender when it has one.</returns>
    /// <remarks>TODO (upstream's): Handle <c>print-object</c>.</remarks>
    internal string MusicXmlLyricsToText(MusicXmlNode lyrics)
    {
        bool continued = false;
        MusicXmlNode extended = null;

        string lyricColor = lyrics.Attribute("color");

        string text = string.Empty;

        bool needMarkup = false;
        foreach (MusicXmlNode e in lyrics.GetAllChildren())
        {
            if ((e is MusicXmlText || e is MusicXmlElision) && e.AttributeDict.Count > 0)
            {
                needMarkup = true;
                break;
            }
        }

        if (needMarkup)
        {
            //Prepare input for the markup builder.
            List<LilyMarkupElement> textElements = new List<LilyMarkupElement>();

            foreach (MusicXmlNode e in lyrics.GetAllChildren())
            {
                if (e is MusicXmlSyllabic syllabic)
                {
                    continued = syllabic.Continued();
                }
                else if (e is MusicXmlText)
                {
                    Dictionary<string, object> a = LilyMarkupElement.CopyAttributes(e);
                    if (!a.ContainsKey("color") && !string.IsNullOrEmpty(lyricColor))
                    {
                        a["color"] = lyricColor;
                    }

                    MusicXmlWords w = new MusicXmlWords { State = State };
                    //Convert soft hyphens to normal hyphens.
                    w.Data = e.GetText().Replace('\u00AD', '-');

                    textElements.Add(new LilyMarkupElement(w, a));
                }
                else if (e is MusicXmlElision)
                {
                    if (textElements.Count > 0)
                    {
                        //U+203F UNDERTIE
                        if (e.GetText() == "\u203F")
                        {
                            Dictionary<string, object> a = LilyMarkupElement.CopyAttributes(e);
                            if (!a.ContainsKey("color") && !string.IsNullOrEmpty(lyricColor))
                            {
                                a["color"] = lyricColor;
                            }

                            //LilyPond's support for `~' being replaced by a special
                            //undertie only works with text strings, not with markups.
                            //Theoretically, we could implement the undertie construction
                            //in `musicxml2ly', but...
                            MusicXmlWords w = new MusicXmlWords { State = State };
                            w.Data = "\u203F";

                            textElements.Add(new LilyMarkupElement(w, a));
                        }
                        else
                        {
                            textElements[textElements.Count - 1].Element.Data += " ";
                        }
                    }

                    continued = false;
                }
                else if (e is MusicXmlExtend)
                {
                    //If present it is the last element in `<lyric>'.
                    extended = e;
                    break;
                }
            }

            text = LilyMarkup.TextToLy(State, textElements);
        }
        else
        {
            foreach (MusicXmlNode e in lyrics.GetAllChildren())
            {
                if (e is MusicXmlSyllabic syllabic)
                {
                    continued = syllabic.Continued();
                }
                else if (e is MusicXmlText)
                {
                    //Convert soft hyphens to normal hyphens.
                    text += e.GetText().Replace('\u00AD', '-');
                }
                else if (e is MusicXmlElision)
                {
                    if (text.Length > 0)
                    {
                        //U+203F UNDERTIE
                        text += e.GetText() == "\u203F" ? "~" : " ";
                    }

                    continued = false;
                }
                else if (e is MusicXmlExtend)
                {
                    //If present it is the last element in `<lyric>'.
                    extended = e;
                    break;
                }
            }

            MusicXmlWords w = new MusicXmlWords { State = State };
            w.Data = text;
            Dictionary<string, object> a = new Dictionary<string, object>();
            if (!string.IsNullOrEmpty(lyricColor))
            {
                a["color"] = lyricColor;
                needMarkup = true;
            }

            text = LilyMarkup.TextToLy(
                State, new List<LilyMarkupElement> { new LilyMarkupElement(w, a) });
        }

        //MusicXML 4.0 doesn't provide a way to change the appearance of hyphens between
        //syllables.
        string hyphen = "--";
        string extend = "__";

        if (extended != null)
        {
            //Ignore 'continue' and 'stop' types (also for backward compatibility with
            //MusicXML 3.0).
            string type = extended.Attribute("type");
            if (type == "continue" || type == "stop")
            {
                extended = null;
            }
            else
            {
                string extendColor = extended.Attribute("color", lyricColor);
                string color = LilyMarkup.ColorToLy(extendColor);
                if (!string.IsNullOrEmpty(color))
                {
                    extend = "\\tweak color " + color + " __";
                }
            }
        }

        //Using `-' and `_' as 'text markers' in addition to MusicXML elements to indicate
        //hyphens and extender lines for melismata, respectively, is neither necessary nor
        //covered by the standard. However, it doesn't harm to emit more `--' or `__'
        //elements just in case since LilyPond simply ignores them.
        if (text == "\"-\"" && continued)
        {
            return hyphen;
        }

        if (text == "\"_\"" && extended != null)
        {
            return extend;
        }

        if (needMarkup && text.Length > 0)
        {
            text = "\\markup " + text;
        }

        if (continued && text.Length > 0)
        {
            return " " + text + " " + hyphen;
        }

        if (continued)
        {
            return hyphen;
        }

        if (extended != null && text.Length > 0)
        {
            return " " + text + " " + extend;
        }

        if (extended != null)
        {
            return extend;
        }

        return text.Length > 0 ? " " + text : string.Empty;
    }
}
