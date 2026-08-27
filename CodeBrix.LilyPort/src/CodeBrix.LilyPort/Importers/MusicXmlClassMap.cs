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

namespace CodeBrix.LilyPort.Importers; //was previously: python/musicxml.py (class_dict and get_class);

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port.

/// <summary>
/// Which class each MusicXML element name is read into, and which of them the schema
/// reduces to a bare value.
/// </summary>
/// <remarks>
/// Upstream synthesises a class for every name this table does not carry, so that
/// <c>isinstance</c> can still tell one unknown element from another. The port answers
/// null for those names instead and matches them BY NAME, which is the same question
/// asked the other way round; see <see cref="MusicXmlNode.GetNamedChildren"/>.
/// <para>
/// The table is read-only and shared: unlike upstream's, it never grows at run time,
/// so two imports at once cannot see each other's entries.
/// </para>
/// </remarks>
internal static class MusicXmlClassMap
{
    private sealed class Entry
    {
        internal Entry(Type type, Func<MusicXmlNode> create, Func<string, object> toValue = null)
        {
            NodeType = type;
            Create = create;
            ToValue = toValue;
        }

        internal Type NodeType { get; }

        internal Func<MusicXmlNode> Create { get; }

        /// <summary>
        /// How the schema reduces this element to a value, or null when it does not.
        /// </summary>
        /// <remarks>
        /// Upstream's four reducers all begin by joining the element's TEXT children,
        /// so the port hands them that text rather than the node.
        /// </remarks>
        internal Func<string, object> ToValue { get; }
    }

    /// <summary>python's <c>minidom_demarshal_text_to_int</c>.</summary>
    private static object TextToInt(string text)
        => int.Parse(text, CultureInfo.InvariantCulture);

    /// <summary>python's <c>minidom_demarshal_text_to_force_int</c>.</summary>
    private static object TextToForceInt(string text)
    {
        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
        {
            return value;
        }

        return (int)double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
    }

    /// <summary>python's <c>minidom_demarshal_text_to_int_or_float</c>.</summary>
    /// <remarks>
    /// ⚠ THE TWO READINGS ARE NOT INTERCHANGEABLE. python keeps the integer as an
    /// integer, and every later use of the value — <c>Fraction(n, 4)</c>, a <c>%s</c>
    /// in a message — behaves differently for 2 than for 2.0. The port therefore boxes
    /// an <see cref="int"/> or a <see cref="double"/>, exactly as upstream decides it.
    /// </remarks>
    private static object TextToIntOrFloat(string text)
    {
        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
        {
            return value;
        }

        return double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
    }

    /// <summary>python's <c>minidom_demarshal_text_to_str</c>.</summary>
    private static object TextToString(string text) => text.Trim();

    /// <summary>python's <c>minidom_demarshal_true</c>.</summary>
    private static object AlwaysTrue(string text) => true;

