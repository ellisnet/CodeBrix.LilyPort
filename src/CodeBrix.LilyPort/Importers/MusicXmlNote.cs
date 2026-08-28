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

using System.Collections.Generic;

namespace CodeBrix.LilyPort.Importers; //was previously: python/musicxml.py (Note);

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port.

/// <summary>The note element.</summary>
internal sealed class MusicXmlNote : MusicXmlMeasureElement
{
    private static readonly Dictionary<string, int> TrackedChildren
        = new Dictionary<string, int>
        {
            { "accidental", 1 },
            { "beam", 2 },
            { "chord", 1 },
            { "dot", 2 },
            { "duration", 1 },
            { "grace", 1 },
            { "instrument", 2 },
            { "lyric", 2 },
            { "notations", 2 },
            { "notehead", 1 },
            { "pitch", 1 },
            { "rest", 1 },
            { "staff", 1 },
            { "stem", 1 },
            { "type", 1 },
            { "unpitched", 1 },
            { "voice", 1 },
        };

    /// <summary>Builds the element.</summary>
    internal MusicXmlNote()
    {
        DurationValue = PythonFraction.One;
        Content["beam"] = new List<MusicXmlNode>();
        Content["dot"] = new List<MusicXmlNode>();
        Content["instrument"] = new List<MusicXmlNode>();
        Content["lyric"] = new List<MusicXmlNode>();
        Content["notations"] = new List<MusicXmlNode>();
    }

    /// <inheritdoc/>
    internal override Dictionary<string, int> MaxOccursByChild => TrackedChildren;

    /// <summary>What instrument sounds this note.</summary>
    internal string InstrumentName { get; set; } = string.Empty;

    /// <summary>Whether this note's voice is alone on its staff.</summary>
    /// <remarks>Not set for invisible notes.</remarks>
    internal bool? SingleVoice { get; set; }

    /// <summary>The arpeggio this note takes part in, across voices and staves.</summary>
    internal string ArpeggioType { get; set; }

    /// <summary>Which way a cross-staff chord reaches from this note.</summary>
    /// <remarks>'U' or 'D' when this note is part of a cross-staff chord.</remarks>
    internal string CrossStaff { get; set; }

    /// <summary>Whether this grace note follows its principal rather than preceding it.</summary>
    internal bool AfterGrace { get; set; }

    /// <summary>The moment of the note before this grace note.</summary>
    /// <remarks>
    /// An attribute upstream grows on the object at run time, and only for grace
    /// notes; C# needs it declared.
    /// </remarks>
    internal PythonFraction? PreviousWhen { get; set; }

    /// <summary>The measure position of the note before this grace note.</summary>
    internal PythonFraction? PreviousMeasurePosition { get; set; }

    /// <summary>Whether this note is a grace note that follows its principal.</summary>
    /// <returns>Whether it is.</returns>
    internal bool IsAfterGrace()
    {
        object grace = Get("grace", false);
        MusicXmlNode graceNode = grace as MusicXmlNode;
        if (graceNode == null)
        {
            return false;
        }

        return AfterGrace || graceNode.HasAttribute("steal-time-previous");
    }

    /// <summary>The note value written on this note, as a logarithm.</summary>
    /// <returns>The logarithm, or null when the document names none.</returns>
    internal int? GetDurationLog()
    {
        //<type> is optional, but profiling showed that is slightly better to treat it
        //as expected.
        if (!Has("type"))
        {
            //FIXME: is it ok to default to eight note for grace notes?
            return Has("grace") ? 3 : (int?)null;
        }

        string log = ((MusicXmlNode)Item("type")).GetText().Trim();
        return MusicXmlUtilities.MusicXmlDurationToLog(log);
    }

    /// <summary>The note value and dot count written on this note.</summary>
    /// <returns>The pair, or null when the document names no note value.</returns>
    internal (int Log, int Dots)? GetDurationInfo()
    {
        int? log = GetDurationLog();
        return log.HasValue ? (log.Value, GetList("dot").Count) : ((int, int)?)null;
    }

    /// <summary>Turns this note's length into an output-side duration.</summary>
    /// <returns>The duration, or null when there is nothing to build one from.</returns>
    internal LilyDuration InitializeDuration()
    {
        //If <note> has no <type> child, GetDurationInfo returns null (except for grace
        //notes). In that case, use the <duration> element instead. If that doesn't
        //exist either, report an error.
        (int Log, int Dots)? info = GetDurationInfo();
        if (info.HasValue)
        {
            LilyDuration duration = new LilyDuration(State);
            duration.DurationLog = info.Value.Log;
            duration.Dots = info.Value.Dots;
            //Grace notes by specification have duration 0, so no time modification
            //factor is possible. It even messes up the output with *0/1.
            if (!Has("grace"))
            {
                PythonFraction nominalDuration = duration.GetLength();
                PythonFraction actualDuration = DurationValue.Value;
                if (actualDuration != nominalDuration)
                {
                    duration.Factor = actualDuration / nominalDuration;
                }
            }

            return duration;
        }

        if (DurationValue.Value > PythonFraction.Zero)
        {
            return LilyDuration.FromFraction(State, DurationValue.Value);
        }

        Message("Encountered note at " + LilyOutputPrinter.FormatValue(When)
                + " without type and duration (="
                + LilyOutputPrinter.FormatValue(DurationValue) + ")");
        return null;
    }

