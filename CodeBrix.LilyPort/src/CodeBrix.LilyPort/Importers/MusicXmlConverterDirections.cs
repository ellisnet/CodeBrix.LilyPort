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

namespace CodeBrix.LilyPort.Importers; //was previously: scripts/musicxml2ly.py (the dynamics, words, dashes, mark, accordion, harp-pedal and metronome builders, and musicxml_direction_to_lily);

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port.

internal sealed partial class MusicXmlConverter
{
    /// <summary>The dynamics LilyPond provides by default.</summary>
    private static readonly HashSet<string> PredefinedDynamics
        = new HashSet<string>(StringComparer.Ordinal)
        {
            "ppppp", "pppp", "ppp", "pp", "p",
            "mp", "mf",
            "f", "ff", "fff", "ffff", "fffff",
            "fp", "sf", "sfp", "sff",
            "sfz", "fz", "sp", "spp", "rfz",
            "n",
        };

    private static readonly HashSet<string> DirectionTypeSpanners
        = new HashSet<string>(StringComparer.Ordinal)
        {
            "bracket",
            "octave-shift",
            "pedal",
            //'principal-voice',
            "wedge",
        };

    /// <summary>The name one child of a dynamics element contributes.</summary>
    /// <param name="element">The child.</param>
    /// <returns>The name.</returns>
    internal static string MusicXmlDynamicToLily(MusicXmlNode element)
    {
        string dynamicsName = element.GetName();
        if (dynamicsName == "other-dynamics")
        {
            //TODO (upstream's): Handle `smufl' attribute.
            dynamicsName = element.GetText();
        }

        return string.IsNullOrEmpty(dynamicsName) || dynamicsName == "#text"
            ? string.Empty
            : dynamicsName;
    }

    /// <summary>Builds the dynamics mark a run of direction children asks for.</summary>
    /// <param name="elements">The children and their carried-over attributes.</param>
    /// <returns>The event.</returns>
    /// <remarks>The note's colour and font size are deliberately not inherited.</remarks>
    internal LilyMusic MusicXmlDynamicsToLilyEvent(List<LilyMarkupElement> elements)
    {
        int dynIndex = elements.FindIndex(e => e.Element.GetName() == "dynamics");

        List<LilyMarkupElement> beforeTextElements = elements.Take(dynIndex).ToList();
        List<LilyMarkupElement> afterTextElements = elements.Skip(dynIndex + 1).ToList();

        MusicXmlNode dynamics = elements[dynIndex].Element;
        Dictionary<string, object> attributes = elements[dynIndex].Attributes;

        //At this point, the attributes for all elements have already been set or derived
        //from previous elements. We thus can manipulate a single element's attributes
        //without having side effects.
        if (Options.DynamicsScale.HasValue)
        {
            if (Options.DynamicsScale.Value == 0)
            {
                attributes.Remove("font-size");
            }
            else
            {
                attributes["font-size-scale"] = Options.DynamicsScale.Value;
            }
        }

        //Construct a name for the dynamics object.
        //
        //TODO (upstream's): The code below is slightly problematic currently since we only
        //take the `enclosure' attribute into account. While in 'normal' scores it is
        //unlikely to find, say, an 'f' in two different fonts (or colors, or sizes), this
        //actually does happen in critical editions to make a distinction between original
        //dynamics written by the composer and dynamics added by the editor.
        string dynamicsName = string.Empty;

        string before = string.Empty;
        foreach (LilyMarkupElement pair in beforeTextElements)
        {
            before += pair.Element.GetText();
        }

        dynamicsName += before;

        string dyns = string.Empty;
        foreach (MusicXmlNode child in dynamics.GetAllChildren())
        {
            dyns += MusicXmlDynamicToLily(child);
        }

        dynamicsName += dyns;

        string after = string.Empty;
        foreach (LilyMarkupElement pair in afterTextElements)
        {
            after += pair.Element.GetText();
        }

        dynamicsName += after;

        string enclosure = GetAttribute(attributes, "enclosure", "none");
        if (enclosure != "none")
        {
            dynamicsName += " (" + enclosure + ")";
        }

        dynamicsName = MusicXmlUtilities.EscapeLyOutputString(dynamicsName);
        string dynamicsString = MusicXmlUtilities.EscapeLyOutputString(dyns);

        LilyDynamicsEvent ev = new LilyDynamicsEvent(State);

        //TODO (upstream's): Handle more `attributes' elements.
        if (PredefinedDynamics.Contains(dynamicsName))
        {
            ev.Color = GetAttribute(attributes, "color", null);
            ev.FontSize = GetAttribute(attributes, "font-size", null);
            ev.FontSizeScale = GetScale(attributes);
        }
        else if (before.Length > 0 || after.Length > 0 || enclosure != "none")
        {
            List<string> markup = new List<string>();
            Dictionary<string, object> markupAttributes = new Dictionary<string, object>();

            if (before.Length > 0 || after.Length > 0)
            {
                markup.Add("\\dynamic");
            }
            else
            {
                foreach (KeyValuePair<string, object> entry in attributes)
                {
                    markupAttributes[entry.Key] = entry.Value;
                }
            }

            markup.Add(dynamicsString);

            MusicXmlLilyPondMarkup markupNode = new MusicXmlLilyPondMarkup { State = State };
            markupNode.Data = string.Join(" ", markup);
            if (enclosure != "none")
            {
                markupAttributes["enclosure"] = enclosure;
            }

            List<LilyMarkupElement> textElements = new List<LilyMarkupElement>();
            textElements.AddRange(beforeTextElements);
            textElements.Add(new LilyMarkupElement(markupNode, markupAttributes));
            textElements.AddRange(afterTextElements);

            string initMarkup = before.Length > 0 || after.Length > 0
                ? "\\normal-text"
                : null;
            string dynamicsMarkup = LilyMarkup.TextToLy(State, textElements, initMarkup);

            State.AdditionalMacros[dynamicsName] =
                dynamicsName + " =\n"
                + "#(make-dynamic-script #{\n"
                + "  \\markup {\n"
                + "    " + dynamicsMarkup + "\n"
                + "  }\n"
                + "#})";
        }
        else
        {
            State.AdditionalMacros[dynamicsName] =
                dynamicsName + " = #(make-dynamic-script \"" + dynamicsString + "\")";
            ev.Color = GetAttribute(attributes, "color", null);
            ev.FontSize = GetAttribute(attributes, "font-size", null);
            ev.FontSizeScale = GetScale(attributes);
        }

        ev.Type = dynamicsName;

        return ev;
    }