    private static readonly Dictionary<string, Entry> Entries
        = new Dictionary<string, Entry>(StringComparer.Ordinal)
        {
            { "#comment", new Entry(typeof(MusicXmlHashComment), () => new MusicXmlHashComment()) },
            { "#text", new Entry(typeof(MusicXmlHashText), () => new MusicXmlHashText()) },
            { "accidental", new Entry(typeof(MusicXmlAccidental), () => new MusicXmlAccidental()) },
            { "alter", new Entry(typeof(MusicXmlAlter), () => new MusicXmlAlter(), TextToIntOrFloat) },
            { "arpeggiate", new Entry(typeof(MusicXmlArpeggiate), () => new MusicXmlArpeggiate()) },
            { "attributes", new Entry(typeof(MusicXmlAttributes), () => new MusicXmlAttributes()) },
            { "backup", new Entry(typeof(MusicXmlBackup), () => new MusicXmlBackup()) },
            { "barline", new Entry(typeof(MusicXmlBarline), () => new MusicXmlBarline()) },
            { "bar-style", new Entry(typeof(MusicXmlBarStyle), () => new MusicXmlBarStyle()) },
            { "bass", new Entry(typeof(MusicXmlBass), () => new MusicXmlBass()) },
            { "beam", new Entry(typeof(MusicXmlBeam), () => new MusicXmlBeam()) },
            { "beats", new Entry(typeof(MusicXmlBeats), () => new MusicXmlBeats()) },
            { "beat-type", new Entry(typeof(MusicXmlBeatType), () => new MusicXmlBeatType()) },
            { "beat-unit", new Entry(typeof(MusicXmlBeatUnit), () => new MusicXmlBeatUnit()) },
            { "beat-unit-dot", new Entry(typeof(MusicXmlBeatUnitDot), () => new MusicXmlBeatUnitDot()) },
            { "beat-unit-tied", new Entry(typeof(MusicXmlBeatUnitTied), () => new MusicXmlBeatUnitTied()) },
            { "bend", new Entry(typeof(MusicXmlBend), () => new MusicXmlBend()) },
            { "bracket", new Entry(typeof(MusicXmlBracket), () => new MusicXmlBracket()) },
            { "chord", new Entry(typeof(MusicXmlChord), () => new MusicXmlChord(), AlwaysTrue) },
            { "credit", new Entry(typeof(MusicXmlCredit), () => new MusicXmlCredit()) },
            { "credit-words", new Entry(typeof(MusicXmlCreditWords), () => new MusicXmlCreditWords()) },
            { "credit-symbol", new Entry(typeof(MusicXmlCreditSymbol), () => new MusicXmlCreditSymbol()) },
            { "dashes", new Entry(typeof(MusicXmlDashes), () => new MusicXmlDashes()) },
            { "degree", new Entry(typeof(MusicXmlChordModification), () => new MusicXmlChordModification()) },
            { "direction", new Entry(typeof(MusicXmlDirection), () => new MusicXmlDirection()) },
            { "direction-type", new Entry(typeof(MusicXmlDirType), () => new MusicXmlDirType()) },
            { "display-octave", new Entry(typeof(MusicXmlDisplayOctave), () => new MusicXmlDisplayOctave(), TextToInt) },
            { "display-step", new Entry(typeof(MusicXmlDisplayStep), () => new MusicXmlDisplayStep(), TextToString) },
            { "duration", new Entry(typeof(MusicXmlDuration), () => new MusicXmlDuration(), TextToForceInt) },
            { "elision", new Entry(typeof(MusicXmlElision), () => new MusicXmlElision()) },
            { "extend", new Entry(typeof(MusicXmlExtend), () => new MusicXmlExtend()) },
            { "forward", new Entry(typeof(MusicXmlForward), () => new MusicXmlForward()) },
            { "frame", new Entry(typeof(MusicXmlFrame), () => new MusicXmlFrame()) },
            { "frame-note", new Entry(typeof(MusicXmlFrameNote), () => new MusicXmlFrameNote()) },
            { "figured-bass", new Entry(typeof(MusicXmlFiguredBass), () => new MusicXmlFiguredBass()) },
            { "glissando", new Entry(typeof(MusicXmlGlissando), () => new MusicXmlGlissando()) },
            { "grace", new Entry(typeof(MusicXmlGrace), () => new MusicXmlGrace()) },
            { "group-abbreviation", new Entry(typeof(MusicXmlGroupAbbreviation), () => new MusicXmlGroupAbbreviation()) },
            { "group-abbreviation-display", new Entry(typeof(MusicXmlGroupAbbreviationDisplay), () => new MusicXmlGroupAbbreviationDisplay()) },
            { "group-name", new Entry(typeof(MusicXmlGroupName), () => new MusicXmlGroupName()) },
            { "group-name-display", new Entry(typeof(MusicXmlGroupNameDisplay), () => new MusicXmlGroupNameDisplay()) },
            { "group-symbol", new Entry(typeof(MusicXmlGroupSymbol), () => new MusicXmlGroupSymbol()) },
            { "harmony", new Entry(typeof(MusicXmlHarmony), () => new MusicXmlHarmony()) },
            { "identification", new Entry(typeof(MusicXmlIdentification), () => new MusicXmlIdentification()) },
            { "key-alter", new Entry(typeof(MusicXmlKeyAlter), () => new MusicXmlKeyAlter()) },
            { "key-octave", new Entry(typeof(MusicXmlKeyOctave), () => new MusicXmlKeyOctave()) },
            { "key-step", new Entry(typeof(MusicXmlKeyStep), () => new MusicXmlKeyStep()) },
            { "lyric", new Entry(typeof(MusicXmlLyric), () => new MusicXmlLyric()) },
            { "measure", new Entry(typeof(MusicXmlMeasure), () => new MusicXmlMeasure()) },
            { "metronome", new Entry(typeof(MusicXmlMetronome), () => new MusicXmlMetronome()) },
            { "non-arpeggiate", new Entry(typeof(MusicXmlNonArpeggiate), () => new MusicXmlNonArpeggiate()) },
            { "notations", new Entry(typeof(MusicXmlNotations), () => new MusicXmlNotations()) },
            { "note", new Entry(typeof(MusicXmlNote), () => new MusicXmlNote()) },
            { "notehead", new Entry(typeof(MusicXmlNotehead), () => new MusicXmlNotehead()) },
            { "octave", new Entry(typeof(MusicXmlOctave), () => new MusicXmlOctave(), TextToInt) },
            { "octave-shift", new Entry(typeof(MusicXmlOctaveShift), () => new MusicXmlOctaveShift()) },
            { "offset", new Entry(typeof(MusicXmlOffset), () => new MusicXmlOffset(), TextToIntOrFloat) },
            { "ornaments", new Entry(typeof(MusicXmlOrnaments), () => new MusicXmlOrnaments()) },
            { "part", new Entry(typeof(MusicXmlPart), () => new MusicXmlPart()) },
            { "part-group", new Entry(typeof(MusicXmlPartGroup), () => new MusicXmlPartGroup()) },
            { "part-list", new Entry(typeof(MusicXmlPartList), () => new MusicXmlPartList()) },
            { "pedal", new Entry(typeof(MusicXmlPedal), () => new MusicXmlPedal()) },
            { "per-minute", new Entry(typeof(MusicXmlPerMinute), () => new MusicXmlPerMinute()) },
            { "pitch", new Entry(typeof(MusicXmlPitch), () => new MusicXmlPitch()) },
            { "print", new Entry(typeof(MusicXmlPrint), () => new MusicXmlPrint()) },
            { "rest", new Entry(typeof(MusicXmlRest), () => new MusicXmlRest()) },
            { "root", new Entry(typeof(MusicXmlRoot), () => new MusicXmlRoot()) },
            { "score-part", new Entry(typeof(MusicXmlScorePart), () => new MusicXmlScorePart()) },
            { "slide", new Entry(typeof(MusicXmlSlide), () => new MusicXmlSlide()) },
            { "slur", new Entry(typeof(MusicXmlSlur), () => new MusicXmlSlur()) },
            { "sound", new Entry(typeof(MusicXmlSound), () => new MusicXmlSound()) },
            { "staff", new Entry(typeof(MusicXmlStaff), () => new MusicXmlStaff(), TextToString) },
            { "stem", new Entry(typeof(MusicXmlStem), () => new MusicXmlStem()) },
            { "step", new Entry(typeof(MusicXmlStep), () => new MusicXmlStep(), TextToString) },
            { "syllabic", new Entry(typeof(MusicXmlSyllabic), () => new MusicXmlSyllabic()) },
            { "text", new Entry(typeof(MusicXmlText), () => new MusicXmlText()) },
            { "time", new Entry(typeof(MusicXmlTime), () => new MusicXmlTime()) },
            { "time-modification", new Entry(typeof(MusicXmlTimeModification), () => new MusicXmlTimeModification()) },
            { "tied", new Entry(typeof(MusicXmlTied), () => new MusicXmlTied()) },
            { "tremolo", new Entry(typeof(MusicXmlTremolo), () => new MusicXmlTremolo()) },
            { "tuplet", new Entry(typeof(MusicXmlTuplet), () => new MusicXmlTuplet()) },
            { "type", new Entry(typeof(MusicXmlType), () => new MusicXmlType()) },
            { "unpitched", new Entry(typeof(MusicXmlUnpitched), () => new MusicXmlUnpitched()) },
            { "voice", new Entry(typeof(MusicXmlVoiceElement), () => new MusicXmlVoiceElement(), TextToString) },
            { "wavy-line", new Entry(typeof(MusicXmlWavyLine), () => new MusicXmlWavyLine()) },
            { "wedge", new Entry(typeof(MusicXmlWedge), () => new MusicXmlWedge()) },
            { "words", new Entry(typeof(MusicXmlWords), () => new MusicXmlWords()) },
            { "work", new Entry(typeof(MusicXmlWork), () => new MusicXmlWork()) },
        };

