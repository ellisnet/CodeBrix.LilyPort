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
using CodeBrix.LilyPort.ConvertLy;

namespace CodeBrix.LilyPort.Importers; //was previously: scripts/musicxml2ly.py (musicxml_spanner_to_lily_event, the articulation and ornament builders, articulations_dict and musicxml_articulation_to_lily_event);

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port.

/// <summary>What one entry of the articulations table says to build.</summary>
/// <remarks>
/// ⚠ Upstream's table holds FOUR shapes in one dictionary — a string, a two-string
/// tuple, a function, and a (class, name) tuple — and asks <c>isinstance</c> which it
/// has. C# cannot, so each shape is a named case here and <see cref="Kind"/> is that
/// <c>isinstance</c> chain.
/// </remarks>
internal sealed class MusicXmlArticulationEntry
{
    /// <summary>Which of the four shapes upstream's table entry has.</summary>
    internal enum EntryKind
    {
        /// <summary>A plain articulation with that name.</summary>
        Articulation,

        /// <summary>An ornament with a glyph name and its equivalent command.</summary>
        Ornament,

        /// <summary>A builder that answers a complete event.</summary>
        Builder,

        /// <summary>A named event of a class other than the plain articulation.</summary>
        TypedArticulation,
    }

    /// <summary>Which class a typed articulation entry builds.</summary>
    internal enum ArticulationClass
    {
        /// <summary>An articulation written as a one-character modifier.</summary>
        Short,

        /// <summary>An articulation LilyPond draws without a direction modifier.</summary>
        NoDirection,
    }

    /// <summary>Builds a plain-articulation entry.</summary>
    /// <param name="name">The articulation's LilyPond name.</param>
    internal MusicXmlArticulationEntry(string name)
    {
        Kind = EntryKind.Articulation;
        Name = name;
    }

    /// <summary>Builds an ornament entry.</summary>
    /// <param name="glyph">The ornament's glyph name.</param>
    /// <param name="command">Its equivalent LilyPond command.</param>
    internal MusicXmlArticulationEntry(string glyph, string command)
    {
        Kind = EntryKind.Ornament;
        Glyph = glyph;
        Command = command;
    }

    /// <summary>Builds a builder entry.</summary>
    /// <param name="builder">The builder.</param>
    internal MusicXmlArticulationEntry(
        Func<MusicXmlNode, string, string, LilyMusic> builder)
    {
        Kind = EntryKind.Builder;
        Builder = builder;
    }

    /// <summary>Builds a typed-articulation entry.</summary>
    /// <param name="articulationClass">Which class to build.</param>
    /// <param name="name">The articulation's name.</param>
    internal MusicXmlArticulationEntry(ArticulationClass articulationClass, string name)
    {
        Kind = EntryKind.TypedArticulation;
        Class = articulationClass;
        Name = name;
    }

    /// <summary>Which of the four shapes this entry has.</summary>
    internal EntryKind Kind { get; }

    /// <summary>The articulation's name, for the two name-carrying shapes.</summary>
    internal string Name { get; }

    /// <summary>The ornament's glyph name.</summary>
    internal string Glyph { get; }

    /// <summary>The ornament's equivalent LilyPond command.</summary>
    internal string Command { get; }

    /// <summary>The builder, for a builder entry.</summary>
    internal Func<MusicXmlNode, string, string, LilyMusic> Builder { get; }

    /// <summary>Which class a typed articulation entry builds.</summary>
    internal ArticulationClass Class { get; }
}

internal sealed partial class MusicXmlConverter
{
    private static readonly Dictionary<string, int> SpannerTypeDict
        = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            { "start", -1 },
            { "begin", -1 },
            { "crescendo", -1 },
            { "diminuendo", -1 },
            { "up", -1 },
            { "down", -1 },

            { "continue", 0 },
            { "change", 0 },