    /// <summary>The dynamics scale factor a carried-over attribute map holds.</summary>
    /// <param name="attributes">The attributes.</param>
    /// <returns>The factor, or one when the map holds none.</returns>
    private static double GetScale(Dictionary<string, object> attributes)
        => attributes.TryGetValue("font-size-scale", out object value) && value is double scale
            ? scale
            : 1.0;

    /// <summary>Builds the free text a run of words elements asks for.</summary>
    /// <param name="elements">The children and their carried-over attributes.</param>
    /// <returns>The event.</returns>
    internal LilyMusic MusicXmlWordsToLilyEvent(List<LilyMarkupElement> elements)
    {
        LilyTextEvent ev = new LilyTextEvent(State);
        ev.TextElements = elements;
        return ev;
    }

    /// <summary>Builds the spanner a starting dashes element asks for.</summary>
    /// <param name="elements">The children and their carried-over attributes.</param>
    /// <returns>The event.</returns>
    internal LilyMusic MusicXmlDashesStartToLilyEvent(List<LilyMarkupElement> elements)
        => MusicXmlDashesToLilyEvent(elements, "start");

    /// <summary>Builds the spanner a stopping dashes element asks for.</summary>
    /// <param name="elements">The children and their carried-over attributes.</param>
    /// <returns>The event.</returns>
    internal LilyMusic MusicXmlDashesStopToLilyEvent(List<LilyMarkupElement> elements)
        => MusicXmlDashesToLilyEvent(elements, "stop");

    /// <summary>Builds the spanner a dashes element asks for.</summary>
    /// <param name="elements">The children and their carried-over attributes.</param>
    /// <param name="type">Which end of the span this is.</param>
    /// <returns>The event.</returns>
    internal LilyMusic MusicXmlDashesToLilyEvent(
        List<LilyMarkupElement> elements, string type)
    {
        LilyMarkupElement pair;
        if (type == "start")
        {
            pair = elements[elements.Count - 1];
            elements = elements.Take(elements.Count - 1).ToList();
        }
        else
        {
            pair = elements[0];
            elements = elements.Skip(1).ToList();
        }

        LilySpanEvent ev = MusicXmlSpannerToLilyEvent(pair.Element, pair.Attributes);
        ev.TextElements = elements;

        return ev;
    }