    /// <summary>Turns a pitched note into an output-side event.</summary>
    /// <param name="noteColor">The note's colour.</param>
    /// <param name="noteFontSize">The note's font size.</param>
    /// <returns>The event.</returns>
    internal LilyNoteEvent InitializePitchedEvent(
        string noteColor = null, string noteFontSize = null)
    {
        LilyPitch pitch = ((MusicXmlPitch)Item("pitch")).ToLilyObject();
        LilyNoteEvent noteEvent = new LilyNoteEvent(State);
        noteEvent.Pitch = pitch;

        //<accidental> is optional, but profiling showed that is slightly better to
        //treat it as expected.
        MusicXmlNode accidental = Has("accidental") ? (MusicXmlNode)Item("accidental") : null;
        if (accidental != null)
        {
            string cautionary = accidental.Attribute("cautionary");
            string editorial = accidental.Attribute("editorial");
            string parentheses = accidental.Attribute("parentheses");
            string bracket = accidental.Attribute("bracket");

            noteEvent.AccidentalValue = accidental.GetText();

            if (cautionary == "yes")
            {
                //According to Gould's book *Behind Bars*, a cautionary accidental can
                //be an ordinary accidental, a parenthesized accidental, or an
                //accidental above the note. Here, we take care of the first two.
                if (parentheses == "no")
                {
                    noteEvent.ForcedAccidental = true;
                }
                else
                {
                    noteEvent.Cautionary = true;
                }
            }

            if (editorial == "yes" && bracket != "no")
            {
                noteEvent.Editorial = true;
            }

            if (parentheses == "yes")
            {
                noteEvent.Cautionary = true;
            }

            if (bracket == "yes")
            {
                noteEvent.Editorial = true;
            }
        }

        //⚠ python falls out of the `except KeyError' branch with `acc = []', a LIST,
        //and then asks it for attributes -- which answers the fallback every time.
        //A null node reads the same way here.
        noteEvent.AccidentalColor = accidental != null
            ? accidental.Attribute("color", noteColor) : noteColor;
        noteEvent.AccidentalFontSize = accidental != null
            ? accidental.Attribute("font-size", noteFontSize) : noteFontSize;

        //Since <harmonic> can change the shape of a note head we have to do an early
        //pass here to set some values.
        LilyHarmonicNoteEvent harmonicEvent = null;
        foreach (MusicXmlNotations notation in GetTypedChildren<MusicXmlNotations>())
        {
            foreach (MusicXmlNode technical in notation.GetNamedChildren("technical"))
            {
                foreach (MusicXmlNode harmonic in technical.GetNamedChildren("harmonic"))
                {
                    //⚠ UPSTREAM REBINDS THE OBJECT'S CLASS HERE (`event.__class__ =
                    //HarmonicNoteEvent', then `event.init()'), which C# cannot do. The
                    //port builds the harmonic event from the note event instead, ONCE,
                    //and carries on filling that -- the same object identity upstream
                    //ends up with, reached the only way this language allows.
                    if (harmonicEvent == null)
                    {
                        harmonicEvent = new LilyHarmonicNoteEvent(noteEvent);
                        noteEvent = harmonicEvent;
                    }

                    if (harmonic.GetNamedChild("natural") != null)
                    {
                        harmonicEvent.Harmonic = "natural";
                    }
                    else if (harmonic.GetNamedChild("artificial") != null)
                    {
                        harmonicEvent.Harmonic = "artificial";
                    }
                    else
                    {
                        harmonicEvent.Harmonic = "yes";
                    }

                    if (harmonic.GetNamedChild("base-pitch") != null)
                    {
                        harmonicEvent.HarmonicType = "base-pitch";
                    }
                    else if (harmonic.GetNamedChild("touching-pitch") != null)
                    {
                        harmonicEvent.HarmonicType = "touching-pitch";
                    }
                    else if (harmonic.GetNamedChild("sounding-pitch") != null)
                    {
                        harmonicEvent.HarmonicType = "sounding-pitch";
                    }

                    //These attributes are used only for the circular harmonic symbol.
                    harmonicEvent.HarmonicVisible =
                        harmonic.Attribute("print-object", "yes") == "yes";
                    harmonicEvent.HarmonicColor = harmonic.Attribute("color", noteColor);
                    harmonicEvent.HarmonicFontSize =
                        harmonic.Attribute("font-size", noteFontSize);
                }
            }
        }

        return noteEvent;
    }