    private static Dictionary<Type, string> _names;

    /// <summary>The element name a class is registered under.</summary>
    /// <param name="nodeType">The class.</param>
    /// <returns>The name, or null when the class is not in the table.</returns>
    /// <remarks>
    /// ⚠ Upstream's element name is a CLASS attribute: a loop at the foot of
    /// <c>musicxml.py</c> writes <c>cls._name = name</c> for every entry of the table, and
    /// <c>get_name</c> is a classmethod reading it. So a node the CONVERTER builds by hand
    /// — the <c>&lt;words&gt;</c> nodes a lyric or a dynamics markup is assembled from —
    /// answers its element name too, without ever having been demarshalled. The port
    /// reproduces that by looking the class up here from the base constructor.
    /// </remarks>
    internal static string GetName(Type nodeType)
    {
        if (_names == null)
        {
            Dictionary<Type, string> names = new Dictionary<Type, string>();
            foreach (KeyValuePair<string, Entry> entry in Entries)
            {
                //The first name a class is registered under wins, exactly as python's own
                //loop leaves the LAST one — see the remark below.
                names[entry.Value.NodeType] = entry.Key;
            }

            _names = names;
        }

        return _names.TryGetValue(nodeType, out string name) ? name : null;
    }

    /// <summary>The class an element name is read into.</summary>
    /// <param name="name">The element name.</param>
    /// <returns>The class, or null when the name is not in the table.</returns>
    internal static Type GetClass(string name)
        => name != null && Entries.TryGetValue(name, out Entry entry) ? entry.NodeType : null;