    /// <summary>Builds the crescendo spanner a run of direction children asks for.</summary>
    /// <param name="elements">The children and their carried-over attributes.</param>
    /// <returns>The event.</returns>
    internal LilyMusic MusicXmlCrescSpannerToLilyEvent(List<LilyMarkupElement> elements)
    {
        State.NeededAdditionalDefinitions.Add("crescendo");
        return MusicXmlDynamicsSpannerToLilyEvent(elements, "cresc");
    }

    /// <summary>Builds the decrescendo spanner a run of direction children asks for.</summary>
    /// <param name="elements">The children and their carried-over attributes.</param>
    /// <returns>The event.</returns>
    internal LilyMusic MusicXmlDimSpannerToLilyEvent(List<LilyMarkupElement> elements)
    {
        State.NeededAdditionalDefinitions.Add("decrescendo");
        return MusicXmlDynamicsSpannerToLilyEvent(elements, "dim");
    }

    /// <summary>Builds a dynamics spanner.</summary>
    /// <param name="elements">The children and their carried-over attributes.</param>
    /// <param name="type">Which of the two kinds of spanner this is.</param>
    /// <returns>The event.</returns>
    internal LilyMusic MusicXmlDynamicsSpannerToLilyEvent(
        List<LilyMarkupElement> elements, string type)
    {
        LilyMarkupElement pair = elements[elements.Count - 1];
        elements = elements.Take(elements.Count - 1).ToList();

        LilyDynamicsSpannerEvent ev = (LilyDynamicsSpannerEvent)MusicXmlSpannerToLilyEvent(
            pair.Element, pair.Attributes, "dynamics-spanner");
        ev.TextElements = elements;
        ev.Type = type;

        return ev;
    }

    /// <summary>Builds the end of a written-out dynamics spanner.</summary>
    /// <param name="elements">The children and their carried-over attributes.</param>
    /// <returns>The event.</returns>
    internal LilyMusic MusicXmlCrescDimStopToLilyEvent(List<LilyMarkupElement> elements)
    {
        LilyDynamicsSpannerEvent ev = new LilyDynamicsSpannerEvent(State);
        ev.TextElements = elements;
        ev.SpanDirection = 1;
        return ev;
    }

    /// <summary>Builds the rehearsal mark a run of direction children asks for.</summary>
    /// <param name="elements">The children and their carried-over attributes.</param>
    /// <returns>The event.</returns>
    internal LilyMusic MusicXmlMarkToLilyEvent(List<LilyMarkupElement> elements)
    {
        LilyMarkEvent ev = new LilyMarkEvent(State);
        ev.TextElements = elements;
        return ev;
    }

    /// <summary>Builds the text mark a run of direction children asks for.</summary>
    /// <param name="elements">The children and their carried-over attributes.</param>
    /// <returns>The event.</returns>
    /// <remarks>
    /// TODO (upstream's): the class answers false to "wait for note", which means that it
    /// gets emitted immediately. However, at the end of music, we should use
    /// <c>\textEndMark</c> instead so that it doesn't get ignored by LilyPond.
    /// </remarks>
    internal LilyMusic MusicXmlTextMarkToLilyEvent(List<LilyMarkupElement> elements)
    {
        State.LayoutInformation.SetContextItem(
            "Score", "\\override TextMark.font-size = 2");

        LilyTextMarkEvent ev = new LilyTextMarkEvent(State);
        ev.TextElements = elements;
        return ev;
    }