            { "stop", 1 },
            { "end", 1 },
            //TODO (upstream's): 'backward hook' for <beam>
            //TODO (upstream's): 'discontinue' for <pedal>
            //TODO (upstream's): 'forward hook' for <beam>
            //TODO (upstream's): 'let-ring' for <tied>
            //TODO (upstream's): 'resume' for <pedal>
            //TODO (upstream's): 'sostenuto' for <pedal>
        };

    private static readonly Dictionary<string, int> DirectionIndicators
        = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            { "above", 1 },
            { "upright", 1 },
            { "up", 1 },
            { "over", 1 },

            { "below", -1 },
            { "downright", -1 },
            { "down", -1 },
            { "under", -1 },
            { "inverted", -1 },
        };

    private static readonly Dictionary<string, string> FermataTypes
        = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { string.Empty, "fermata" },
            { "angled", "shortfermata" },
            //'curlew': TODO (upstream's)
            { "double-angled", "veryshortfermata" },
            { "double-dot", "henzelongfermata" },
            { "double-squared", "verylongfermata" },
            { "half-curve", "henzeshortfermata" },
            { "normal", "fermata" },
            { "square", "longfermata" },
        };

    private Dictionary<string, MusicXmlArticulationEntry> _articulationsDict;

    /// <summary>Builds the output-side spanner event one element asks for.</summary>
    /// <param name="mxlEvent">The element.</param>
    /// <param name="attributes">The carried-over attributes, or null for the element's own.</param>
    /// <param name="spannerName">Which spanner to build, or null for the element's name.</param>
    /// <returns>The event.</returns>
    /// <remarks>The colour and font-size arguments upstream declares here get ignored.</remarks>
    internal LilySpanEvent MusicXmlSpannerToLilyEvent(
        MusicXmlNode mxlEvent,
        Dictionary<string, object> attributes = null,
        string spannerName = null)
    {
        LilySpanEvent ev = null;

        string name = spannerName ?? mxlEvent.GetName();
        MusicXmlSpanner spanner = mxlEvent as MusicXmlSpanner;
        switch (name)
        {
            case "beam":
                ev = new LilyBeamEvent(State);
                break;
            case "dashes":
            case "wavy-line":
                ev = new LilyTextSpannerEvent(State);
                break;
            case "dynamics-spanner":
                ev = new LilyDynamicsSpannerEvent(State);
                break;
            case "bracket":
                ev = new LilyBracketSpannerEvent(State);
                break;
            case "glissando":
            case "slide":
                ev = new LilyGlissandoEvent(State);
                break;
            case "octave-shift":
                ev = new LilyOctaveShiftEvent(State);
                break;
            case "pedal":
                ev = new LilyPedalEvent(State);
                break;
            case "slur":
                ev = new LilySlurEvent(State);
                break;
            case "wedge":
                ev = new LilyHairpinEvent(State);
                break;
            default:
                State.Warning("unknown span event " + mxlEvent);
                break;
        }

        if (ev != null)
        {
            if (spanner != null)
            {
                spanner.SpannerEvent = ev;
            }

            ev.MxlEvent = spanner;
            ev.MxlAttributes = ToStringAttributes(attributes);
        }

        if (name == "wavy-line")
        {
            ((LilyTextSpannerEvent)ev).Style = OrnamentHasWhat(
                (LilyTextSpannerEvent)ev, mxlEvent);
        }
        else if (name == "dashes")
        {
            ((LilyTextSpannerEvent)ev).Style = "dashes";
        }
        else if (name == "bracket")
        {
            State.NeededAdditionalDefinitions.Add("make-edge-height");
        }

        string type = spanner != null ? spanner.GetSpannerType() : mxlEvent.Attribute("type");
        if (SpannerTypeDict.TryGetValue(type ?? string.Empty, out int spanDirection))
        {
            ev.SpanDirection = spanDirection;
        }
        else
        {
            State.Warning("unknown span type " + type + " for " + name);
            spanDirection = int.MinValue;
        }

        Dictionary<string, object> effective = attributes
            ?? LilyMarkupElement.CopyAttributes(mxlEvent);

        ev.SetSpanType(type);
        ev.LineType = GetAttribute(
            effective, "line-type", name == "glissando" ? "wavy" : "solid");
        //⚠ Upstream's `getattr(mxl_event, "start_stop", False)': only the wavy line
        //carries this, and every other element reads as false.
        ev.StartStop = mxlEvent is MusicXmlWavyLine wavyLine && wavyLine.StartStop;

        //The `line-end' attribute gets handled in the bracket spanner.

        //An attribute of <octave-shift>.
        //⚠ Upstream's `getattr(mxl_event, "size", 0)' finds the CLASS attribute
        //`Octave_shift.size = 8' when the document names none, so eight is the default for
        //that element and zero for every other. Reading the element's own member is that
        //lookup.
        ev.Size = int.Parse(
            mxlEvent is MusicXmlOctaveShift octaveShift
                ? octaveShift.Size
                : mxlEvent.Attribute("size", "0"),
            CultureInfo.InvariantCulture);
        ev.Color = GetAttribute(effective, "color", null);

        //The font size is handled via the markup builder in dynamics spanners.
        if (name != "dynamics-spanner")
        {
            ev.FontSize = GetAttribute(effective, "font-size", null);
        }

        if (name == "slur")
        {
            ((LilySlurEvent)ev).Number = int.Parse(
                GetAttribute(effective, "number", "1"), CultureInfo.InvariantCulture);
        }

        if (!Options.NoArticulationDirections)
        {
            if (spanDirection == -1)
            {
                //If both an associated ornament and the spanner has a `placement'
                //attribute, the former wins.
                string direction = ev is LilyTextSpannerEvent textSpanner
                    ? textSpanner.MxlOrnament?.Attribute("placement")
                    : null;

                if (direction == null)
                {
                    direction = GetAttribute(effective, "placement", null);
                }

                if (direction == null && name == "slur")
                {
                    direction = GetAttribute(effective, "orientation", null);
                }

                if (direction != null)
                {
                    ev.ForceDirection = MusicXmlDirectionToIndicator(direction);
                }
            }
        }

        return ev;
    }

    /// <summary>Reads one carried-over attribute.</summary>
    /// <param name="attributes">The attributes.</param>
    /// <param name="name">The attribute name.</param>
    /// <param name="defaultValue">What to answer when it is absent.</param>
    /// <returns>The value, or the default.</returns>
    private static string GetAttribute(
        Dictionary<string, object> attributes, string name, string defaultValue)
        => attributes != null && attributes.TryGetValue(name, out object value)
            ? value as string
            : defaultValue;

    /// <summary>Narrows a carried-over attribute map to the strings a spanner reads.</summary>
    /// <param name="attributes">The attributes, or null.</param>
    /// <returns>The strings, or null.</returns>
    /// <remarks>
    /// The only non-string an attribute map ever carries is the dynamics scale factor,
    /// which no spanner reads.
    /// </remarks>
    private static Dictionary<string, string> ToStringAttributes(
        Dictionary<string, object> attributes)
    {
        if (attributes == null)
        {
            return null;
        }

        Dictionary<string, string> answer
            = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, object> entry in attributes)
        {
            if (entry.Value is string text)
            {
                answer[entry.Key] = text;
            }
        }

        return answer;
    }

    /// <summary>Which side of the staff a placement value names.</summary>
    /// <param name="direction">The value.</param>
    /// <returns>1 for above, -1 for below, 0 otherwise.</returns>
    internal static int MusicXmlDirectionToIndicator(string direction)
        => direction != null && DirectionIndicators.TryGetValue(direction, out int value)
            ? value
            : 0;

    /// <summary>Builds the articulation a fermata element asks for.</summary>
    /// <param name="mxlEvent">The element.</param>
    /// <param name="noteColor">The note's colour.</param>
    /// <param name="noteFontSize">The note's font size.</param>
    /// <returns>The event.</returns>
    internal LilyMusic MusicXmlFermataToLilyEvent(
        MusicXmlNode mxlEvent, string noteColor = null, string noteFontSize = null)
    {
        LilyArticulationEvent ev = new LilyArticulationEvent(State);
        ev.Type = FermataTypes.TryGetValue(mxlEvent.GetText(), out string named)
            ? named
            : "fermata";
        ev.Color = mxlEvent.Attribute("color", noteColor);
        ev.FontSize = mxlEvent.Attribute("font-size", noteFontSize);

        string typeAttr = mxlEvent.Attribute("type");
        if (!Options.NoArticulationDirections && typeAttr != null)
        {
            int direction = MusicXmlDirectionToIndicator(typeAttr);
            if (direction != 0)
            {
                ev.ForceDirection = direction;
            }
        }

        return ev;
    }

    /// <summary>Builds the single-note tremolo a tremolo element asks for.</summary>
    /// <param name="mxlEvent">The element.</param>
    /// <param name="noteColor">The note's colour.</param>
    /// <param name="noteFontSize">The note's font size, which is ignored.</param>
    /// <returns>The event.</returns>
    internal LilyMusic MusicXmlTremoloToLilyEvent(
        MusicXmlNode mxlEvent, string noteColor = null, string noteFontSize = null)
    {
        LilyTremoloEvent ev = new LilyTremoloEvent(State);
        ev.Color = mxlEvent.Attribute("color", noteColor);
        //TODO (upstream's): Support unmeasured tremolos by handling the `smufl' attribute
        //(which in turn would react to the currently unused `font-size' attribute).

        string text = mxlEvent.GetText();
        if (!string.IsNullOrEmpty(text))
        {
            ev.Strokes = int.Parse(text.Trim(), CultureInfo.InvariantCulture);
        }
        else
        {
            ev.Strokes = 3;
            State.Warning(
                "empty <tremolo> element, setting value to "
                + ev.Strokes.ToString(CultureInfo.InvariantCulture));
        }

        return ev;
    }

    /// <summary>Builds the bend a falloff element asks for.</summary>
    /// <param name="mxlEvent">The element.</param>
    /// <param name="noteColor">The note's colour.</param>
    /// <param name="noteFontSize">The note's font size, which is ignored.</param>
    /// <returns>The event.</returns>
    internal LilyMusic MusicXmlFalloffToLilyEvent(
        MusicXmlNode mxlEvent, string noteColor = null, string noteFontSize = null)
    {
        LilyBendEvent ev = new LilyBendEvent(State);
        ev.Alter = -4L;
        ev.Color = mxlEvent.Attribute("color", noteColor);
        return ev;
    }

    /// <summary>Builds the bend a doit element asks for.</summary>
    /// <param name="mxlEvent">The element.</param>
    /// <param name="noteColor">The note's colour.</param>
    /// <param name="noteFontSize">The note's font size, which is ignored.</param>
    /// <returns>The event.</returns>
    internal LilyMusic MusicXmlDoitToLilyEvent(
        MusicXmlNode mxlEvent, string noteColor = null, string noteFontSize = null)
    {
        LilyBendEvent ev = new LilyBendEvent(State);
        ev.Alter = 4L;
        ev.Color = mxlEvent.Attribute("color", noteColor);
        return ev;
    }

    /// <summary>Builds the bend a bend element asks for.</summary>
    /// <param name="mxlEvent">The element.</param>
    /// <param name="noteColor">The note's colour.</param>
    /// <param name="noteFontSize">The note's font size, which is ignored.</param>
    /// <returns>The event.</returns>
    internal LilyMusic MusicXmlBendToLilyEvent(
        MusicXmlNode mxlEvent, string noteColor = null, string noteFontSize = null)
    {
        LilyBendEvent ev = new LilyBendEvent(State);
        ev.Color = mxlEvent.Attribute("color", noteColor);
        ev.Alter = ((MusicXmlBend)mxlEvent).BendAlter();
        return ev;
    }

    /// <summary>Builds the breath mark a breath-mark element asks for.</summary>
    /// <param name="mxlEvent">The element.</param>
    /// <param name="noteColor">The note's colour, which is not inherited.</param>
    /// <param name="noteFontSize">The note's font size, which is not inherited.</param>
    /// <returns>The event.</returns>
    /// <remarks>
    /// TODO (upstream's): Read the <c>&lt;breath-mark-value&gt;</c> child and override the
    /// type of symbol: comma, tick, upbow, salzedo. TODO (upstream's): Shall the colour
    /// and font size of <c>&lt;note&gt;</c> be inherited?
    /// </remarks>
    internal LilyMusic MusicXmlBreathMarkToLilyEvent(
        MusicXmlNode mxlEvent, string noteColor = null, string noteFontSize = null)
        => new LilyBreatheEvent(
            State, mxlEvent.Attribute("color"), mxlEvent.Attribute("font-size"));

    /// <summary>Builds the caesura a caesura element asks for.</summary>
    /// <param name="mxlEvent">The element.</param>
    /// <param name="noteColor">The note's colour, which is not inherited.</param>
    /// <param name="noteFontSize">The note's font size, which is not inherited.</param>
    /// <returns>The event.</returns>
    /// <remarks>
    /// TODO (upstream's): Read the <c>&lt;caesura-value&gt;</c> child and override the
    /// type of symbol: normal, thick, short, curved, single.
    /// </remarks>
    internal LilyMusic MusicXmlCaesuraToLilyEvent(
        MusicXmlNode mxlEvent, string noteColor = null, string noteFontSize = null)
        => new LilyCaesuraEvent(
            State, mxlEvent.Attribute("color"), mxlEvent.Attribute("font-size"));

    /// <summary>Builds the fingering a fingering element asks for.</summary>
    /// <param name="mxlEvent">The element.</param>
    /// <param name="noteColor">The note's colour.</param>
    /// <param name="noteFontSize">The note's font size.</param>
    /// <returns>The event.</returns>
    internal LilyMusic MusicXmlFingeringEvent(
        MusicXmlNode mxlEvent, string noteColor = null, string noteFontSize = null)
    {
        LilyFingeringEvent ev = new LilyFingeringEvent(State);
        ev.Type = mxlEvent.GetText();
        ev.Alternate = mxlEvent.Attribute("alternate", "no") == "yes";
        ev.Substitution = mxlEvent.Attribute("substitution", "no") == "yes";
        ev.Color = mxlEvent.Attribute("color", noteColor);
        ev.FontSize = mxlEvent.Attribute("font-size", noteFontSize);
        ev.Visible = mxlEvent.Attribute("print-object", "yes") == "yes";

        if (ev.Substitution)
        {
            State.NeededAdditionalDefinitions.Add("fingering-substitution");
        }

        return ev;
    }

    /// <summary>Builds the plucking mark a pluck element asks for.</summary>
    /// <param name="mxlEvent">The element.</param>
    /// <param name="noteColor">The note's colour.</param>
    /// <param name="noteFontSize">The note's font size.</param>
    /// <returns>The event.</returns>
    internal LilyMusic MusicXmlPluckEvent(
        MusicXmlNode mxlEvent, string noteColor = null, string noteFontSize = null)
    {
        LilyFingeringEvent ev = new LilyFingeringEvent(State);
        ev.IsPluck = true;
        ev.Type = mxlEvent.GetText();
        ev.Color = mxlEvent.Attribute("color", noteColor);
        ev.FontSize = mxlEvent.Attribute("font-size", noteFontSize);
        ev.Visible = mxlEvent.Attribute("print-object", "yes") == "yes";

        State.NeededAdditionalDefinitions.Add("pluck");

        return ev;
    }

    /// <summary>Builds the string number a string element asks for.</summary>
    /// <param name="mxlEvent">The element.</param>
    /// <param name="noteColor">The note's colour.</param>
    /// <param name="noteFontSize">The note's font size.</param>
    /// <returns>The event.</returns>
    internal LilyMusic MusicXmlStringEvent(
        MusicXmlNode mxlEvent, string noteColor = null, string noteFontSize = null)
    {
        LilyNoDirectionArticulationEvent ev = new LilyNoDirectionArticulationEvent(State);
        ev.Type = mxlEvent.GetText();
        ev.Color = mxlEvent.Attribute("color", noteColor);
        ev.FontSize = mxlEvent.Attribute("font-size", noteFontSize);
        return ev;
    }

    /// <summary>Builds the accidental mark an accidental-mark element asks for.</summary>
    /// <param name="mxlEvent">The element.</param>
    /// <param name="noteColor">The note's colour.</param>
    /// <param name="noteFontSize">The note's font size.</param>
    /// <returns>The event.</returns>
    /// <remarks>This is for <c>&lt;accidental-mark&gt;</c> children of
    /// <c>&lt;notations&gt;</c>.</remarks>
    internal LilyMusic MusicXmlAccidentalMark(
        MusicXmlNode mxlEvent, string noteColor = null, string noteFontSize = null)
    {
        LilyAccidentalMarkEvent ev = new LilyAccidentalMarkEvent(State);
        ev.Contents = mxlEvent.GetText();
        ev.Color = mxlEvent.Attribute("color", noteColor);
        ev.FontSize = mxlEvent.Attribute("font-size", noteFontSize);
        return ev;
    }

    /// <summary>Builds the delayed inverted turn a delayed-inverted-turn element asks for.</summary>
    /// <param name="mxlEvent">The element.</param>
    /// <param name="noteColor">The note's colour.</param>
    /// <param name="noteFontSize">The note's font size.</param>
    /// <returns>The event.</returns>
    internal LilyMusic MusicXmlDelayedInvertedTurnEvent(
        MusicXmlNode mxlEvent, string noteColor = null, string noteFontSize = null)
        => MusicXmlDelayedTurnEvent(mxlEvent, noteColor, noteFontSize, true);

    /// <summary>Builds the delayed turn a delayed-turn element asks for.</summary>
    /// <param name="mxlEvent">The element.</param>
    /// <param name="noteColor">The note's colour.</param>
    /// <param name="noteFontSize">The note's font size.</param>
    /// <param name="inverted">Whether the turn is inverted.</param>
    /// <returns>The event.</returns>
    internal LilyMusic MusicXmlDelayedTurnEvent(
        MusicXmlNode mxlEvent, string noteColor = null, string noteFontSize = null,
        bool inverted = false)
    {
        LilyDelayedTurnEvent ev = new LilyDelayedTurnEvent(State);
        ev.OrnamentType = inverted
            ? ("scripts.reverseturn", "reverseturn")
            : ("scripts.turn", "turn");
        ev.Color = mxlEvent.Attribute("color", noteColor);
        ev.FontSize = mxlEvent.Attribute("font-size", noteFontSize);
        ev.Duration = ((MusicXmlMusicNode)mxlEvent.Parent.Parent.Parent).DurationValue.Value;
        return ev;
    }

    /// <summary>Builds the toe or heel mark such an element asks for.</summary>
    /// <param name="mxlEvent">The element.</param>
    /// <param name="type">Which of the two marks this is.</param>
    /// <param name="noteColor">The note's colour.</param>
    /// <param name="noteFontSize">The note's font size.</param>
    /// <returns>The event.</returns>
    internal LilyMusic MusicXmlToeHeelEvent(
        MusicXmlNode mxlEvent, string type, string noteColor = null,
        string noteFontSize = null)
    {
        State.LayoutInformation.SetContextItem("Voice", "toeHeelStyle = #'standard");
        LilyArticulationWithSubstitutionEvent ev
            = new LilyArticulationWithSubstitutionEvent(State);
        ev.Type = type;
        ev.Substitution = mxlEvent.Attribute("substitution", "no") == "yes";
        ev.Color = mxlEvent.Attribute("color", noteColor);
        ev.FontSize = mxlEvent.Attribute("font-size", noteFontSize);

        return ev;
    }

    /// <summary>Builds the toe mark a toe element asks for.</summary>
    /// <param name="mxlEvent">The element.</param>
    /// <param name="noteColor">The note's colour.</param>
    /// <param name="noteFontSize">The note's font size.</param>
    /// <returns>The event.</returns>
    internal LilyMusic MusicXmlToeEvent(
        MusicXmlNode mxlEvent, string noteColor = null, string noteFontSize = null)
        => MusicXmlToeHeelEvent(mxlEvent, "toe", noteColor, noteFontSize);

    /// <summary>Builds the heel mark a heel element asks for.</summary>
    /// <param name="mxlEvent">The element.</param>
    /// <param name="noteColor">The note's colour.</param>
    /// <param name="noteFontSize">The note's font size.</param>
    /// <returns>The event.</returns>
    internal LilyMusic MusicXmlHeelEvent(
        MusicXmlNode mxlEvent, string noteColor = null, string noteFontSize = null)
        => MusicXmlToeHeelEvent(mxlEvent, "heel", noteColor, noteFontSize);

    /// <summary>
    /// The table that translates articulations, ornaments, and other notations into the
    /// output-side events.
    /// </summary>
    /// <remarks>
    /// TODO (upstream's): Some translations are missing! Every commented-out row of
    /// upstream's own table is one it has not decided on.
    /// </remarks>
    private Dictionary<string, MusicXmlArticulationEntry> ArticulationsDict
        => _articulationsDict ??= new Dictionary<string, MusicXmlArticulationEntry>(
            StringComparer.Ordinal)
        {
            //or "accent"
            { "accent", Typed(MusicXmlArticulationEntry.ArticulationClass.Short, ">") },
            { "accidental-mark", new MusicXmlArticulationEntry(MusicXmlAccidentalMark) },
            //"arrow": "?",
            { "bend", new MusicXmlArticulationEntry(MusicXmlBendToLilyEvent) },
            //"brass-bend": "?",
            { "breath-mark", new MusicXmlArticulationEntry(MusicXmlBreathMarkToLilyEvent) },
            { "caesura", new MusicXmlArticulationEntry(MusicXmlCaesuraToLilyEvent) },
            {
                "delayed-inverted-turn",
                new MusicXmlArticulationEntry(MusicXmlDelayedInvertedTurnEvent)
            },
            {
                "delayed-turn",
                new MusicXmlArticulationEntry(
                    (element, color, fontSize)
                        => MusicXmlDelayedTurnEvent(element, color, fontSize))
            },
            //or "portato"
            {
                "detached-legato",
                Typed(MusicXmlArticulationEntry.ArticulationClass.Short, "_")
            },
            { "doit", new MusicXmlArticulationEntry(MusicXmlDoitToLilyEvent) },
            //"double-tongue": "?",
            { "down-bow", new MusicXmlArticulationEntry("downbow") },
            { "falloff", new MusicXmlArticulationEntry(MusicXmlFalloffToLilyEvent) },
            { "fingering", new MusicXmlArticulationEntry(MusicXmlFingeringEvent) },
            //"fingernails": "?", "flip": "?", "fret": "?", "golpe": "?",
            //"half-muted": "?", "hammer-on": "?", "handbell": "?", "harmon-mute": "?",
            //"harmonic": handled by the note event,
            { "haydn", new MusicXmlArticulationEntry("haydnturn") },
            { "heel", new MusicXmlArticulationEntry(MusicXmlHeelEvent) },
            //"hole": "?",
            { "inverted-mordent", new MusicXmlArticulationEntry("scripts.prall", "prall") },
            {
                "inverted-turn",
                new MusicXmlArticulationEntry("scripts.reverseturn", "reverseturn")
            },
            //"inverted-vertical-turn": "?",
            { "mordent", new MusicXmlArticulationEntry("scripts.mordent", "mordent") },
            //"open": "?",
            { "open-string", new MusicXmlArticulationEntry("open") },
            //"other-ornament": "?", "other-technical": "?", "plop": "?",
            { "pluck", new MusicXmlArticulationEntry(MusicXmlPluckEvent) },
            //"pull-off": "?",
            {
                "schleifer",
                Typed(
                    MusicXmlArticulationEntry.ArticulationClass.NoDirection,
                    "bachschleifer")
            },
            //"scoop": "?", "shake": "?", "smear": "?",
            { "snap-pizzicato", new MusicXmlArticulationEntry("snappizzicato") },
            { "soft-accent", new MusicXmlArticulationEntry("espressivo") },
            //same as next
            { "spiccato", Typed(MusicXmlArticulationEntry.ArticulationClass.Short, "!") },
            //or "staccatissimo"
            {
                "staccatissimo",
                Typed(MusicXmlArticulationEntry.ArticulationClass.Short, "!")
            },
            //or "staccato"
            { "staccato", Typed(MusicXmlArticulationEntry.ArticulationClass.Short, ".") },
            //or "stopped"
            { "stopped", Typed(MusicXmlArticulationEntry.ArticulationClass.Short, "+") },
            //"stress": "?",
            { "string", new MusicXmlArticulationEntry(MusicXmlStringEvent) },
            //or "marcato"
            {
                "strong-accent",
                Typed(MusicXmlArticulationEntry.ArticulationClass.Short, "^")
            },
            //"tap": "?",
            //or "tenuto"
            { "tenuto", Typed(MusicXmlArticulationEntry.ArticulationClass.Short, "-") },
            { "thumb-position", new MusicXmlArticulationEntry("thumb") },
            { "toe", new MusicXmlArticulationEntry(MusicXmlToeEvent) },
            { "turn", new MusicXmlArticulationEntry("scripts.turn", "turn") },
            //only the single-note symbol
            { "tremolo", new MusicXmlArticulationEntry(MusicXmlTremoloToLilyEvent) },
            { "trill-mark", new MusicXmlArticulationEntry("scripts.trill", "trill") },
            //"triple-tongue": "?", "unstress": "?"
            { "up-bow", new MusicXmlArticulationEntry("upbow") },
            //"vertical-turn": "?",
            //"wavy-line": handled as spanner
        };

    /// <summary>Builds a typed-articulation table entry.</summary>
    /// <param name="articulationClass">Which class to build.</param>
    /// <param name="name">The articulation's name.</param>
    /// <returns>The entry.</returns>
    private static MusicXmlArticulationEntry Typed(
        MusicXmlArticulationEntry.ArticulationClass articulationClass, string name)
        => new MusicXmlArticulationEntry(articulationClass, name);

    /// <summary>What kind of trill and wavy line an ornament element sits in.</summary>
    /// <param name="ev">The spanner being built, whose ornament this may set.</param>
    /// <param name="mxlEvent">The element.</param>
    /// <returns>The kind, or null when neither is there.</returns>
    /// <remarks>
    /// In MusicXML, the trill symbol and the wavy line that follows are handled
    /// separately, while in LilyPond they form a single grob, <c>TrillSpanner</c>. The
    /// code here checks whether <c>&lt;trill-mark&gt;</c> is followed by (a starting)
    /// <c>&lt;wavy-line&gt;</c> and sets the spanner's ornament accordingly.
    /// </remarks>
    internal string OrnamentHasWhat(LilyTextSpannerEvent ev, MusicXmlNode mxlEvent)
    {
        MusicXmlNode wave = null;
        MusicXmlNode trill = null;
        bool ignore = false;
        bool start = false;
        bool stop = false;

        foreach (MusicXmlNode child in mxlEvent.Parent.GetAllChildren())
        {
            if (child.GetName() == "wavy-line")
            {
                wave = child;
            }
            else if (child.GetName() == "trill-mark")
            {
                trill = child;
            }

            string type = child.Attribute("type");
            if (type == "continue")
            {
                ignore = true;
            }
            else if (type == "start")
            {
                start = true;
            }
            else if (type == "stop")
            {
                stop = true;
            }
        }

        if (start)
        {
            if (wave != null && trill != null)
            {
                ev.MxlOrnament = trill;
            }
        }

        if (ignore)
        {
            return "ignore";
        }

        if (stop)
        {
            return "stop";
        }

        if (wave != null && trill != null)
        {
            return "trill and wave";
        }

        if (wave != null)
        {
            return "wave";
        }

        return trill != null ? "trill" : null;
    }

    /// <summary>Whether an ornament element sits beside a wavy line.</summary>
    /// <param name="mxlEvent">The element.</param>
    /// <returns>Whether one is there.</returns>
    internal static bool OrnamentHasWavyline(MusicXmlNode mxlEvent)
        => mxlEvent.Parent.GetAllChildren().Any(c => c.GetName() == "wavy-line");

    /// <summary>Builds the event a notation element asks for.</summary>
    /// <param name="mxlEvent">The element.</param>
    /// <param name="noteColor">The note's colour.</param>
    /// <param name="noteFontSize">The note's font size.</param>
    /// <returns>
    /// The event; null when there is nothing to build; or one of the two marker answers
    /// <c>delayed</c> and <c>unsupported</c>, which the caller tests for.
    /// </returns>
    internal (LilyMusic Event, string Marker) MusicXmlArticulationToLilyEvent(
        MusicXmlNode mxlEvent, string noteColor = null, string noteFontSize = null)
    {
        string name = mxlEvent.GetName();
        if (name == "wavy-line")
        {
            //`wavy-line' elements are treated as trill spanners, not as articulation
            //ornaments.
            return (MusicXmlSpannerToLilyEvent(mxlEvent), null);
        }

        if (name == "tremolo")
        {
            //At this point, double-note `tremolo' elements have already been handled.
            string tremoloType = mxlEvent.Attribute("type");
            if (tremoloType == "start" || tremoloType == "stop")
            {
                return (null, null);
            }
        }

        //A wavy line preceded by a trill mark gets handled as a spanner a few lines above;
        //if we see the `<trill-mark>' element, we thus pass.
        if (OrnamentHasWavyline(mxlEvent))
        {
            return (null, "delayed");
        }

        if (name == "harmonic")
        {
            State.NeededAdditionalDefinitions.Add("harmonic");
        }

        if (!ArticulationsDict.TryGetValue(name, out MusicXmlArticulationEntry entry))
        {
            return (null, "unsupported");
        }

        LilyMusic ev;
        switch (entry.Kind)
        {
            case MusicXmlArticulationEntry.EntryKind.Articulation:
            {
                LilyArticulationEvent articulation = new LilyArticulationEvent(State);
                articulation.Type = entry.Name;
                ev = articulation;
                break;
            }

            case MusicXmlArticulationEntry.EntryKind.Ornament:
            {
                LilyOrnamentEvent ornament = new LilyOrnamentEvent(State);
                //For accidental marks.
                ornament.NoteColor = noteColor;
                ornament.NoteFontSize = noteFontSize;
                ornament.OrnamentType = (entry.Glyph, entry.Command);
                ornament.YPos = mxlEvent.Attribute("default-y");
                ev = ornament;
                break;
            }

            case MusicXmlArticulationEntry.EntryKind.TypedArticulation:
            {
                LilyArticulationEvent typed
                    = entry.Class == MusicXmlArticulationEntry.ArticulationClass.Short
                        ? new LilyShortArticulationEvent(State)
                        : (LilyArticulationEvent)new LilyNoDirectionArticulationEvent(State);
                typed.Type = entry.Name;
                ev = typed;
                break;
            }

            default:
                //⚠ Upstream calls the builder with the element ALONE, so the colours it
                //would inherit default to nothing inside; the two lines below then set
                //them again from the element with the note's own as the fallback.
                ev = entry.Builder(mxlEvent, null, null);
                break;
        }

        ev.Color = mxlEvent.Attribute("color", noteColor);
        ev.FontSize = mxlEvent.Attribute("font-size", noteFontSize);

        //Some articulations use the type attribute, other the placement...
        if (!Options.NoArticulationDirections)
        {
            string placement = mxlEvent.Attribute("type") ?? mxlEvent.Attribute("placement");
            int direction = MusicXmlDirectionToIndicator(placement);
            if (direction != 0)
            {
                SetForceDirection(ev, direction);
            }
        }

        return (ev, null);
    }

    /// <summary>Sets an event's direction, whichever kind of event it is.</summary>
    /// <param name="ev">The event.</param>
    /// <param name="direction">The direction.</param>
    /// <remarks>
    /// ⚠ Upstream assigns <c>force_direction</c> on whatever the table built, and the
    /// four classes it can build each declare it. C# needs to be told which, since they
    /// do not share a base that has it.
    /// </remarks>
    private static void SetForceDirection(LilyMusic ev, int? direction)
    {
        switch (ev)
        {
            case LilyArticulationEvent articulation:
                articulation.ForceDirection = direction;
                break;
            case LilySpanEvent span:
                span.ForceDirection = direction;
                break;
            case LilyTextEvent text:
                text.ForceDirection = direction;
                break;
            case LilyMarkEvent mark:
                mark.ForceDirection = direction;
                break;
            case LilyTextMarkEvent textMark:
                textMark.ForceDirection = direction;
                break;
            case LilyDynamicsEvent dynamics:
                dynamics.ForceDirection = direction;
                break;
            case LilyTempoMark tempo:
                tempo.ForceDirection = direction;
                break;
            case LilyBreatheEvent:
            case LilyCaesuraEvent:
                //Upstream's breath and caesura events carry no direction; assigning one
                //would grow an attribute python never reads.
                break;
            default:
                throw new InvalidOperationException(
                    "No direction can be set on " + ev.GetType().Name + ".");
        }
    }
}