    /// <summary>Builds the node an element name is read into.</summary>
    /// <param name="name">The element name.</param>
    /// <returns>The node, with its element name already set.</returns>
    internal static MusicXmlNode CreateNode(string name)
    {
        MusicXmlNode node = name != null && Entries.TryGetValue(name, out Entry entry)
            ? entry.Create()
            : new MusicXmlGenericNode();
        node.ElementName = name;
        return node;
    }

    /// <summary>How the schema reduces an element to a value, if it does.</summary>
    /// <param name="name">The element name.</param>
    /// <returns>The reducer, or null.</returns>
    internal static Func<string, object> GetValueReader(string name)
        => name != null && Entries.TryGetValue(name, out Entry entry) ? entry.ToValue : null;
}

/// <summary>The alter element, which the schema reduces to a number.</summary>
internal sealed class MusicXmlAlter : MusicXmlMusicNode
{
}

/// <summary>The duration element, which the schema reduces to a whole number.</summary>
/// <remarks>
/// While non-integer values are technically allowed, we don't support them (using only
/// integers is also recommended by the specification).
/// </remarks>
internal sealed class MusicXmlDuration : MusicXmlMusicNode
{
}

/// <summary>The offset element, which the schema reduces to a number.</summary>
/// <remarks>
/// As with the duration element, values are recommended to be integers. However,
/// non-integer values have been seen in the wild.
/// </remarks>
internal sealed class MusicXmlOffset : MusicXmlMusicNode
{
}