    /// <summary>Builds the markup command an accordion registration asks for.</summary>
    /// <param name="mxlEvent">The element.</param>
    /// <returns>The command's name, with its backslash.</returns>
    /// <remarks>
    /// Since LilyPond does not have any built-in commands, we need to create the markup
    /// commands manually and define our own variables. Idea was taken from
    /// http://lsr.dsi.unimi.it/LSR/Item?id=194.
    /// </remarks>
    internal string MusicXmlAccordionToMarkup(MusicXmlNode mxlEvent)
    {
        string commandName = "accReg";
        string command = string.Empty;

        MusicXmlNode high = mxlEvent.GetMaybeExistNamedChild("accordion-high");
        if (high != null)
        {
            commandName += "H";
            command += "\\combine\n          \\raise #2.5 \\musicglyph #\"accordion.dot\"\n          ";
        }

        MusicXmlNode middle = mxlEvent.GetMaybeExistNamedChild("accordion-middle");
        if (middle != null)
        {
            //By default, use one dot (when no or invalid content is given). The MusicXML
            //spec is quiet about this case...
            int text = 1;
            if (int.TryParse(
                    middle.GetText().Trim(), NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out int parsed))
            {
                text = parsed;
            }

            if (text == 3)
            {
                commandName += "MMM";
                command += "\\combine\n          \\raise #1.5 \\musicglyph \"accordion.dot\"\n"
                           + "          \\combine\n          \\raise #1.5 \\translate #(cons 1 0) \\musicglyph \"accordion.dot\"\n"
                           + "          \\combine\n          \\raise #1.5 \\translate #(cons -1 0) \\musicglyph \"accordion.dot\"\n"
                           + "          ";
            }
            else if (text == 2)
            {
                commandName += "MM";
                command += "\\combine\n          \\raise #1.5 \\translate #(cons 0.5 0) \\musicglyph \"accordion.dot\"\n"
                           + "          \\combine\n          \\raise #1.5 \\translate #(cons -0.5 0) \\musicglyph \"accordion.dot\"\n"
                           + "          ";
            }
            else if (!(text <= 0))
            {
                commandName += "M";
                command += "\\combine\n          \\raise #1.5 \\musicglyph \"accordion.dot\"\n          ";
            }
        }

        MusicXmlNode low = mxlEvent.GetMaybeExistNamedChild("accordion-low");
        if (low != null)
        {
            commandName += "L";
            command += "\\combine\n          \\raise #0.5 \\musicglyph \"accordion.dot\"\n          ";
        }

        command += "\\musicglyph \"accordion.discant\"";
        command = "\\markup { \\normalsize " + command + " }";
        //Define the newly built command \accReg[H][MMM][L]
        State.AdditionalMacros[commandName] = commandName + " = " + command;
        return "\\" + commandName;
    }

    /// <summary>Builds the text mark an accordion registration asks for.</summary>
    /// <param name="mxlEvent">The element.</param>
    /// <returns>The event.</returns>
    internal LilyMusic MusicXmlAccordionToLy(MusicXmlNode mxlEvent)
    {
        string text = MusicXmlAccordionToMarkup(mxlEvent);
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        MusicXmlLilyPondMarkup markupNode = new MusicXmlLilyPondMarkup { State = State };
        markupNode.Data = text;

        LilyTextMarkEvent ev = new LilyTextMarkEvent(State);
        ev.TextElements = new List<LilyMarkupElement>
        {
            new LilyMarkupElement(markupNode, new Dictionary<string, object>()),
        };

        return ev;
    }

    /// <summary>Builds the markup a harp-pedals element asks for.</summary>
    /// <param name="mxlEvent">The element.</param>
    /// <returns>The event.</returns>
    internal LilyMusic MusicXmlHarpPedalsToLy(MusicXmlNode mxlEvent)
    {
        int count = 0;
        string result = "\\harp-pedal #\"";
        foreach (MusicXmlNode tuning in mxlEvent.GetNamedChildren("pedal-tuning"))
        {
            MusicXmlNode alter = tuning.GetNamedChild("pedal-alter");
            if (alter != null)
            {
                int val = int.Parse(
                    alter.GetText().Trim(), CultureInfo.InvariantCulture);
                result += val switch { 1 => "v", 0 => "-", -1 => "^", _ => string.Empty };
            }

            count += 1;
            if (count == 3)
            {
                result += "|";
            }
        }

        LilyMarkupEvent ev = new LilyMarkupEvent(State);
        ev.Contents = result + "\"";
        return ev;
    }

    /// <summary>Builds the text mark an eyeglasses element asks for.</summary>
    /// <param name="mxlEvent">The element.</param>
    /// <returns>The event.</returns>
    internal LilyMusic MusicXmlEyeglassesToLy(MusicXmlNode mxlEvent)
    {
        State.NeededAdditionalDefinitions.Add("eyeglasses");

        MusicXmlLilyPondMarkup markupNode = new MusicXmlLilyPondMarkup { State = State };
        markupNode.Data = "\\eyeglasses";

        LilyTextMarkEvent ev = new LilyTextMarkEvent(State);
        ev.TextElements = new List<LilyMarkupElement>
        {
            new LilyMarkupElement(markupNode, new Dictionary<string, object>()),
        };

        return ev;
    }