    /// <summary>Turns an unpitched note into an output-side event.</summary>
    /// <param name="clef">The clef in force.</param>
    /// <returns>The event.</returns>
    internal LilyNoteEvent InitializeUnpitchedEvent(LilyClefChange clef)
    {
        //Unpitched elements can also have <display-step> and <display-octave>
        //elements.
        LilyNoteEvent noteEvent = new LilyNoteEvent(State);
        noteEvent.Pitch = ((MusicXmlUnpitched)Item("unpitched")).ToLilyObject(clef);
        return noteEvent;
    }

    /// <summary>Turns a rest into an output-side event.</summary>
    /// <param name="noteColor">The note's colour.</param>
    /// <param name="noteFontSize">The note's font size.</param>
    /// <param name="convertRestPositions">Whether the rest's placement is wanted.</param>
    /// <returns>The event.</returns>
    internal LilyRestEvent InitializeRestEvent(
        string noteColor = null, string noteFontSize = null,
        bool convertRestPositions = true)
    {
        //Rests can have <display-octave> and <display-step>, which are treated like an
        //ordinary note pitch.
        LilyRestEvent restEvent = new LilyRestEvent(State);
        restEvent.Color = Attribute("color", noteColor);
        restEvent.FontSize = Attribute("font-size", noteFontSize);

        if (convertRestPositions)
        {
            restEvent.Pitch = ((MusicXmlRest)Item("rest")).ToLilyObject();
        }

        return restEvent;
    }

    /// <summary>Turns this note into an output-side event.</summary>
    /// <param name="clef">The clef in force.</param>
    /// <param name="convertStemDirections">Whether stem directions are wanted.</param>
    /// <param name="convertRestPositions">Whether rest placements are wanted.</param>
    /// <returns>The event, or null when nothing suitable could be built.</returns>
    internal LilyRhythmicEvent ToLilyObject(
        LilyClefChange clef, bool convertStemDirections, bool convertRestPositions)
    {
        string color = Attribute("color");
        string fontSize = Attribute("font-size");

        bool isRest = false;
        LilyRhythmicEvent musicEvent;
        if (Has("pitch"))
        {
            musicEvent = InitializePitchedEvent(color, fontSize);
        }
        else if (Has("unpitched"))
        {
            musicEvent = InitializeUnpitchedEvent(clef);
        }
        else if (Has("rest"))
        {
            musicEvent = InitializeRestEvent(color, fontSize, convertRestPositions);
            isRest = true;
        }
        else
        {
            Message("cannot find suitable event");
            return null;
        }

        musicEvent.Duration = InitializeDuration();

        //LilyPond handles all dots together; we thus only use the first dot's
        //attributes.
        List<MusicXmlNode> dots = GetList("dot");
        if (dots.Count > 0)
        {
            musicEvent.DotColor = dots[0].Attribute("color", color);
            musicEvent.DotFontSize = dots[0].Attribute("font-size", fontSize);
        }

        if (!isRest)
        {
            //Technically, rests can have a <notehead> element. However, this doesn't
            //make any sense...
            MusicXmlNotehead notehead = Get("notehead") as MusicXmlNotehead;
            if (notehead == null && (color != null || fontSize != null))
            {
                notehead = new MusicXmlNotehead { State = State };
            }

            if (notehead != null)
            {
                notehead.DurationValue = DurationValue;
                foreach (LilyExpression style in notehead.ToLilyObject(color, fontSize))
                {
                    musicEvent.AddAssociatedEvent(style);
                }
            }
        }

        MusicXmlStem stem = Get("stem") as MusicXmlStem;
        if (stem == null && color != null)
        {
            stem = new MusicXmlStem { State = State };
        }

        if (stem != null)
        {
            LilyStemEvent stemEvent = stem.ToStemEvent(color, isRest, convertStemDirections);
            if (stemEvent != null)
            {
                musicEvent.AddAssociatedEvent(stemEvent);
            }
        }

        musicEvent.Visible = Attribute("print-object", "yes") == "yes";
        musicEvent.Spacing = Attribute("print-spacing", "yes") == "yes";
        if (!isRest)
        {
            musicEvent.Ledger = Attribute("print-leger", "yes") == "yes";
        }

        musicEvent.PrintDot = Attribute("print-dot", "yes") == "yes";

        return musicEvent;
    }
}
