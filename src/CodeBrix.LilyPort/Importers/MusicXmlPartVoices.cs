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

namespace CodeBrix.LilyPort.Importers; //was previously: python/musicxml.py (Part.extract_voices);

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port.

internal sealed partial class MusicXmlPart
{
    /// <summary>One key of upstream's ordered voice dictionary.</summary>
    /// <remarks>
    /// A staff, a voice, which way a cross-staff chord reaches, and whether the
    /// element is a grace. C# tuples compare by value, which is what python's tuple
    /// keys do.
    /// </remarks>
    private readonly struct VoiceKey : IEquatable<VoiceKey>
    {
        internal VoiceKey(string staffId, string voiceId, string crossStaffDirection, bool grace)
        {
            StaffId = staffId;
            VoiceId = voiceId;
            CrossStaffDirection = crossStaffDirection;
            Grace = grace;
        }

        internal string StaffId { get; }

        internal string VoiceId { get; }

        internal string CrossStaffDirection { get; }

        internal bool Grace { get; }

        public bool Equals(VoiceKey other)
            => StaffId == other.StaffId
               && VoiceId == other.VoiceId
               && CrossStaffDirection == other.CrossStaffDirection
               && Grace == other.Grace;

        public override bool Equals(object obj) => obj is VoiceKey other && Equals(other);

        public override int GetHashCode()
            => HashCode.Combine(StaffId, VoiceId, CrossStaffDirection, Grace);
    }

    private void LinkSpanners(
        List<MusicXmlNode> elements, Type[] structure, string type, bool oneChild = true)
    {
        Dictionary<string, MusicXmlSpanner> spannerStarts
            = new Dictionary<string, MusicXmlSpanner>(StringComparer.Ordinal);
        foreach (MusicXmlNode element in elements)
        {
            LinkSpannersInner(element, structure, 0, type, oneChild, spannerStarts);
        }
    }

    private void LinkSpannersInner(
        MusicXmlNode element, Type[] structure, int depth, string type, bool oneChild,
        Dictionary<string, MusicXmlSpanner> spannerStarts)
    {
        if (!structure[depth].IsInstanceOfType(element))
        {
            return;
        }

        if (structure.Length - depth > 1)
        {
            foreach (MusicXmlNode child in element.Children)
            {
                if (structure[depth + 1].IsInstanceOfType(child))
                {
                    LinkSpannersInner(
                        child, structure, depth + 1, type, oneChild, spannerStarts);
                }
            }

            return;
        }

        List<MusicXmlNode> spanners;
        if (oneChild)
        {
            MusicXmlNode single = element.GetNamedChild(type);
            spanners = single != null
                ? new List<MusicXmlNode> { single }
                : new List<MusicXmlNode>();
        }
        else
        {
            spanners = element.GetNamedChildren(type);
        }

        foreach (MusicXmlNode node in spanners)
        {
            MusicXmlSpanner spanner = (MusicXmlSpanner)node;
            string nr = spanner.Attribute("number", "1");
            string spannerType = spanner.Attribute("type");
            if (spannerType == "start")
            {
                spannerStarts[nr] = spanner;
            }
            else if (spannerType == "continue")
            {
                if (spannerStarts.TryGetValue(nr, out MusicXmlSpanner started))
                {
                    spanner.PairedWith = started;
                }
            }
            else if (spannerType == "stop")
            {
                if (spannerStarts.TryGetValue(nr, out MusicXmlSpanner started))
                {
                    started.PairedWith = spanner;
                    spanner.PairedWith = started;
                    spannerStarts.Remove(nr);
                }
                else
                {
                    //⚠ Upstream raises a python UserWarning here, whose printed form
                    //names the source file and line of its own tree. The port reports
                    //the message alone; PORT-COVERAGE records the shape.
                    State.Warning(type + " end seen without " + type + " start");
                }
            }
        }
    }