    /// <summary>The next child that is not a run of text.</summary>
    /// <param name="list">The children.</param>
    /// <param name="pos">Where to start looking, exclusive.</param>
    /// <returns>The index, which may be past the end.</returns>
    private static int NextNonHashIndex(List<MusicXmlNode> list, int pos)
    {
        pos += 1;
        while (pos < list.Count && list[pos] is MusicXmlHashText)
        {
            pos += 1;
        }

        return pos;
    }

    /// <summary>Builds the tempo mark a run of direction children asks for.</summary>
    /// <param name="elements">The children and their carried-over attributes.</param>
    /// <returns>The event.</returns>
    internal LilyMusic MusicXmlMetronomeToLilyEvent(List<LilyMarkupElement> elements)
    {
        MusicXmlNode maybeMetronome = elements[elements.Count - 1].Element;
        Dictionary<string, object> attributes = elements[elements.Count - 1].Attributes;

        bool tempoWithMetronome = false;
        List<MusicXmlNode> children = null;
        if (maybeMetronome is MusicXmlMetronome)
        {
            children = maybeMetronome.GetAllChildren();
            if (children.Count > 0)
            {
                tempoWithMetronome = true;
            }
            else
            {
                State.Warning("Empty metronome element");
            }
        }

        LilyTempoMark ev = new LilyTempoMark(State);

        if (!tempoWithMetronome)
        {
            ev.TextElements = elements;
            return ev;
        }

        if (GetAttribute(attributes, "parentheses", "no") == "yes")
        {
            ev.Parentheses = true;
        }

        if (GetAttribute(attributes, "print-object", "yes") == "no")
        {
            ev.Visible = false;
        }

        //We extend MusicXML by accepting a carried-over `enclosure' attribute for the
        //metronome mark.
        string enclosureAttribute = GetAttribute(attributes, "enclosure", "none");
        if (enclosureAttribute != "none")
        {
            ev.Enclosure = enclosureAttribute;
        }

        if (elements.Count > 1)
        {
            ev.TextElements = elements.Take(elements.Count - 1).ToList();
        }

        int numChildren = children.Count;
        bool complex = false;
        int index = -1;
        index = NextNonHashIndex(children, index);
        if (index < numChildren && children[index] is MusicXmlBeatUnit)
        {
            //For flow control.
            while (true)
            {
                LilyDuration newDuration = null;
                int? bpm = null;

                //The simple form of a metronome mark.
                LilyDuration duration = new LilyDuration(State);
                duration.DurationLog = MusicXmlUtilities.MusicXmlDurationToLog(
                    children[index].GetText());
                index = NextNonHashIndex(children, index);
                while (index < numChildren && children[index] is MusicXmlBeatUnitDot)
                {
                    duration.Dots += 1;
                    index = NextNonHashIndex(children, index);
                }

                if (index >= numChildren)
                {
                    ev.BaseDuration = duration;
                    ev.NewDuration = null;
                    ev.Bpm = null;
                    break;
                }

                if (children[index] is MusicXmlBeatUnitTied)
                {
                    complex = true;
                    break;
                }

                if (children[index] is MusicXmlBeatUnit)
                {
                    //Form "note = newnote".
                    newDuration = new LilyDuration(State);
                    newDuration.DurationLog = MusicXmlUtilities.MusicXmlDurationToLog(
                        children[index].GetText());
                    index = NextNonHashIndex(children, index);
                    while (index < numChildren && children[index] is MusicXmlBeatUnitDot)
                    {
                        newDuration.Dots += 1;
                        index = NextNonHashIndex(children, index);
                    }

                    if (index < numChildren && children[index] is MusicXmlBeatUnitTied)
                    {
                        complex = true;
                        break;
                    }
                }
                else if (children[index] is MusicXmlPerMinute)
                {
                    //Form "note = bpm".
                    if (int.TryParse(
                            children[index].GetText().Trim(), NumberStyles.Integer,
                            CultureInfo.InvariantCulture, out int parsed))
                    {
                        bpm = parsed;
                    }
                    else
                    {
                        State.Warning("Invalid bpm value in metronome mark");
                        bpm = 0;
                    }
                }
                else
                {
                    State.Warning("Unknown metronome mark, ignoring");
                    break;
                }

                ev.BaseDuration = duration;
                ev.NewDuration = newDuration;
                ev.Bpm = bpm;
                break;
            }
        }
        else
        {
            complex = true;
        }

        if (complex)
        {
            //TODO (upstream's): Implement the other (more complex) way for tempo marks.
            State.Warning(
                "Metronome marks with complex relations (<metronome-note> in MusicXML) "
                + "are not yet implemented.");
        }

        return ev;
    }

