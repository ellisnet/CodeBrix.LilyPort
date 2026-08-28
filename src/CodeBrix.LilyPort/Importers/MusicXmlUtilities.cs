/*
   This file is part of LilyPond, the GNU music typesetter.

   Copyright (C) 2016--2026 John Gourlay <john@weathervanefarm.net>

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
using System.Text;
using System.Text.RegularExpressions;
using CodeBrix.LilyPort.ConvertLy;

namespace CodeBrix.LilyPort.Importers; //was previously: python/utilities.py;

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port.

/// <summary>
/// The handful of helpers <c>musicxml2ly</c> shares between its modules.
/// </summary>
internal static class MusicXmlUtilities
{
    /// <summary>
    /// Quotes a string for LilyPond output, unless it is a bare word already.
    /// </summary>
    /// <param name="inputString">The text.</param>
    /// <returns>The text, quoted if it has to be.</returns>
    internal static string EscapeLyOutputString(string inputString)
    {
        string returnString = inputString;
        bool needsQuotes = !PythonRegex.MatchAt(
            "^[a-zA-ZäöüÜÄÖßñ]+$", returnString).Success;
        if (needsQuotes)
        {
            returnString = "\""
                + returnString.Replace("\\", "\\\\").Replace("\"", "\\\"")
                + "\"";
        }

        return returnString;
    }

    /// <summary>
    /// Reads an <c>&lt;alter&gt;</c>-shaped element's value.
    /// </summary>
    /// <param name="alterElement">The element, or null.</param>
    /// <returns>The alteration; zero when the element is absent.</returns>
    /// <remarks>
    /// ⚠ ONE DELIBERATE DIVERGENCE, AND IT IS A SAFETY ONE. Upstream reads this value
    /// with python's <c>eval</c>, so a document's text is EXECUTED as an expression;
    /// the port parses a number instead. Every alteration a valid MusicXML document can
    /// carry is a number, and the two readings agree on all of them — but the port
    /// would otherwise be handing an untrusted file a way to run code inside whatever
    /// application called it, which is not a behaviour worth being faithful to.
    /// Recorded in PORT-COVERAGE.
    /// </remarks>
    internal static double InterpretAlterElement(MusicXmlNode alterElement)
    {
        double alter = 0;
        if (alterElement != null)
        {
            string text = alterElement.GetText();
            if (double.TryParse(
                    text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
            {
                alter = value;
            }
            else
            {
                //Upstream's `eval' raises here and nothing catches it, so the script
                //ends without writing a file.
                throw new ImportAbortedException(
                    "cannot interpret alteration '" + text + "'");
            }
        }

        return alter;
    }

    /// <summary>
    /// The alteration an element carries, boxed the way python's <c>eval</c> leaves it.
    /// </summary>
    /// <param name="alterElement">The element, or null.</param>
    /// <returns>A <see cref="long"/> for an integer, a <see cref="double"/> otherwise.</returns>
    /// <remarks>
    /// ⚠ Needed only where the value is PRINTED rather than computed with: python's
    /// <c>eval("4")</c> is the integer 4 and prints as '4', while <c>eval("4.0")</c> is
    /// the float 4.0 and prints as '4.0'. The same safety divergence as
    /// <see cref="InterpretAlterElement"/> applies: the port parses rather than executes.
    /// </remarks>
    internal static object InterpretAlterElementBoxed(MusicXmlNode alterElement)
    {
        if (alterElement == null)
        {
            return 0L;
        }

        string text = alterElement.GetText().Trim();
        if (long.TryParse(
                text, NumberStyles.Integer | NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture, out long integer))
        {
            return integer;
        }

        if (double.TryParse(
                text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
        {
            return value;
        }

        //Upstream's `eval' raises here and nothing catches it, so the script ends without
        //writing a file.
        throw new ImportAbortedException("cannot interpret alteration '" + text + "'");
    }

    private static readonly Dictionary<string, int> DurationLogs
        = new Dictionary<string, int>
        {
            { "1024th", 10 },
            { "512th", 9 },
            { "256th", 8 },
            { "128th", 7 },
            { "64th", 6 },
            { "32nd", 5 },
            { "16th", 4 },
            { "eighth", 3 },
            { "quarter", 2 },
            { "half", 1 },
            { "whole", 0 },
            { "breve", -1 },
            { "longa", -2 }, //non-standard name
            { "long", -2 },
            { "maxima", -3 },
        };

    /// <summary>Turns a MusicXML note type into LilyPond's duration logarithm.</summary>
    /// <param name="duration">The type name.</param>
    /// <returns>The logarithm; zero for anything unrecognised.</returns>
    internal static int MusicXmlDurationToLog(string duration)
        => duration != null && DurationLogs.TryGetValue(duration, out int log) ? log : 0;

    /// <summary>Rounds to two decimal places.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The rounded value.</returns>
    /// <remarks>
    /// python's <c>round</c> is BANKER'S rounding — a half goes to the even neighbour,
    /// not away from zero — which is what <see cref="MidpointRounding.ToEven"/> is, and
    /// .NET's default. Spelled out because the two families disagree here and the
    /// difference reaches page margins.
    /// </remarks>
    internal static double RoundToTwoDigits(double value)
        => Math.Round(value * 100, MidpointRounding.ToEven) / 100;

    /// <summary>
    /// python's <c>round</c> for a whole number result: half to EVEN, not away from
    /// zero.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <returns>The rounded value.</returns>
    internal static double PythonRound(double value)
        => Math.Round(value, MidpointRounding.ToEven);

    /// <summary>
    /// Splits on whitespace, keeping double-quoted runs together.
    /// </summary>
    /// <param name="value">The text.</param>
    /// <returns>The pieces.</returns>
    /// <remarks>
    /// Only ASCII whitespace splits, mainly to preserve the non-breakable space
    /// character.
    /// <para>
    /// ⚠ THAT SENTENCE IS THE WHOLE REASON THE PATTERN IS NOT VERBATIM. Upstream writes
    /// <c>\S</c> under python's <c>(?a)</c> flag, which narrows it to the six ASCII
    /// whitespace characters; .NET's <c>\S</c> is Unicode-aware and WOULD split on
    /// U+00A0, which is exactly what upstream says it is preserving. The class is
    /// therefore spelled out. Every other part of the pattern is upstream's.
    /// </para>
    /// </remarks>
    internal static List<string> SplitStringAndPreserveDoublequotedSubstrings(string value)
        => PythonRegex.FindAll(
            "(?sx) (?: \" .*? [^\\\\] \" | [^ \\t\\n\\r\\f\\v] )+", value);

    /// <summary>python's <c>len(s)</c>: the number of CODE POINTS, not of UTF-16 units.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The length.</returns>
    /// <remarks>
    /// The output printer breaks lines at eighty, measured with python's <c>len</c>. A
    /// surrogate pair counts once there and twice in .NET, so a document carrying one
    /// would wrap at a different word.
    /// </remarks>
    internal static int PythonLength(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        int length = 0;
        for (int i = 0; i < text.Length; i++)
        {
            length++;
            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length
                && char.IsLowSurrogate(text[i + 1]))
            {
                i++;
            }
        }

        return length;
    }

    private static readonly Dictionary<string, string> MidiInstruments
        = new Dictionary<string, string>
        {
        { "brass.french-horn", "french horn" },
        { "brass.group", "brass section" },
        { "brass.group.synth", "synthbrass 1" },
        { "brass.trombone", "trombone" },
        { "brass.trombone.alto", "trombone" },
        { "brass.trombone.bass", "trombone" },
        { "brass.trombone.contrabass", "trombone" },
        { "brass.trombone.tenor", "trombone" },
        { "brass.trumpet", "trumpet" },
        { "brass.trumpet.baroque", "trumpet" },
        { "brass.trumpet.bass", "trumpet" },
        { "brass.trumpet.bflat", "trumpet" },
        { "brass.trumpet.c", "trumpet" },
        { "brass.trumpet.d", "trumpet" },
        { "brass.trumpet.piccolo", "trumpet" },
        { "brass.trumpet.pocket", "trumpet" },
        { "brass.trumpet.slide", "trumpet" },
        { "brass.trumpet.tenor", "trumpet" },
        { "brass.tuba", "tuba" },
        { "brass.tuba.bass", "tuba" },
        { "brass.tuba.subcontrabass", "tuba" },
        { "brass.wagner-tuba", "french horn" },
        { "drum.timpani", "timpani" },
        { "drum.tom-tom", "melodic tom" },
        { "drum.tom-tom.synth", "synth drum" },
        { "effect.applause", "applause" },
        { "effect.bass-string-slap", "slap bass 1" },
        { "effect.bird", "bird tweet" },
        { "effect.bird.tweet", "bird tweet" },
        { "effect.breath", "breath noise" },
        { "effect.guitar-fret", "guitar fret noise" },
        { "effect.gunshot", "gunshot" },
        { "effect.helicopter", "helicopter" },
        { "effect.metronome-click", "woodblock" },
        { "effect.rain", "fx1 (rain)" },
        { "effect.seashore", "seashore" },
        { "effect.telephone-ring", "telephone ring" },
        { "keyboard.accordion", "accordion" },
        { "keyboard.bandoneon", "accordion" },
        { "keyboard.celesta", "celesta" },
        { "keyboard.clavichord", "clav" },
        { "keyboard.concertina", "concertina" },
        { "keyboard.harpsichord", "harpsichord" },
        { "keyboard.ondes-martenot", "ocarina" },
        { "keyboard.organ", "church organ" },
        { "keyboard.organ.drawbar", "drawbar organ" },
        { "keyboard.organ.percussive", "percussive organ" },
        { "keyboard.organ.pipe", "church organ" },
        { "keyboard.organ.reed", "reed organ" },
        { "keyboard.piano", "acoustic grand" },
        { "keyboard.piano.electric", "electric piano 1" },
        { "keyboard.piano.grand", "acoustic grand" },
        { "keyboard.piano.honky-tonk", "honky-tonk" },
        { "metal.bells.agogo", "agogo" },
        { "metal.bells.tinklebell", "tinkle bell" },
        { "metal.cymbal.reverse", "reverse cymbal" },
        { "pitched-percussion.glockenspiel", "glockenspiel" },
        { "pitched-percussion.glockenspiel.alto", "glockenspiel" },
        { "pitched-percussion.glockenspiel.soprano", "glockenspiel" },
        { "pitched-percussion.hammer-dulcimer", "dulcimer" },
        { "pitched-percussion.kalimba", "kalimba" },
        { "pitched-percussion.marimba", "marimba" },
        { "pitched-percussion.marimba.bass", "marimba" },
        { "pitched-percussion.music-box", "music box" },
        { "pitched-percussion.tubular-bells", "tubular bells" },
        { "pitched-percussion.vibraphone", "vibraphone" },
        { "pitched-percussion.xylophone", "xylophone" },
        { "pitched-percussion.xylophone.alto", "xylophone" },
        { "pitched-percussion.xylophone.bass", "xylophone" },
        { "pitched-percussion.xylophone.soprano", "xylophone" },
        { "pitched-percussion.xylorimba", "xylophone" },
        { "pluck.banjo", "banjo" },
        { "pluck.banjo.tenor", "banjo" },
        { "pluck.bass", "acoustic bass" },
        { "pluck.bass.acoustic", "acoustic bass" },
        { "pluck.bass.electric", "electric bass" },
        { "pluck.bass.fretless", "fretless bass" },
        { "pluck.bass.synth", "synth bass 1" },
        { "pluck.dulcimer", "dulcimer" },
        { "pluck.guitar", "acoustic guitar (nylon)" },
        { "pluck.guitar.acoustic", "acoustic guitar (nylon)" },
        { "pluck.guitar.electric", "electric guitar (jazz)" },
        { "pluck.guitar.nylon-string", "acoustic guitar (nylon)" },
        { "pluck.guitar.steel-string", "acoustic guitar (steel)" },
        { "pluck.harp", "orchestral harp" },
        { "pluck.lute", "acoustic guitar (nylon)" },
        { "pluck.shamisen", "shamisen" },
        { "pluck.sitar", "sitar" },
        { "strings.cello", "cello" },
        { "strings.cello.piccolo", "cello" },
        { "strings.contrabass", "contrabass" },
        { "strings.fiddle", "fiddle" },
        { "strings.group.synth", "synth strings 1" },
        { "strings.viola", "viola" },
        { "strings.violin", "violin" },
        { "synth.effects.atmosphere", "fx 4 (atmosphere)" },
        { "synth.effects.brightness", "fx 5 (brightness)" },
        { "synth.effects.crystal", "fx 3 (crystal)" },
        { "synth.effects.echoes", "fx 7 echoes" },
        { "synth.effects.goblins", "fx 6 goblins" },
        { "synth.effects.rain", "fx 1 rain" },
        { "synth.effects.sci-fi", "fx 8 sci-fi" },
        { "synth.effects.soundtrack", "fx 2 (soundtrack)" },
        { "synth.pad.bowed", "pad 5 bowed" },
        { "synth.pad.choir", "pad 4 choir" },
        { "synth.pad.halo", "pad 7 halo" },
        { "synth.pad.metallic", "pad 6 metallic" },
        { "synth.pad.polysynth", "pad 3 polysynth" },
        { "synth.pad.sweep", "pad 8 sweep" },
        { "synth.pad.warm", "pad 2 warm" },
        { "synth.tone.sawtooth", "lead 1 (square)" },
        { "synth.tone.square", "lead 2 (sawtooth)" },
        { "voice.aa", "choir aahs" },
        { "voice.alto", "choir aahs" },
        { "voice.aw", "choir aahs" },
        { "voice.baritone", "choir aahs" },
        { "voice.bass", "choir aahs" },
        { "voice.child", "choir aahs" },
        { "voice.countertenor", "choir aahs" },
        { "voice.doo", "choir aahs" },
        { "voice.ee", "choir aahs" },
        { "voice.female", "choir aahs" },
        { "voice.kazoo", "choir aahs" },
        { "voice.male", "choir aahs" },
        { "voice.mezzo-soprano", "choir aahs" },
        { "voice.mm", "choir aahs" },
        { "voice.oo", "voice oohs" },
        { "voice.soprano", "choir aahs" },
        { "voice.synth", "synth voice" },
        { "wind.flutes.blown-bottle", "blown bottle" },
        { "wind.flutes.calliope", "lead 3 (calliope)" },
        { "wind.flutes.flute", "flute" },
        { "wind.flutes.flute.alto", "flute" },
        { "wind.flutes.flute.bass", "flute" },
        { "wind.flutes.flute.contra-alto", "flute" },
        { "wind.flutes.flute.contrabass", "flute" },
        { "wind.flutes.flute.double-contrabass", "flute" },
        { "wind.flutes.flute.piccolo", "flute" },
        { "wind.flutes.flute.subcontrabass", "flute" },
        { "wind.flutes.ocarina", "ocarina" },
        { "wind.flutes.recorder", "recorder" },
        { "wind.flutes.recorder.alto", "recorder" },
        { "wind.flutes.recorder.bass", "recorder" },
        { "wind.flutes.recorder.contrabass", "recorder" },
        { "wind.flutes.recorder.descant", "recorder" },
        { "wind.flutes.recorder.garklein", "recorder" },
        { "wind.flutes.recorder.great-bass", "recorder" },
        { "wind.flutes.recorder.sopranino", "recorder" },
        { "wind.flutes.recorder.soprano", "recorder" },
        { "wind.flutes.recorder.tenor", "recorder" },
        { "wind.flutes.shakuhachi", "shakuhachi" },
        { "wind.flutes.whistle", "whistle" },
        { "wind.flutes.whistle.alto", "whistle" },
        { "wind.pipes.bagpipes", "bagpipe" },
        { "wind.reed.basset-horn", "clarinet" },
        { "wind.reed.bassoon", "bassoon" },
        { "wind.reed.clarinet", "clarinet" },
        { "wind.reed.clarinet.a", "clarinet" },
        { "wind.reed.clarinet.alto", "clarinet" },
        { "wind.reed.clarinet.bass", "clarinet" },
        { "wind.reed.clarinet.basset", "clarinet" },
        { "wind.reed.clarinet.bflat", "clarinet" },
        { "wind.reed.clarinet.contra-alto", "clarinet" },
        { "wind.reed.clarinet.contrabass", "clarinet" },
        { "wind.reed.clarinet.eflat", "clarinet" },
        { "wind.reed.clarinet.piccolo.aflat", "clarinet" },
        { "wind.reed.contrabass", "contrabass" },
        { "wind.reed.contrabassoon", "bassoon" },
        { "wind.reed.english-horn", "oboe" },
        { "wind.reed.harmonica", "harmonica" },
        { "wind.reed.harmonica.bass", "harmonica" },
        { "wind.reed.oboe", "oboe" },
        { "wind.reed.oboe.bass", "oboe" },
        { "wind.reed.oboe.piccolo", "oboe" },
        { "wind.reed.oboe-da-caccia", "oboe" },
        { "wind.reed.oboe-damore", "oboe" },
        { "wind.reed.saxophone", "alto sax" },
        { "wind.reed.saxophone.alto", "alto sax" },
        { "wind.reed.saxophone.baritone", "baritone sax" },
        { "wind.reed.saxophone.bass", "baritone sax" },
        { "wind.reed.saxophone.contrabass", "baritone sax" },
        { "wind.reed.saxophone.melody", "soprano sax" },
        { "wind.reed.saxophone.mezzo-soprano", "soprano sax" },
        { "wind.reed.saxophone.sopranino", "soprano sax" },
        { "wind.reed.saxophone.sopranissimo", "soprano sax" },
        { "wind.reed.saxophone.soprano", "soprano, sax" },
        { "wind.reed.saxophone.subcontrabass", "baritone sax" },
        { "wind.reed.saxophone.tenor", "tenor sax" },
        { "wind.reed.shenai", "shanai" },
        { "wood.temple-block", "wood block" },
        { "wood.wood-block", "wood block" },
        };

    /// <summary>
    /// Maps a MusicXML <c>&lt;instrument-sound&gt;</c> value to a LilyPond MIDI
    /// instrument name.
    /// </summary>
    /// <param name="sound">The sound identifier.</param>
    /// <returns>The instrument name; a grand piano for anything unrecognised.</returns>
    internal static string MusicXmlSoundToLilyPondMidiInstrument(string sound)
        => sound != null && MidiInstruments.TryGetValue(sound, out string name)
            ? name
            : "acoustic grand";
}