    private void HandleCrossStaffChords(
        List<MusicXmlNote> chordElements, int minStaffId, int maxStaffId, string stemDirection)
    {
        if (minStaffId == 1000 || maxStaffId == minStaffId)
        {
            return; //Not a cross-staff chord.
        }

        int mainStaffId;

        //We use stem information (if available) to split cross-staff chords into
        //different voices even if option `convert_stem_directions' is not set.
        if (stemDirection == null)
        {
            //Derive stem direction from main note.
            MusicXmlNote mainNoteForStem = chordElements[0];
            mainStaffId = int.Parse(
                mainNoteForStem.Get("staff", "1") as string, CultureInfo.InvariantCulture);
            if (mainStaffId == maxStaffId)
            {
                stemDirection = "down";
            }
            else if (mainStaffId == minStaffId)
            {
                stemDirection = "up";
            }
            else
            {
                //If a chord spans more than two staves, the main note could be in the
                //middle, which doesn't work with LilyPond.
                mainStaffId = maxStaffId;
                stemDirection = "down";
            }
        }
        else if (stemDirection == "up")
        {
            mainStaffId = minStaffId;
        }
        else
        {
            mainStaffId = maxStaffId;
        }

        string mainStaff = mainStaffId.ToString(CultureInfo.InvariantCulture);

        //We can now set the `cross_staff' field, also moving the <beam> children to a
        //note in the main staff if necessary. The chord itself gets split into
        //sub-chords.
        MusicXmlNote otherNoteWithBeam = null;
        MusicXmlNote mainNote = null;
        HashSet<string> subChordsMainNote = new HashSet<string>(StringComparer.Ordinal);

        foreach (MusicXmlNote chordElement in chordElements)
        {
            string staffId = chordElement.Get("staff", "1") as string;

            if (!subChordsMainNote.Contains(staffId))
            {
                //Convert first chord note of a sub-chord to a main note.
                if (chordElement.Has("chord"))
                {
                    chordElement.Content.Remove("chord");
                }

                subChordsMainNote.Add(staffId);
            }

            if (mainStaff == staffId)
            {
                if (mainNote == null)
                {
                    mainNote = chordElement;
                }
            }
            else
            {
                chordElement.CrossStaff = stemDirection == "up" ? "U" : "D";
                if (otherNoteWithBeam == null && chordElement.GetList("beam").Count > 0)
                {
                    otherNoteWithBeam = chordElement;
                }
            }
        }

        if (otherNoteWithBeam != null)
        {
            mainNote.Content["beam"] = otherNoteWithBeam.Item("beam");
            otherNoteWithBeam.Content["beam"] = new List<MusicXmlNode>();
        }

        //Synthesize a <stem> element for main note.
        if (!mainNote.Has("stem"))
        {
            MusicXmlHashText hashText = new MusicXmlHashText
            {
                State = State,
                ElementName = "#text",
                Data = stemDirection,
            };
            MusicXmlStem stem = new MusicXmlStem { State = State, ElementName = "stem" };
            stem.Children = new List<MusicXmlNode> { hashText };
            mainNote.Content["stem"] = stem;
        }
    }