    /// <summary>Builds the events one direction element asks for.</summary>
    /// <param name="n">The direction element.</param>
    /// <returns>The events.</returns>
    /// <remarks>
    /// TODO (upstream's): Handle the <c>&lt;staff&gt;</c> element.
    /// <para>
    /// We apply some heuristics to convert children of <c>&lt;direction&gt;</c> into
    /// something meaningful. In general, a <c>&lt;dashes&gt;</c> element, with
    /// <c>&lt;words&gt;</c> before a 'start' element or <c>&lt;words&gt;</c> after a
    /// 'stop' element, translates to <c>\startTextSpan</c>; however, if it is followed by
    /// <c>&lt;dynamics&gt;</c>, or if the text before the 'start' element contains either
    /// 'cresc', 'dim', or 'decresc', we translate it to a dynamics line spanner. A single
    /// <c>&lt;dynamics&gt;</c> element builds an argument to <c>make-dynamic-script</c>;
    /// multiple <c>&lt;rehearsal&gt;</c> elements in a row become a <c>\mark</c> command;
    /// <c>&lt;segno&gt;</c> and <c>&lt;coda&gt;</c> build a <c>\textMark</c> markup; a
    /// <c>&lt;metronome&gt;</c> element builds a <c>\tempo</c> command; and a series of
    /// only <c>&lt;words&gt;</c> builds a <c>\markup</c> command, except when
    /// <c>&lt;direction&gt;</c> carries <c>directive="yes"</c>, which makes it a
    /// <c>\tempo</c> command instead.
    /// </para>
    /// </remarks>
    internal List<LilyMusic> MusicXmlDirectionToLily(MusicXmlDirection n)
    {
        List<LilyMusic> res = new List<LilyMusic>();

        //The `placement' attribute applies to all children.
        int? dir = null;
        if (!Options.NoArticulationDirections)
        {
            string placement = n.Attribute("placement");
            if (placement != null)
            {
                dir = MusicXmlDirectionToIndicator(placement);
            }
        }

        bool directive = n.Attribute("directive", "no") == "yes";

        List<MusicXmlNode> dirtypeChildren = new List<MusicXmlNode>();
        Dictionary<string, object> attributes = new Dictionary<string, object>();

        foreach (MusicXmlDirType dirType in n.GetTypedChildren<MusicXmlDirType>())
        {
            dirtypeChildren.AddRange(dirType.GetAllChildren());
        }

        //Also handle children of 'chained' `<direction>' element.
        if (n.Next != null)
        {
            foreach (MusicXmlDirType dirType in n.Next.GetTypedChildren<MusicXmlDirType>())
            {
                dirtypeChildren.AddRange(dirType.GetAllChildren());
            }
        }

        dirtypeChildren = dirtypeChildren.Where(d => d.GetName() != "#text").ToList();

        int numChildren = dirtypeChildren.Count;
        int i = 0;
        while (i < numChildren)
        {
            MusicXmlNode entry = dirtypeChildren[i];

            //We store `<direction-type>' children together with the carried-over
            //attributes.
            List<LilyMarkupElement> elements = new List<LilyMarkupElement>();

            bool rehearsalEnclosureDefault = true;
            int maybeDashesStopIndex = -1;
            string maybeDynamicsSpanner = null;

            int nc = i;
            string state = "words";
            while (nc < numChildren)
            {
                MusicXmlNode elem = dirtypeChildren[nc];
                string name = elem.GetName();

                //We use nothing as the default so that we can check whether the attribute
                //is set at all.
                string enclosure = elem.Attribute("enclosure");
                if (enclosure != null)
                {
                    rehearsalEnclosureDefault = false;
                }

                //Update attributes with data from current element.
                foreach (KeyValuePair<string, string> attribute in elem.AttributeDict)
                {
                    if (!FormattingAttributesToIgnore.Contains(attribute.Key))
                    {
                        attributes[attribute.Key] = attribute.Value;
                    }
                }

                if (state == "cresc-spanner")
                {
                    break;
                }

                if (state == "dashes-start")
                {
                    break;
                }

                if (state == "dashes-stop")
                {
                    if (name == "words")
                    {
                        //Keep collecting.
                    }
                    else if (name == "dynamics")
                    {
                        nc = maybeDashesStopIndex + 1;
                        break;
                    }
                    else
                    {
                        break;
                    }
                }
                else if (state == "dim-spanner")
                {
                    break;
                }
                else if (state == "dynamics")
                {
                    if (name != "words")
                    {
                        break;
                    }
                }
                else if (state == "cresc-dim-stop")
                {
                    break;
                }
                else if (state == "mark")
                {
                    if (name == "words")
                    {
                        if (rehearsalEnclosureDefault)
                        {
                            attributes.Remove("enclosure");
                        }

                        state = "post-mark";
                    }
                    else if (name == "rehearsal")
                    {
                        //Keep collecting.
                    }
                    else
                    {
                        break;
                    }
                }
                else if (state == "post-mark")
                {
                    if (name != "words")
                    {
                        break;
                    }
                }
                else if (state == "textmark")
                {
                    if (name != "words" && name != "segno" && name != "coda")
                    {
                        break;
                    }
                }
                else if (state == "metronome")
                {
                    break;
                }
                else if (state == "words")
                {
                    if (name == "words")
                    {
                        //This is awkward, but MusicXML doesn't make a distinction whether
                        //dashes are used for, say, either 'cresc.' or 'rit.'.
                        string text = elem.GetText();
                        if (PythonRegex.Search(@"(?x) (?<! \w) cresc", text).Success)
                        {
                            maybeDynamicsSpanner = "cresc";
                        }
                        else if (PythonRegex.Search(@"(?x) (?<! \w) ( decr | dim )", text)
                                 .Success)
                        {
                            maybeDynamicsSpanner = "dim";
                        }
                    }
                    else if (name == "rehearsal")
                    {
                        if (rehearsalEnclosureDefault)
                        {
                            attributes["enclosure"] = "square";
                        }

                        state = "mark";
                    }
                    else if (name == "segno" || name == "coda")
                    {
                        state = "textmark";
                    }
                    else if (name == "dynamics")
                    {
                        state = "dynamics";
                    }
                    else if (name == "metronome")
                    {
                        state = "metronome";
                    }
                    else if (name == "dashes")
                    {
                        string dashesType = elem.Attribute("type");
                        if (dashesType == "start")
                        {
                            state = maybeDynamicsSpanner switch
                            {
                                "cresc" => "cresc-spanner",
                                "dim" => "dim-spanner",
                                _ => "dashes-start",
                            };
                        }
                        else if (dashesType == "stop")
                        {
                            state = "dashes-stop";
                            maybeDashesStopIndex = nc;
                            MusicXmlSpanner dashes = elem as MusicXmlSpanner;
                            if (dashes != null && dashes.PairedWith != null)
                            {
                                object paired = dashes.PairedWith.SpannerEvent;
                                if (paired != null
                                    && paired.GetType() == typeof(LilyDynamicsSpannerEvent))
                                {
                                    //Don't add `<words>' at the right of a dynamics
                                    //spanner.
                                    state = "cresc-dim-stop";
                                }
                            }
                        }
                        else
                        {
                            break;
                        }
                    }
                    else
                    {
                        break;
                    }
                }

                string carriedEnclosure = GetAttribute(attributes, "enclosure", null);
                if (carriedEnclosure != null && ExtraEnclosures.Contains(carriedEnclosure))
                {
                    State.NeededAdditionalDefinitions.Add(carriedEnclosure);
                }

                elements.Add(
                    new LilyMarkupElement(
                        elem, new Dictionary<string, object>(attributes)));
                nc += 1;
            }

            if (state == "words")
            {
                if (directive)
                {
                    state = "metronome";
                }
            }
            else if (state == "post-mark")
            {
                state = "mark";
            }

            if (elements.Count > 0)
            {
                LilyMusic ev = BuildDirectionEvent(state, elements);
                if (ev != null)
                {
                    SetEventOffset(ev, n);
                    SetForceDirection(ev, dir);
                    res.Add(ev);
                }

                i = nc;
                continue;
            }

            //At this point, the attributes map is up to date since the start of the
            //previous inner loop gets always executed.
            if (DirectionTypeSpanners.Contains(entry.GetName()))
            {
                LilySpanEvent ev = MusicXmlSpannerToLilyEvent(entry);
                //TODO (upstream's): Use the carried-over attributes.
                if (ev != null)
                {
                    //Don't apply `<offset>' to ottavation, which is always tied to a note
                    //(or rest). Consequently, a horizontal shift is just a layout
                    //correction, which we ignore.
                    if (!(ev is LilyOctaveShiftEvent))
                    {
                        SetEventOffset(ev, n);
                    }

                    if (ev.SpanDirection == -1)
                    {
                        ev.ForceDirection = dir;
                    }

                    res.Add(ev);
                }

                i += 1;
                continue;
            }

            //Everything else is taken as a single command (and ignored otherwise if
            //`musicxml2ly' can't handle it).
            LilyMusic directionEvent = entry.GetName() switch
            {
                "accordion-registration" => MusicXmlAccordionToLy(entry),
                //'damp': TODO (upstream's)
                //'damp-all': TODO (upstream's)
                "eyeglasses" => MusicXmlEyeglassesToLy(entry),
                "harp-pedals" => MusicXmlHarpPedalsToLy(entry),
                //'image': TODO (upstream's)
                //'other-direction': TODO (upstream's)
                //'percussion': TODO (upstream's)
                //'scordatura': TODO (upstream's)
                //'staff-divide': TODO (upstream's)
                //'string-mute': TODO (upstream's)
                _ => null,
            };

            if (directionEvent != null)
            {
                //TODO (upstream's): Use the carried-over attributes.
                SetEventOffset(directionEvent, n);
                SetForceDirection(directionEvent, dir);
                res.Add(directionEvent);
            }

            i += 1;
        }

        return res;
    }