    /// <summary>Splits this part into the voices LilyPond will engrave.</summary>
    internal void ExtractVoices()
    {
        //Fill `elements' array.
        List<MusicXmlMeasure> measures = GetTypedChildren<MusicXmlMeasure>();
        List<MusicXmlNode> elements = new List<MusicXmlNode>();
        foreach (MusicXmlMeasure measure in measures)
        {
            elements.Add(measure);
            if (measure.Partial > PythonFraction.Zero)
            {
                elements.Add(new MusicXmlPartial(measure.Partial) { State = State });
            }

            elements.AddRange(measure.GetAllChildren());
        }

        //LilyPond needs to know properties of the end of some spanners before the
        //spanner code gets actually emitted (and sometimes vice versa). To enable that
        //we set links between a spanner's start part and its end part.
        //
        //Similarly, middle parts of spanners sometimes need to know details of the
        //start spanner element (for example, accidental marks attached to a middle
        //part of a wavy line need to know the `placement' attribute of the start part).
        LinkSpanners(elements, new[] { typeof(MusicXmlDirection), typeof(MusicXmlDirType) },
                     "bracket");
        LinkSpanners(elements, new[] { typeof(MusicXmlDirection), typeof(MusicXmlDirType) },
                     "dashes");
        LinkSpanners(elements,
                     new[] { typeof(MusicXmlNote), typeof(MusicXmlNotations),
                             typeof(MusicXmlOrnaments) },
                     "wavy-line", oneChild: false);

        //The following pre-pass scans for cross-staff chords, setting the note's
        //cross-staff field to either 'U' or 'D' where necessary.
        //
        //We look at a chord's stem direction to decide which staff or staves are the
        //'other' one; if no stem direction is set, the chord's main note is assumed to
        //be not in the 'other' staff or staves.
        List<MusicXmlNote> chordElements = new List<MusicXmlNote>();
        int minStaffId = 1000;
        int maxStaffId = -1000;
        string stemDirection = null;

        foreach (MusicXmlNode node in elements)
        {
            if (!(node is MusicXmlNote note) || note.Has("rest"))
            {
                continue;
            }

            if (note.Has("chord"))
            {
                chordElements.Add(note);
            }
            else
            {
                HandleCrossStaffChords(chordElements, minStaffId, maxStaffId, stemDirection);

                chordElements = new List<MusicXmlNote> { note };
                minStaffId = 1000;
                maxStaffId = -1000;
                stemDirection = null;
            }

            string staffId = note.Get("staff") as string;
            if (staffId != null)
            {
                int value = int.Parse(staffId, CultureInfo.InvariantCulture);
                minStaffId = Math.Min(minStaffId, value);
                maxStaffId = Math.Max(maxStaffId, value);
            }

            //The first <stem> element wins.
            if (stemDirection == null)
            {
                MusicXmlNode stem = note.Get("stem") as MusicXmlNode;
                if (stem != null)
                {
                    string direction = stem.GetText().Trim();
                    if (direction == "down" || direction == "up")
                    {
                        stemDirection = direction;
                    }
                }
            }
        }

        if (chordElements.Count > 0)
        {
            HandleCrossStaffChords(chordElements, minStaffId, maxStaffId, stemDirection);
        }

        //Keys of `voicesDict' are (staff ID, voice ID, cross-staff chord direction,
        //grace). Values are constructed voice IDs reflecting LilyPond's needs.
        List<VoiceKey> voicesOrder = new List<VoiceKey>();
        Dictionary<VoiceKey, string> voicesDict = new Dictionary<VoiceKey, string>();

        //The next pre-pass collects all voice and staff ID information so that
        //dynamics, clefs, cross-staff chords, etc., can be assigned to the correct
        //voices.
        string lastVid = null;
        foreach (MusicXmlNode node in elements)
        {
            string vid;
            if (node.Has("voice"))
            {
                vid = (string)node.Item("voice");
            }
            else if (node is MusicXmlNote)
            {
                vid = node.Has("chord") ? lastVid : "1";
            }
            else
            {
                continue;
            }

            string cscVoiceDirection = null;
            if (node is MusicXmlNote noteElement && noteElement.CrossStaff != null)
            {
                cscVoiceDirection = noteElement.CrossStaff;
            }
            else if (vid != null)
            {
                lastVid = vid;
            }

            string staffId = node.Get("staff", "1") as string;
            bool grace = node.Has("grace");

            //We start with using this as an ordered set.
            VoiceKey key = new VoiceKey(staffId, vid, cscVoiceDirection, grace);
            if (!voicesDict.ContainsKey(key))
            {
                voicesOrder.Add(key);
            }

            voicesDict[key] = null;
        }

        //We now construct IDs for all voices. For cross-staff chords we need separate
        //voices that come either first or last for a given staff, and which are shared
        //with all voices that contain cross-staff chords (this means we need unique
        //IDs based on staff IDs, not on voice IDs).
        HashSet<string> normalVoices = new HashSet<string>(StringComparer.Ordinal);
        foreach (VoiceKey key in voicesOrder)
        {
            if (string.IsNullOrEmpty(key.CrossStaffDirection))
            {
                normalVoices.Add(key.VoiceId);
            }
        }

        foreach (VoiceKey key in voicesOrder)
        {
            if (string.IsNullOrEmpty(key.CrossStaffDirection))
            {
                voicesDict[key] = key.VoiceId;
                continue;
            }

            foreach (string suffix in new[] { "U", "D" })
            {
                if (key.CrossStaffDirection == suffix)
                {
                    string cscVid = key.StaffId;
                    //Find unique ID.
                    while (normalVoices.Contains(cscVid + suffix))
                    {
                        cscVid += "x";
                    }

                    voicesDict[key] = cscVid + suffix;
                }
            }
        }

        //The collected information in `voicesDict' is now used to construct two other
        //dictionaries.
        //
        //`staffToVoiceDict' is needed to assign staff-related objects like clefs,
        //times, etc., to the proper voices; this will never be entirely correct due to
        //staff switches, but it is the best we can do with the used algorithm. Voice
        //IDs must not occur more than once in the value arrays.
        //
        //`voices' defines the order of voices for a given part. We assume that voice
        //order in the MusicXML file is from top to bottom in a part.
        //
        //The used algorithm has two major flaws. Staff switches might mix up the
        //correct voice order in a staff, and attributes might be affected too; and
        //relying on a global top-to-bottom order can fail easily.
        Dictionary<string, (List<string> Up, List<string> Middle, List<string> Down)>
            staffToVoiceTriplets
                = new Dictionary<string, (List<string>, List<string>, List<string>)>(
                    StringComparer.Ordinal);
        List<string> staffOrder = new List<string>();
        HashSet<string> seenIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (VoiceKey key in voicesOrder)
        {
            string id = voicesDict[key];
            //Also ignore grace elements.
            if (seenIds.Contains(id) || key.Grace)
            {
                continue;
            }

            seenIds.Add(id);

            //We temporarily use a triplet of lists as the value to simplify ordering.
            if (!staffToVoiceTriplets.ContainsKey(key.StaffId))
            {
                staffToVoiceTriplets[key.StaffId]
                    = (new List<string>(), new List<string>(), new List<string>());
                staffOrder.Add(key.StaffId);
            }

            (List<string> up, List<string> middle, List<string> down)
                = staffToVoiceTriplets[key.StaffId];
            if (key.CrossStaffDirection == "U")
            {
                up.Add(id);
            }
            else if (key.CrossStaffDirection == "D")
            {
                down.Add(id);
            }
            else
            {
                middle.Add(id);
            }
        }

        _voiceOrder.Clear();
        _voices.Clear();
        List<string> sortedStaves = staffToVoiceTriplets.Keys.ToList();
        sortedStaves.Sort(StringComparer.Ordinal);
        foreach (string staffId in sortedStaves)
        {
            (List<string> up, List<string> middle, List<string> down)
                = staffToVoiceTriplets[staffId];
            foreach (string v in up)
            {
                _voiceOrder.Add(v);
                _voices[v] = new MusicXmlVoice("U");
            }

            foreach (string v in middle)
            {
                _voiceOrder.Add(v);
                _voices[v] = new MusicXmlVoice();
            }

            foreach (string v in down)
            {
                _voiceOrder.Add(v);
                _voices[v] = new MusicXmlVoice("D");
            }
        }

        //Now merge the triplets into simple lists.
        Dictionary<string, List<string>> staffToVoiceDict
            = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (string staffId in staffOrder)
        {
            (List<string> up, List<string> middle, List<string> down)
                = staffToVoiceTriplets[staffId];
            List<string> merged = new List<string>();
            merged.AddRange(up);
            merged.AddRange(middle);
            merged.AddRange(down);
            staffToVoiceDict[staffId] = merged;
        }

        MusicXmlAttributes startAttr = null;
        List<MusicXmlNode> assignToNextNote = new List<MusicXmlNode>();
        string id2 = null;

        //Now assign all elements to voices. Upstream's `prev_curr_iter' hands each
        //element the previous NON-TEXT element.
        MusicXmlNode previousNode = null;
        foreach (MusicXmlNode node in elements)
        {
            MusicXmlNode prevNode = previousNode;
            if (!(node is MusicXmlHashText))
            {
                previousNode = node;
            }

            if (node.Has("voice"))
            {
                id2 = (string)node.Item("voice");
            }
            else if (node is MusicXmlNote)
            {
                id2 = node.Has("chord") ? lastVid : "1";
            }

            if (id2 != null)
            {
                lastVid = id2;
            }

            //We don't need <backup> and <forward> any more since we have already
            //assigned the correct onset times.
            //
            //TODO: Pass <grouping>, <link>, <bookmark>, <sound>.
            if (!(node is MusicXmlNote || node is MusicXmlAttributes
                  || node is MusicXmlDirection || node is MusicXmlMeasure
                  || node is MusicXmlPartial || node is MusicXmlBarline
                  || node is MusicXmlHarmony || node is MusicXmlFiguredBass
                  || node is MusicXmlKeepAlive || node is MusicXmlPrint))
            {
                continue;
            }

            if (node is MusicXmlAttributes attributesNode && startAttr == null)
            {
                startAttr = attributesNode;
                continue;
            }

            if (node is MusicXmlAttributes laterAttributes)
            {
                //Assign attributes to corresponding voices.
                foreach (string staffId in staffOrder)
                {
                    MusicXmlAttributes staffAttributes
                        = ExtractAttributesForStaff(laterAttributes, staffId);
                    if (staffAttributes != null)
                    {
                        foreach (string v in staffToVoiceDict[staffId])
                        {
                            //`musicxml2ly' expects that after a <backup> element a new
                            //voice begins. However, the <clef>, <key>, and <time>
                            //children of <attributes> apply to all staves (and thus
                            //all voices) of a part in case the `number' attribute
                            //isn't set. For this reason we have to search backward in
                            //the voices to find the right musical moment for inserting
                            //<attributes>.
                            List<MusicXmlNode> voiceElements = _voices[v].Elements;
                            for (int i = voiceElements.Count - 1; i >= 0; i--)
                            {
                                MusicXmlNode el = voiceElements[i];
                                MusicXmlMusicNode elMusic = el as MusicXmlMusicNode;
                                PythonFraction elDuration =
                                    elMusic?.DurationValue ?? PythonFraction.Zero;
                                //<backup> is restricted to current measure.
                                if (el is MusicXmlMeasure
                                    || laterAttributes.When.Value
                                       >= elMusic.When.Value + elDuration)
                                {
                                    voiceElements.Insert(i + 1, staffAttributes);
                                    break;
                                }
                            }
                        }
                    }
                }

                continue;
            }

            if (node is MusicXmlMeasure || node is MusicXmlPartial || node is MusicXmlBarline)
            {
                if (id2 == null)
                {
                    id2 = "1";
                }

                foreach (MusicXmlNode pending in assignToNextNote)
                {
                    _voices[id2].AddElement(pending);
                }

                assignToNextNote = new List<MusicXmlNode>();

                foreach (string v in _voiceOrder)
                {
                    _voices[v].AddElement(node);
                }

                continue;
            }

            if (node is MusicXmlKeepAlive)
            {
                _voices[id2].AddElement(node, node.Get("staff") as string);
                continue;
            }

            if (node is MusicXmlPrint)
            {
                foreach (string v in _voiceOrder)
                {
                    _voices[v].AddElement(node);
                }

                continue;
            }

            if (node is MusicXmlDirection direction)
            {
                if (prevNode is MusicXmlDirection previousDirection)
                {
                    ChainDirections(previousDirection, direction);
                }

                if (!string.IsNullOrEmpty(direction.VoiceId))
                {
                    _voices[direction.VoiceId].AddElement(direction);
                }
                else
                {
                    assignToNextNote.Add(direction);
                }

                continue;
            }

            if (node is MusicXmlHarmony || node is MusicXmlFiguredBass)
            {
                //store the harmony or figured bass element until we encounter the next
                //note and assign it only to that one voice.
                assignToNextNote.Add(node);
                continue;
            }

            //At this point, `node' is a <note> element.
            foreach (MusicXmlNode pending in assignToNextNote)
            {
                _voices[id2].AddElement(pending);
            }

            assignToNextNote = new List<MusicXmlNode>();

            MusicXmlNote theNote = (MusicXmlNote)node;
            if (theNote.CrossStaff != null)
            {
                _voices[(theNote.Get("staff", "1") as string) + theNote.CrossStaff]
                    .AddElement(theNote);
            }
            else
            {
                _voices[id2].AddElement(theNote);
            }
        }

        //Assign all remaining elements from `assignToNextNote' to the voice of the
        //previous note (if any).
        if (id2 == null)
        {
            id2 = "1";
        }

        foreach (MusicXmlNode pending in assignToNextNote)
        {
            _voices[id2].AddElement(pending);
        }

        //Insert start attributes into all staves of the current part.
        if (startAttr != null)
        {
            foreach (string staffId in staffOrder)
            {
                MusicXmlAttributes staffAttributes
                    = ExtractAttributesForStaff(startAttr, staffId);
                staffAttributes.ReadSelf();
                _staffAttributesDict[staffId] = staffAttributes;
                foreach (string v in staffToVoiceDict[staffId])
                {
                    //Element 0 is a Measure object.
                    _voices[v].Insert(1, staffAttributes);
                    ((MusicXmlAttributes)_voices[v].Elements[1]).ReadSelf();
                }
            }
        }
    }