    /// <summary>Builds the event one parser state names.</summary>
    /// <param name="state">The state.</param>
    /// <param name="elements">The children and their carried-over attributes.</param>
    /// <returns>The event.</returns>
    private LilyMusic BuildDirectionEvent(string state, List<LilyMarkupElement> elements)
        => state switch
        {
            "cresc-dim-stop" => MusicXmlCrescDimStopToLilyEvent(elements),
            "cresc-spanner" => MusicXmlCrescSpannerToLilyEvent(elements),
            "dashes-start" => MusicXmlDashesStartToLilyEvent(elements),
            "dashes-stop" => MusicXmlDashesStopToLilyEvent(elements),
            "dim-spanner" => MusicXmlDimSpannerToLilyEvent(elements),
            "dynamics" => MusicXmlDynamicsToLilyEvent(elements),
            "mark" => MusicXmlMarkToLilyEvent(elements),
            "metronome" => MusicXmlMetronomeToLilyEvent(elements),
            "textmark" => MusicXmlTextMarkToLilyEvent(elements),
            "words" => MusicXmlWordsToLilyEvent(elements),
            _ => throw new InvalidOperationException(
                "No direction event is defined for the state '" + state + "'."),
        };

    /// <summary>Gives an event the offset its direction element asks for.</summary>
    /// <param name="ev">The event.</param>
    /// <param name="n">The direction element.</param>
    /// <remarks>
    /// ⚠ Upstream reaches for <c>event.mxl_event._parent._parent._offset</c> inside a
    /// <c>try</c> that catches <c>AttributeError</c>, so an event without an element of
    /// its own — or one whose grandparent is not a direction — falls back to the
    /// direction it is being built for. An event whose offset ends up unset keeps
    /// <c>None</c>, which every reader treats exactly as zero, so the port stores zero.
    /// </remarks>
    private static void SetEventOffset(LilyMusic ev, MusicXmlDirection n)
    {
        MusicXmlNode mxlEvent = (ev as LilySpanEvent)?.MxlEvent;
        PythonFraction? offset = mxlEvent?.Parent?.Parent is MusicXmlDirection owner
            ? owner.Offset
            : n.Offset;

        if (offset.HasValue)
        {
            offset = offset.Value + n.When.Value;
        }

        ((ILilyOffsetEvent)ev).Offset = offset ?? PythonFraction.Zero;
    }
}