    /// <summary>
    /// Decides whether two successive direction elements belong together, and chains
    /// them when they do.
    /// </summary>
    /// <param name="previous">The earlier direction.</param>
    /// <param name="current">The later direction.</param>
    /// <remarks>
    /// The three heuristic checks are: the first direction-type child of each has
    /// approximately the same 'default-y' value; neither has a staff child, or both
    /// have one with the same value; and the offset children have different values (a
    /// missing offset child counts as zero). Example: 'Allegro' in the first and a
    /// metronome mark in the second.
    /// </remarks>
    private void ChainDirections(MusicXmlDirection previous, MusicXmlDirection current)
    {
        MusicXmlNode previousDirType = previous.GetTypedChildren<MusicXmlDirType>()
            .FirstOrDefault();
        if (previousDirType == null)
        {
            //Upstream's `next(...)' raises StopIteration here, which nothing catches.
            throw new ImportAbortedException("StopIteration");
        }

        MusicXmlNode previousFirstChild = previousDirType.GetAllChildren()
            .FirstOrDefault(c => !(c is MusicXmlHashText));
        if (previousFirstChild == null)
        {
            throw new ImportAbortedException("StopIteration");
        }

        string previousDefaultY = previousFirstChild.Attribute("default-y");

        MusicXmlNode dirType = current.GetTypedChildren<MusicXmlDirType>().FirstOrDefault();
        if (dirType == null)
        {
            throw new ImportAbortedException("StopIteration");
        }

        MusicXmlNode firstChild = dirType.GetAllChildren()
            .FirstOrDefault(c => !(c is MusicXmlHashText));
        if (firstChild == null)
        {
            throw new ImportAbortedException("StopIteration");
        }

        string defaultY = firstChild.Attribute("default-y");

        //Condition (1). We use half an interline staff space as a heuristic threshold.
        if (previousDefaultY == null || defaultY == null
            || !(Math.Abs(double.Parse(previousDefaultY, NumberStyles.Float,
                                       CultureInfo.InvariantCulture)
                          - double.Parse(defaultY, NumberStyles.Float,
                                         CultureInfo.InvariantCulture)) < 5))
        {
            return;
        }

        string previousStaff = previous.Get("staff", "0") as string;
        string staff = current.Get("staff", "0") as string;

        //Condition (2).
        if (previousStaff != staff)
        {
            return;
        }

        double previousOffset = previous.Has("offset")
            ? Convert.ToDouble(previous.Item("offset"), CultureInfo.InvariantCulture)
            : 0;
        double offset = current.Has("offset")
            ? Convert.ToDouble(current.Item("offset"), CultureInfo.InvariantCulture)
            : 0;

        //Condition (3).
        if (previousOffset == offset)
        {
            current.Message("Found overlapping <direction> elements");
            offset += 1; //Arbitrary choice.
        }

        //If the two elements aren't wedges, 'chain' them, and make the element's offset
        //on the right equal to the one on the left so that LilyPond uses the same
        //moment for both.
        if (previousFirstChild.GetName() == "wedge" && firstChild.GetName() == "wedge")
        {
            return;
        }

        if (previousOffset > offset)
        {
            current.Next = previous;
            previous.Previous = current;
            previous.Offset = current.Offset;
        }
        else
        {
            previous.Next = current;
            current.Previous = previous;
            current.Offset = previous.Offset;
        }
    }
}
