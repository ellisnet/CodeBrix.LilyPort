/*
   This file is part of LilyPond, the GNU music typesetter.

   Copyright (C) 1999--2026  Han-Wen Nienhuys <hanwen@xs4all.nl>
                             Jan Nieuwenhuizen <janneke@gnu.org>

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

namespace CodeBrix.LilyPort.Importers; //was previously: scripts/abc2ly.py;

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port.

/// <summary>
/// One run of <c>abc2ly</c>: reads ABC and writes LilyPond.
/// </summary>
/// <remarks>
/// ⚠ EVERY FIELD HERE IS ONE OF UPSTREAM'S MODULE GLOBALS. abc2ly is a script that
/// keeps its whole world at module scope and a run of it is a run of the process, so
/// the faithful translation of "the module" is "an instance" — that, and nothing else,
/// is what this class is. Two imports at once then cannot see each other's state, which
/// upstream could not have said.
/// <para>
/// The support this file leans on is deliberately the SAME support convert-ly's port
/// leans on: <see cref="PythonRegex"/> carries the patterns verbatim and answers
/// <c>re.match</c>/<c>re.search</c>/<c>re.sub</c> with python's semantics.
/// </para>
/// </remarks>
internal sealed class AbcConverter
{
    //abc2ly.py:74. 255 is not a number of anything; it is the script's "no value".
    private const int Undefined = 255;

    /// <summary>
    /// abc2ly.py:1682-1684. The release whose syntax this converter's OUTPUT was last
    /// verified against — upstream's own frozen number, and upstream's own reason:
    /// <para>
    /// "Don't substitute @VERSION@.  We want this to reflect the last version that was
    /// verified to work."
    /// </para>
    /// </summary>
    /// <remarks>
    /// ⚠ THIS IS NOT THE PORTED RELEASE, and rule 16 does not govern it. That rule
    /// makes <c>LilyVersion.CompatibleWithVersion</c> the one place the ported LilyPond
    /// release is named in C#; this is a different constant meaning a different thing,
    /// and conflating the two would make the emitted document claim to be current 2.27
    /// syntax when it is 2.24 idiom — <c>\times 2/3</c> rather than <c>\tuplet</c> —
    /// so convert-ly would decline to modernise a file that needs it. Ruled D63,
    /// 2026-08-26. The TAGLINE is the other case and DOES read the ported release,
    /// because there it is upstream's <c>@TOPLEVEL_VERSION@</c>.
    /// </remarks>
    private const string LastVerifiedOutputVersion = "2.24.0";

    private const string Digits = "0123456789";
    private const string HSpace = " \t";

    //abc2ly.py's repeat states. `None' is the fourth, and is spelled null here.
    private const int Repeat = 0;
    private const int Alternative1 = 1;
    private const int Alternative2 = 2;

    //abc2ly.py:625-627. slyrics_append's three-way state.
    private const int Text = 1;
    private const int Space = 2;
    private const int Spanner = 3;

    private readonly AbcImportOptions _globalOptions;
    private readonly ImportDiagnostics _stderr;

    //abc2ly.py:75-92, the module globals, in the order the script declares them.
    private AbcParserState _parserState;
    private readonly Dictionary<string, int> _voiceIdxDict = new Dictionary<string, int>();
    private readonly Dictionary<string, string> _header = new Dictionary<string, string>();
    private readonly List<string> _lyrics = new List<string>();
    private readonly List<List<string>> _slyrics = new List<List<string>>();
    private readonly List<string> _voices = new List<string>();
    private readonly List<AbcParserState> _stateList = new List<AbcParserState>();
    private readonly bool[] _implicitRepeat = new bool[8];
    private int _currentVoiceIdx = -1;
    private readonly int _currentLyricIdx = -1;
    private int _lyricIdx = -1;
    private int _defaultLen = 8;
    private bool _lengthSpecified;
    private bool _noBarLines;
    private int[] _globalKey = new int[7];
    private readonly string _midiSpecs = string.Empty;
    private bool _needUnmeteredBar;

    //abc2ly.py:1264. Eight, because that is how many the script allocates. A ninth
    //voice walks off the end of the list in python too.
    private readonly int?[] _repeatState = new int?[8];

    //abc2ly.py:1541.
    private int _lineNo;

    internal AbcConverter(AbcImportOptions options, ImportDiagnostics diagnostics)
    {
        _globalOptions = options;
        _stderr = diagnostics;
        _header["footnotes"] = string.Empty;
    }

    /// <summary>abc2ly.py:95-99.</summary>
    /// <param name="msg">The message.</param>
    private void Error(string msg)
    {
        _stderr.Write(msg);
        _stderr.CountError();
        if (_globalOptions.Strict)
        {
            //sys.exit(1): nothing is written, and there is no output file at all.
            throw ImportAbortedException.Reported();
        }
    }

    //abc2ly.py:102-103's `alphabet' is NOT ported. Its only caller was dump_voices'
    //numbered-voice name, and naming a voice differently from every reference to it is
    //the defect fixed above; with that gone the function has no reachable use.

    /// <summary>abc2ly.py:106-148. The number gives the base_octave.</summary>
    /// <param name="s">What is left of the line.</param>
    /// <param name="state">The voice's state.</param>
    /// <returns>What is left after the clef.</returns>
    private string CheckClef(string s, AbcParserState state)
    {
        (string Pattern, string Name, int Octave)[] clefs =
        {
            ("treble", "treble", 0),
            ("treble1", "french", 0),
            ("bass3", "varbaritone", 0),
            ("bass", "bass", 0),
            ("alto4", "tenor", 0),
            ("alto2", "mezzosoprano", 0),
            ("alto1", "soprano", 0),
            ("alto", "alto", 0),
            ("perc", "percussion", 0),
        };
        (string Pattern, string Suffix, int Octave)[] modifier =
        {
            ("-8va", "_8", -1),
            ("-8", "_8", -1),
            ("\\+8", "^8", +1),
            ("8", "_8", -1),
        };

        if (string.IsNullOrEmpty(s))
        {
            return string.Empty;
        }

        string clef = null;
        int octave = 0;
        foreach ((string Pattern, string Name, int Octave) c in clefs)
        {
            Match m = PythonRegex.MatchAt("^" + c.Pattern, s);
            if (m.Success)
            {
                (clef, octave) = (c.Name, c.Octave);
                s = Slice(s, m.Length);
                break;
            }
        }

        if (clef == null)
        {
            return s;
        }

        string mod = string.Empty;
        foreach ((string Pattern, string Suffix, int Octave) md in modifier)
        {
            Match m = PythonRegex.MatchAt("^" + md.Pattern, s);
            if (m.Success)
            {
                mod = md.Suffix;
                octave += md.Octave;
                s = Slice(s, m.Length);
                break;
            }
        }

        state.BaseOctave = octave;
        VoicesAppend("\\clef \"" + clef + mod + "\"\n");
        return s;
    }

    /// <summary>abc2ly.py:151-181.</summary>
    /// <param name="name">The voice's name.</param>
    /// <param name="rol">The rest of the line.</param>
    private void SelectVoice(string name, string rol)
    {
        if (!_voiceIdxDict.ContainsKey(name))
        {
            _stateList.Add(new AbcParserState());
            _voices.Add(string.Empty);
            _slyrics.Add(new List<string>());
            _voiceIdxDict[name] = _voices.Count - 1;
        }

        _currentVoiceIdx = _voiceIdxDict[name];
        _parserState = _stateList[_currentVoiceIdx];

        // TODO: Add more keywords.
        while (rol != string.Empty)
        {
            Match m = PythonRegex.MatchAt("^([^ \t=]*)=(.*)$", rol);  // find keyword
            if (m.Success)
            {
                string keyword = m.Groups[1].Value;
                rol = m.Groups[2].Value;
                Match a = PythonRegex.MatchAt("^(\"[^\"]*\"|[^ \t]*) *(.*)$", rol);
                if (a.Success)
                {
                    string value = a.Groups[1].Value;
                    rol = a.Groups[2].Value;
                    if (keyword == "clef")
                    {
                        CheckClef(value, _parserState);
                    }
                    else if (keyword == "name")
                    {
                        value = PythonRegex.Sub("\\\\", "\\\\\\\\", value);
                        // < 2.2
                        VoicesAppend("\\set Staff.instrumentName = " + value + "\n");
                    }
                    else if (keyword == "sname" || keyword == "snm")
                    {
                        VoicesAppend("\\set Staff.shortInstrumentName = " + value + "\n");
                    }
                }
            }
            else
            {
                break;
            }
        }
    }

    /// <summary>abc2ly.py:184-192.</summary>
    /// <param name="outf">Where the document is being written.</param>
    private void DumpGlobal(StringBuilder outf)
    {
        if (_needUnmeteredBar)
        {
            outf.Append(
                "\ncadenzaMeasure = {\n  \\cadenzaOff\n  \\partial 1024 s1024\n"
                + "  \\cadenzaOn\n}\n");
        }
    }

    /// <summary>abc2ly.py:195-202.</summary>
    /// <param name="outf">Where the document is being written.</param>
    /// <param name="hdr">The header fields.</param>
    private static void DumpHeader(StringBuilder outf, Dictionary<string, string> hdr)
    {
        outf.Append("\n\\header {\n");
        List<string> ks = new List<string>(hdr.Keys);
        ks.Sort(StringComparer.Ordinal);
        foreach (string k in ks)
        {
            hdr[k] = PythonRegex.Sub("\"", "\\\"", hdr[k]);
            outf.Append("  ").Append(k).Append(" = \"").Append(hdr[k]).Append("\"\n");
        }

        outf.Append("}\n");
    }

    /// <summary>abc2ly.py:205-211.</summary>
    /// <param name="outf">Where the document is being written.</param>
    private void DumpLyrics(StringBuilder outf)
    {
        if (_lyrics.Count > 0)
        {
            outf.Append("\n\\markup \\column {\n");
            for (int i = 0; i < _lyrics.Count; i++)
            {
                outf.Append(_lyrics[i]);
                outf.Append("\n");
            }

            outf.Append("}\n");
        }
    }

    /// <summary>abc2ly.py:214-220.</summary>
    /// <param name="outf">Where the document is being written.</param>
    private void DumpSlyrics(StringBuilder outf)
    {
        foreach (string k in SortedVoiceNames())
        {
            for (int i = 0; i < _slyrics[_voiceIdxDict[k]].Count; i++)
            {
                outf.Append("\n\"words").Append(k).Append("V")
                    .Append((i + 1).ToString(CultureInfo.InvariantCulture))
                    .Append("\" = \\lyricmode {");
                outf.Append("\n").Append(_slyrics[_voiceIdxDict[k]][i]);
                outf.Append("\n}\n");
            }
        }
    }

    /// <summary>abc2ly.py:223-241.</summary>
    /// <param name="outf">Where the document is being written.</param>
    private void DumpVoices(StringBuilder outf)
    {
        foreach (string k in SortedVoiceNames())
        {
            int idx = _voiceIdxDict[k];

            //⚠ DIVERGENCE FROM UPSTREAM — abc2ly.py:229-232 is broken here. Upstream
            //names a NUMBERED voice with `alphabet(int(k))', so `V: 1' is DEFINED as
            //"voiceB"; but dump_score and dump_slyrics both REFERENCE it as \"voice1",
            //and so does dump_slyrics' own "words%sV%s". The two halves never agree and
            //the document does not compile. MEASURED against the pinned 2.27.2: a
            //three-voice tune yields `error: unknown command: `\voice1'' three times
            //and a fatal failed-files. The number is what the user wrote and what every
            //reference already uses, so the definition is brought into line with the
            //references rather than the other way round.
            outf.Append("\n\"voice").Append(k).Append("\" = {");
            if (_implicitRepeat[idx])
            {
                outf.Append("\n\\repeat volta 2 {");
            }

            outf.Append("\n").Append(_voices[idx]);

            //⚠ DIVERGENCE FROM UPSTREAM — abc2ly.py:235-239 is broken here. Upstream
            //writes `if repeat_state[idx]:', and REPEAT is 0, which python reads as
            //FALSE — so a voice still inside an open `\repeat volta 2 {' never gets its
            //closing brace and the else arm upstream wrote for exactly that case is
            //unreachable. MEASURED against the pinned 2.27.2: a tune ending inside a
            //repeat yields `error: syntax error, unexpected \score'. Tested against
            //None instead, which makes upstream's own else arm do its job.
            if (_repeatState[idx] != null)
            {
                if (_repeatState[idx] == Alternative1 || _repeatState[idx] == Alternative2)
                {
                    outf.Append("} } }");
                }
                else
                {
                    outf.Append("}");
                }
            }

            outf.Append("\n}\n");
        }
    }

    /// <summary>
    /// abc2ly.py:244-268. Assume that Q takes the form "Q:'opt. description' 1/4=120".
    /// There are other possibilities, but they are deprecated.
    /// </summary>
    /// <param name="a">The field's text.</param>
    private void TryParseQ(string a)
    {
        Match m = PythonRegex.MatchAt(
            " *^(.*?) *([0-9]+) */ *([0-9]+) *=* *([0-9]+)\\s*", a);
        if (m.Success)
        {
            string descr = m.Groups[1].Value;  // possibly empty
            int numerator = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
            int denominator = int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
            string tempo = m.Groups[4].Value;
            string dur = DurationToLilyPondDuration(numerator, denominator, 0);
            if (descr != string.Empty)
            {
                descr += " ";
            }

            VoicesAppend("\\tempo " + descr + dur + " = " + tempo + "\n");
        }
        else
        {
            // Parsing of numeric tempi, as these are fairly common. The spec says the
            // number is a "beat" so using a quarter note as the standard time.
            Match numeric = PythonRegex.MatchAt("[0-9]+", a);
            if (numeric.Success)
            {
                VoicesAppend("\\tempo 4=" + numeric.Value);
            }
            else
            {
                _stderr.Write(
                    "abc2ly: Warning, unable to parse Q specification: " + a + "\n");
            }
        }
    }

    /// <summary>abc2ly.py:271-306.</summary>
    /// <param name="outf">Where the document is being written.</param>
    private void DumpScore(StringBuilder outf)
    {
        outf.Append("\n\n\\score {\n  <<\n");

        foreach (string k in SortedVoiceNames())
        {
            if (k == "default" && _voiceIdxDict.Count > 1)
            {
                break;
            }

            outf.Append("    \\context Staff = \"").Append(k).Append("\" {\n");
            if (k != "default")
            {
                outf.Append("      \\voicedefault\n");
            }

            //⚠ UPSTREAM WRITES A BACKSLASH BEFORE THE OPENING QUOTE, so the
            //reference reads \"voiceX" -- which is not a name LilyPond can read back.
            //DumpVoices also names a numeric voice with the ALPHABET letter while this
            //names it with the number, so the two halves do not even agree. Both are
            //upstream's, both are reproduced, neither is corrected here.
            outf.Append("      \\\"voice").Append(k).Append("\"");
            outf.Append("\n    }");

            for (int i = 0; i < _slyrics[_voiceIdxDict[k]].Count; i++)
            {
                outf.Append("\n    \\addlyrics {\n");
                outf.Append("      \\\"words").Append(k).Append("V")
                    .Append((i + 1).ToString(CultureInfo.InvariantCulture)).Append("\"");
                outf.Append("\n    }");
            }
        }

        outf.Append("\n  >>\n");
        if (_globalOptions.Beams)
        {
            outf.Append(
                "\n  \\layout {\n    \\context {\n      \\Voice\n      \\autoBeamOff\n"
                + "      melismaBusyProperties = #'()\n    }\n  }");
        }
        else
        {
            outf.Append("\n  \\layout {}");
        }

        outf.Append("\n  \\midi {").Append(_midiSpecs).Append("}\n}\n");
    }

    /// <summary>abc2ly.py:309-316.</summary>
    /// <param name="s">The field's text.</param>
    private void SetDefaultLength(string s)
    {
        Match m = PythonRegex.Search("1/([0-9]+)", s);
        if (m.Success)
        {
            _defaultLen = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            _lengthSpecified = true;
        }
    }

    /// <summary>abc2ly.py:319-328.</summary>
    /// <param name="s">The meter.</param>
    private void SetDefaultLenFromTimeSig(string s)
    {
        Match m = PythonRegex.Search("([0-9]+)/([0-9]+)", s);
        if (m.Success)
        {
            int n = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            int d = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
            _defaultLen = (double)n / d < 0.75 ? 16 : 8;
        }
    }

    // Pitch manipulation. Tuples are (name, alteration). 0 is (central) C. Alteration
    // -1 is a flat, alteration +1 is a sharp. Pitch in semitones.

    /// <summary>abc2ly.py:334-345.</summary>
    /// <param name="tup">The (name, alteration) pair.</param>
    /// <returns>The pitch in semitones.</returns>
    private static int SemitonePitch((int Name, int Alteration) tup)
    {
        int p = 0;

        int t = tup.Name;
        p += 12 * FloorDiv(t, 7);
        t = PyMod(t, 7);

        if (t > 2)
        {
            p -= 1;
        }

        p += (t * 2) + tup.Alteration;
        return p;
    }

    /// <summary>abc2ly.py:348-354.</summary>
    /// <param name="tup">The (name, alteration) pair.</param>
    /// <returns>The pair a fifth above.</returns>
    private static (int Name, int Alteration) FifthAbovePitch((int Name, int Alteration) tup)
    {
        (int n, int a) = (tup.Name + 4, tup.Alteration);
        int difference = 7 - (SemitonePitch((n, a)) - SemitonePitch(tup));
        a += difference;
        return (n, a);
    }

    /// <summary>abc2ly.py:357-367.</summary>
    /// <returns>The keys reached by stacking fifths.</returns>
    private static List<(int Name, int Alteration)> SharpKeys()
    {
        (int Name, int Alteration) p = (0, 0);
        List<(int Name, int Alteration)> l = new List<(int, int)>();
        while (true)
        {
            l.Add(p);
            (int t, int a) = FifthAbovePitch(p);
            if (PyMod(SemitonePitch((t, a)), 12) == 0)
            {
                break;
            }

            p = (PyMod(t, 7), a);
        }

        return l;
    }

    /// <summary>abc2ly.py:370-380.</summary>
    /// <returns>The keys reached by stacking fourths.</returns>
    private static List<(int Name, int Alteration)> FlatKeys()
    {
        (int Name, int Alteration) p = (0, 0);
        List<(int Name, int Alteration)> l = new List<(int, int)>();
        while (true)
        {
            l.Add(p);
            (int t, int a) = QuartAbovePitch(p);
            if (PyMod(SemitonePitch((t, a)), 12) == 0)
            {
                break;
            }

            p = (PyMod(t, 7), a);
        }

        return l;
    }

    /// <summary>abc2ly.py:383-389.</summary>
    /// <param name="tup">The (name, alteration) pair.</param>
    /// <returns>The pair a fourth above.</returns>
    private static (int Name, int Alteration) QuartAbovePitch((int Name, int Alteration) tup)
    {
        (int n, int a) = (tup.Name + 3, tup.Alteration);
        int difference = 5 - (SemitonePitch((n, a)) - SemitonePitch(tup));
        a += difference;
        return (n, a);
    }

    //abc2ly.py:392-406. abc to LilyPond key mode names.
    private static readonly Dictionary<string, string> KeyLookup
        = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "m", "minor" },
            { "min", "minor" },
            { "maj", "major" },
            { "major", "major" },
            { "phr", "phrygian" },
            { "ion", "ionian" },
            { "loc", "locrian" },
            { "aeo", "aeolian" },
            { "mix", "mixolydian" },
            { "mixolydian", "mixolydian" },
            { "lyd", "lydian" },
            { "dor", "dorian" },
            { "dorian", "dorian" },
        };

    /// <summary>abc2ly.py:409-432.</summary>
    /// <param name="k">The key as ABC spells it.</param>
    /// <returns>
    /// The key as LilyPond spells it; empty for <c>none</c>, and null when the mode was
    /// not recognised — both of which upstream's callers read as "write nothing".
    /// </returns>
    private string LilyKey(string k)
    {
        if (k == "none")
        {
            return string.Empty;
        }

        string orig = string.Empty + k;
        // UGR
        k = k.ToLowerInvariant();
        string key = CharAt(k, 0).ToString();
        // UGH
        k = Tail(k);
        if (k != string.Empty && k[0] == '#')
        {
            key += "is";
            k = Tail(k);
        }
        else if (k != string.Empty && k[0] == 'b')
        {
            key += "es";
            k = Tail(k);
        }

        if (k == string.Empty)
        {
            return key + " \\major";
        }

        string typ = k.Length <= 3 ? k : k.Substring(0, 3);
        if (!KeyLookup.ContainsKey(typ))
        {
            // ugh, use lilylib, say WARNING:FILE:LINE:
            _stderr.Write("abc2ly:warning:");
            _stderr.Write("ignoring unknown key: `" + orig + "'");
            _stderr.Write("\n");
            return null;
        }

        return key + " \\" + KeyLookup[typ];
    }

    /// <summary>abc2ly.py:435-446.</summary>
    /// <param name="note">The note name.</param>
    /// <param name="acc">The alteration.</param>
    /// <param name="shift">The shift in semitones.</param>
    /// <returns>The shifted (name, alteration) pair.</returns>
    private static (int Name, int Alteration) ShiftKey(int note, int acc, int shift)
    {
        int s = SemitonePitch((note, acc));
        s = PyMod(s + shift + 12, 12);
        int n;
        int a;
        if (s <= 4)
        {
            n = FloorDiv(s, 2);
            a = PyMod(s, 2);
        }
        else
        {
            n = FloorDiv(s + 1, 2);
            a = PyMod(s + 1, 2);
        }

        if (a != 0)
        {
            n += 1;
            a = -1;
        }

        return (n, a);
    }

    //abc2ly.py:449-468. Semitone shifts for key mode names.
    private static readonly Dictionary<string, int> KeyShift
        = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            { "m", 3 },
            { "min", 3 },
            { "minor", 3 },
            { "maj", 0 },
            { "major", 0 },
            { "phr", -4 },
            { "phrygian", -4 },
            { "ion", 0 },
            { "ionian", 0 },
            { "loc", 1 },
            { "locrian", 1 },
            { "aeo", 3 },
            { "aeolian", 3 },
            { "mix", 5 },
            { "mixolydian", 5 },
            { "lyd", -5 },
            { "lydian", -5 },
            { "dor", -2 },
            { "dorian", -2 },
        };

    /// <summary>abc2ly.py:471-516.</summary>
    /// <param name="k">The key as ABC spells it.</param>
    /// <returns>The alteration each of the seven note names carries.</returns>
    private int[] ComputeKey(string k)
    {
        k = k.ToLowerInvariant();
        int intkey = PyMod(CharAt(k, 0) - 'a' + 5, 7);
        int intkeyacc = 0;
        k = Tail(k);

        if (k != string.Empty && k[0] == 'b')
        {
            intkeyacc = -1;
            k = Tail(k);
        }
        else if (k != string.Empty && k[0] == '#')
        {
            intkeyacc = 1;
            k = Tail(k);
        }

        k = k.Length <= 3 ? k : k.Substring(0, 3);
        if (k != string.Empty && KeyShift.ContainsKey(k))
        {
            (intkey, intkeyacc) = ShiftKey(intkey, intkeyacc, KeyShift[k]);
        }

        (int Name, int Alteration) keytup = (intkey, intkeyacc);

        List<(int Name, int Alteration)> sharpKeySeq = SharpKeys();
        List<(int Name, int Alteration)> flatKeySeq = FlatKeys();

        List<int> accseq;
        int accsign;
        if (sharpKeySeq.Contains(keytup))
        {
            accsign = 1;
            int keyCount = sharpKeySeq.IndexOf(keytup);
            accseq = new List<int>();
            for (int x = 1; x <= keyCount; x++)
            {
                accseq.Add(PyMod((4 * x) - 1, 7));
            }
        }
        else if (flatKeySeq.Contains(keytup))
        {
            accsign = -1;
            int keyCount = flatKeySeq.IndexOf(keytup);
            accseq = new List<int>();
            for (int x = 1; x <= keyCount; x++)
            {
                accseq.Add(PyMod((3 * x) + 3, 7));
            }
        }
        else
        {
            Error("Huh?");
            throw new ImportAbortedException("Huh");
        }

        int[] keyTable = new int[7];
        foreach (int a in accseq)
        {
            keyTable[a] += accsign;
        }

        return keyTable;
    }

    //abc2ly.py:519-527.
    private static readonly Dictionary<string, string> TupLookup
        = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "2", "3/2" },
            { "3", "2/3" },
            { "4", "4/3" },
            { "5", "4/5" },
            { "6", "4/6" },
            { "7", "6/7" },
            { "9", "8/9" },
        };

    /// <summary>abc2ly.py:530-540.</summary>
    /// <param name="s">What is left of the line.</param>
    /// <param name="state">The voice's state.</param>
    /// <returns>What is left after the tuplet.</returns>
    private string TryParseTupletBegin(string s, AbcParserState state)
    {
        if (PythonRegex.MatchAt("\\([2-9]", s).Success)
        {
            string dig = s[1].ToString();
            s = Slice(s, 2);
            int prevTupletState = state.ParsingTuplet;
            state.ParsingTuplet = int.Parse(
                dig[0].ToString(), CultureInfo.InvariantCulture);
            if (prevTupletState != 0)
            {
                CloseBeamState(state);
                VoicesAppend("}");
            }

            VoicesAppend("\\times " + TupLookup[dig] + " {");
        }

        return s;
    }

    /// <summary>abc2ly.py:543-547.</summary>
    /// <param name="s">What is left of the line.</param>
    /// <param name="state">The voice's state.</param>
    /// <returns>What is left after the group end.</returns>
    private string TryParseGroupEnd(string s, AbcParserState state)
    {
        if (s != string.Empty && HSpace.IndexOf(s[0]) >= 0)
        {
            s = Tail(s);
            CloseBeamState(state);
        }

        return s;
    }

    /// <summary>abc2ly.py:550-554.</summary>
    /// <param name="key">The header field.</param>
    /// <param name="a">What to append.</param>
    /// <remarks>
    /// ⚠ DIVERGENCE FROM UPSTREAM — abc2ly.py:550-554 is broken here. Upstream's
    /// assignment sits INSIDE its own <c>if key in header</c> guard, so a field that is
    /// not already present is never created and the text is dropped on the floor
    /// (MEASURED against the pinned 2.27.2: two <c>H:</c> lines produce a header with
    /// no <c>history</c> field at all). The <c>s = ''</c> default upstream writes one
    /// line earlier only makes sense if it is USED, which is what the assignment does
    /// once it is outside the guard — so this is upstream's own intent, one indent out.
    /// </remarks>
    private void HeaderAppend(string key, string a)
    {
        string s = string.Empty;
        if (_header.ContainsKey(key))
        {
            s = _header[key] + "\n";
        }

        _header[key] = s + a;
    }

    /// <summary>abc2ly.py:557-561.</summary>
    /// <param name="a">What is being appended.</param>
    /// <param name="v">What it is being appended to.</param>
    /// <returns>The two, wrapped.</returns>
    private static string WordWrap(string a, string v)
    {
        int linelen = v.Length - v.LastIndexOf('\n');
        if (linelen + a.Length > 80)
        {
            v += "\n";
        }

        return v + a + " ";
    }

    /// <summary>abc2ly.py:564-568.</summary>
    /// <param name="stuff">The list.</param>
    /// <param name="idx">Which entry.</param>
    /// <param name="a">What to append.</param>
    private static void StuffAppend(List<string> stuff, int idx, string a)
    {
        if (stuff.Count == 0)
        {
            stuff.Add(a);
        }
        else
        {
            int at = PyIndex(stuff, idx);
            stuff[at] = WordWrap(a, stuff[at]);
        }
    }

    // Ignore wordwrap since we are adding to the previous word.

    /// <summary>abc2ly.py:573-580.</summary>
    /// <param name="stuff">The list.</param>
    /// <param name="idx">Which entry.</param>
    /// <param name="a">What to append.</param>
    private static void StuffAppendBack(List<string> stuff, int idx, string a)
    {
        if (stuff.Count == 0)
        {
            stuff.Add(a);
        }
        else
        {
            int at = PyIndex(stuff, idx);
            int point = stuff[at].Length - 1;
            while (CharAt(stuff[at], point) == ' ')
            {
                point -= 1;
            }

            point += 1;
            stuff[at] = stuff[at].Substring(0, point) + a + stuff[at].Substring(point);
        }
    }

    /// <summary>abc2ly.py:583-586.</summary>
    /// <param name="a">What to append.</param>
    private void VoicesAppend(string a)
    {
        if (_currentVoiceIdx < 0)
        {
            SelectVoice("default", string.Empty);
        }

        StuffAppend(_voices, _currentVoiceIdx, a);
    }

    // Wordwrap really makes it hard to bind beams to the end of notes since it pushes
    // out whitespace on every call. The _back functions do an append prior to the last
    // space, effectively tagging whatever they are given onto the last note.

    /// <summary>abc2ly.py:594-597.</summary>
    /// <param name="a">What to append.</param>
    private void VoicesAppendBack(string a)
    {
        if (_currentVoiceIdx < 0)
        {
            SelectVoice("default", string.Empty);
        }

        StuffAppendBack(_voices, _currentVoiceIdx, a);
    }

    /// <summary>abc2ly.py:600-604.</summary>
    /// <param name="a">The words.</param>
    private void LyricsAppend(string a)
    {
        a = PythonRegex.Sub("#", "\\#", a);        // latex does not like naked #'s
        a = PythonRegex.Sub("\"", "\\\"", a);      // latex does not like naked "'s
        a = "  \\line { \"" + a + "\" }\n";
        StuffAppend(_lyrics, _currentLyricIdx, a);
    }

    /// <summary>
    /// abc2ly.py:609-622. Break lyrics to words and put "'s around words containing
    /// numbers and '"'s.
    /// </summary>
    /// <param name="s">The lyric line.</param>
    /// <returns>The fixed line.</returns>
    private static string FixLyric(string s)
    {
        string ret = string.Empty;
        while (s != string.Empty)
        {
            Match m = PythonRegex.MatchAt("[ \t]*([^ \t]*)[ \t]*(.*$)", s);
            if (m.Success)
            {
                string word = m.Groups[1].Value;
                s = m.Groups[2].Value;
                word = PythonRegex.Sub("\"", "\\\"", word);    // escape "
                if (PythonRegex.MatchAt(".*[0-9\"\\(]", word).Success)
                {
                    word = PythonRegex.Sub("_", " ", word);  // _ causes probs inside ""
                    ret += "\"" + word + "\" ";
                }
                else
                {
                    ret += word + " ";
                }
            }
            else
            {
                return ret;
            }
        }

        return ret;
    }

    /// <summary>abc2ly.py:629-703.</summary>
    /// <param name="a">The vocals line.</param>
    /// <remarks>
    /// ⚠ THE <c>_</c> ARM'S SECOND TEST IS AN <c>if</c>, NOT AN <c>elif</c>, in
    /// upstream — so an underscore after a space appends its spanner twice. Ported as
    /// written.
    /// </remarks>
    private void SlyricsAppend(string a)
    {
        string s = string.Empty;
        int status = Text;
        int prevStatus = Text;
        bool escaped = false;

        foreach (char c in a)
        {
            // Escaped characters are inserted as-is.
            if (escaped)
            {
                if (status != Text)
                {
                    s += " ";
                }

                s += c;
                status = Text;
                escaped = false;
                continue;
            }

            if (c == '\\')
            {
                escaped = true;
            }
            else if (c == ' ')
            {
                s += " ";
                status = Space;
                continue;       // Don't update `prev_status'.
            }
            else if (c == '-')
            {
                // ' -' is the same as '-_'.
                if (status == Space)
                {
                    if (prevStatus == Text)
                    {
                        s += "-- ";
                    }

                    s += "_";
                    status = Spanner;
                }
                else if (status == Text)
                {
                    s += " --";
                    status = Spanner;
                }
                else if (status == Spanner)
                {
                    s += " _";
                }
            }
            else if (c == '_')
            {
                //⚠ DIVERGENCE FROM UPSTREAM — abc2ly.py:672-682 is broken here.
                //Upstream's second test is `if status == TEXT', where the `-' arm sixteen
                //lines above it correctly writes `elif'. After the SPACE arm has already
                //appended and set SPANNER, that stray `if' falls through to its own
                //`elif status == SPANNER' and appends a SECOND spanner. MEASURED against
                //the pinned 2.27.2: `w: one _ two' converts to `one __ _ _ two' where
                //`one __ _ two' is the extender that was meant. Written here as the
                //if/else-if chain the `-' arm already is.
                if (status == Space)
                {
                    if (prevStatus == Text)
                    {
                        s += "__ ";
                    }

                    s += "_";
                    status = Spanner;
                }
                else if (status == Text)
                {
                    s += " __ _";
                    status = Spanner;
                }
                else if (status == Spanner)
                {
                    s += " _";
                }
            }
            else if (c == '*')
            {
                s += status == Space ? "_" : " _";
            }
            else if (c == '~')
            {
                s += "_";
                status = Text;
            }
            else
            {
                if (status == Spanner)
                {
                    s += " ";
                }

                s += c;
                status = Text;
            }

            prevStatus = status;
        }

        // ensure that we have a space between this and a potential follow-up 'w:' line
        s += " ";

        s = PythonRegex.Sub("#", "\\#", s);         // latex does not like naked #'s
        // put numbers and " and ( into quoted string
        if (PythonRegex.MatchAt(".*[0-9\"\\(]", s).Success)
        {
            s = FixLyric(s);
        }

        _lyricIdx += 1;

        if (_slyrics[_currentVoiceIdx].Count <= _lyricIdx)
        {
            _slyrics[_currentVoiceIdx].Add(s);
        }
        else
        {
            _slyrics[_currentVoiceIdx][_lyricIdx]
                = WordWrap(s, _slyrics[_currentVoiceIdx][_lyricIdx]);
        }
    }

    /// <summary>abc2ly.py:706-826.</summary>
    /// <param name="ln">The line.</param>
    /// <param name="state">The voice's state.</param>
    /// <returns>Empty when the line was a header field, and the line otherwise.</returns>
    private string TryParseHeaderLine(string ln, AbcParserState state)
    {
        Match m = PythonRegex.MatchAt("^([A-Za-z]): *(.*)$", ln);

        if (!m.Success)
        {
            return ln;
        }

        string g = m.Groups[1].Value;
        string a = m.Groups[2].Value;
        switch (g)
        {
            case "T":  // title
                a = PythonRegex.Sub("[ \t]*$", string.Empty, a);  // strip trailing blanks
                if (_header.ContainsKey("title"))
                {
                    if (a != string.Empty)
                    {
                        if (_header["title"].Length != 0)
                        {
                            // the non-ascii character in the string below is a
                            // punctuation dash. (TeX ---)
                            _header["title"] += " \u2014 " + a;
                        }
                        else
                        {
                            _header["subtitle"] = a;
                        }
                    }
                }
                else
                {
                    _header["title"] = a;
                }

                break;

            case "M":  // Meter
                if (a == "C")
                {
                    if (!state.CommonTime)
                    {
                        state.CommonTime = true;
                        VoicesAppend("\\defaultTimeSignature");
                    }

                    a = "4/4";
                }
                else if (a == "C|")
                {
                    if (!state.CommonTime)
                    {
                        state.CommonTime = true;
                        VoicesAppend("\\defaultTimeSignature");
                    }

                    a = "2/2";
                }
                else if (a == "4/4" || a == "2/2")
                {
                    if (state.CommonTime)
                    {
                        state.CommonTime = false;
                        VoicesAppend("\\numericTimeSignature");
                    }
                }

                if (!_lengthSpecified)
                {
                    SetDefaultLenFromTimeSig(a);
                }
                else
                {
                    _lengthSpecified = false;
                }

                if (a == "none")
                {
                    state.HasMeter = false;
                }
                else
                {
                    if (state.InMusic && !state.HasMeter)
                    {
                        VoicesAppend("\\cadenzaOff\n");
                    }

                    VoicesAppend("\\time " + a);
                    state.HasMeter = true;
                }

                state.NextBar = string.Empty;
                break;

            case "K":  // KEY
                a = CheckClef(a, state);
                if (a != string.Empty && a != "none")
                {
                    // separate clef info
                    Match km = PythonRegex.MatchAt("^([^ \t]*) *([^ ]*)( *)(.*)$", a);
                    if (km.Success)
                    {
                        // There may or may not be a space between the key letter and
                        // the mode. Convert the mode to lower-case before comparing.
                        string group2 = km.Groups[2].Value;
                        string mode = (group2.Length <= 3 ? group2 : group2.Substring(0, 3))
                            .ToLowerInvariant();
                        string keyInfo;
                        string clefInfo;
                        if (mode != string.Empty && KeyLookup.ContainsKey(mode))
                        {
                            // use the full mode, not only the first three letters
                            keyInfo = km.Groups[1].Value + group2.ToLowerInvariant();
                            clefInfo = Slice(a, km.Groups[4].Index);
                        }
                        else
                        {
                            keyInfo = km.Groups[1].Value;
                            clefInfo = Slice(a, km.Groups[2].Index);
                        }

                        _globalKey = ComputeKey(keyInfo);
                        string k = LilyKey(keyInfo);
                        if (!string.IsNullOrEmpty(k))
                        {
                            VoicesAppend("\\key " + k);
                        }

                        CheckClef(clefInfo, state);
                    }
                    else
                    {
                        _globalKey = ComputeKey(a);
                        string k = LilyKey(a);
                        if (!string.IsNullOrEmpty(k))
                        {
                            VoicesAppend("\\key " + k + " \\major");
                        }
                    }
                }

                break;

            case "N":  // Notes
                _header["footnotes"] += "\\\\\\\\" + a;
                break;
            case "O":  // Origin
                _header["origin"] = a;
                break;
            case "X":  // Reference Number
                _header["crossRefNumber"] = a;
                break;
            case "A":  // Area
                _header["area"] = a;
                break;
            case "H":  // History
                HeaderAppend("history", a);
                break;
            case "B":  // Book
                _header["book"] = a;
                break;
            case "C":  // Composer
                if (_header.ContainsKey("composer"))
                {
                    if (a != string.Empty)
                    {
                        _header["composer"] += "\\\\\\\\" + a;
                    }
                }
                else
                {
                    _header["composer"] = a;
                }

                break;
            case "S":
                _header["subtitle"] = a;
                break;
            case "L":  // Default note length
                SetDefaultLength(ln);
                break;
            case "V":  // Voice
                string voice = PythonRegex.Sub(" .*$", string.Empty, a);
                string rest = PythonRegex.Sub("^[^ \t]*  *", string.Empty, a);
                if (state.NextBar != string.Empty)
                {
                    VoicesAppend(state.NextBar);
                    state.NextBar = string.Empty;
                }

                SelectVoice(voice, rest);
                break;
            case "W":  // Words
                LyricsAppend(a);
                break;
            case "w":  // vocals
                SlyricsAppend(a);
                break;
            case "Q":  // tempo
                TryParseQ(a);
                break;
            case "R":  // Rhythm (e.g. jig, reel, hornpipe)
                _header["meter"] = a;
                break;
            case "Z":  // Transcription (e.g. Steve Mansfield 1/2/2000)
                _header["transcription"] = a;
                break;
        }

        return string.Empty;
    }

    // We use in this order specified accidental, active accidental for bar, active
    // accidental for key.

    /// <summary>abc2ly.py:832-846.</summary>
    /// <param name="name">The note name.</param>
    /// <param name="acc">The specified alteration.</param>
    /// <param name="barAcc">The alteration active in this bar.</param>
    /// <param name="key">The alteration the key carries.</param>
    /// <returns>The note as LilyPond spells it.</returns>
    private string PitchToLilyPondName(int name, int acc, int barAcc, int key)
    {
        string s = string.Empty;
        if (acc == Undefined && !_noBarLines)
        {
            acc = barAcc;
        }

        if (acc == Undefined)
        {
            acc = key;
        }

        if (acc == -1)
        {
            s = "es";
        }
        else if (acc == 1)
        {
            s = "is";
        }

        if (name > 4)
        {
            name -= 7;
        }

        return ((char)(name + 'c')).ToString() + s;
    }

    /// <summary>abc2ly.py:849-858.</summary>
    /// <param name="o">The octave.</param>
    /// <returns>The quotes or commas.</returns>
    private static string OctaveToLilyPondQuotes(int o)
    {
        o += 2;
        string s;
        if (o < 0)
        {
            o = -o;
            s = ",";
        }
        else
        {
            s = "'";
        }

        return o > 0 ? string.Concat(System.Linq.Enumerable.Repeat(s, o)) : string.Empty;
    }

    /// <summary>abc2ly.py:861-870.</summary>
    /// <param name="s">What is left of the line.</param>
    /// <returns>What is left, and the number read.</returns>
    private static (string Remaining, int? Number) ParseNum(string s)
    {
        string durstr = string.Empty;
        while (s != string.Empty && Digits.IndexOf(s[0]) >= 0)
        {
            durstr += s[0];
            s = Tail(s);
        }

        int? n = null;
        if (durstr != string.Empty)
        {
            n = int.Parse(durstr, CultureInfo.InvariantCulture);
        }

        return (s, n);
    }

    /// <summary>abc2ly.py:873-884.</summary>
    /// <param name="num">The numerator.</param>
    /// <param name="den">The denominator.</param>
    /// <param name="dots">How many dots.</param>
    /// <returns>The duration as LilyPond spells it.</returns>
    private static string DurationToLilyPondDuration(double num, double den, int dots)
    {
        int baseValue = 1;
        while (baseValue * num < den)
        {
            baseValue *= 2;
        }

        string baseText = baseValue.ToString(CultureInfo.InvariantCulture);
        if (baseValue == 1)
        {
            double ratio = num / den;
            if (ratio == 2)
            {
                baseText = "\\breve";
            }
            else if (ratio == 3)
            {
                baseText = "\\breve";
                dots = 1;
            }
            else if (ratio == 4)
            {
                baseText = "\\longa";
            }
        }

        return baseText + (dots > 0 ? new string('.', dots) : string.Empty);
    }

    /// <summary>abc2ly.py:908-975.</summary>
    /// <param name="s">What is left of the line.</param>
    /// <param name="state">The voice's state.</param>
    /// <returns>What is left, the duration, the dots and the tie.</returns>
    private (string Remaining, double Num, double Den, int Dots, string Tie)
        ParseDurationAndTie(string s, AbcParserState state)
    {
        double den = state.NextDen;
        state.NextDen = 1;
        string tie = string.Empty;

        (string rest, int? parsed) = ParseNum(s);
        s = rest;
        double num = parsed == null || parsed.Value == 0 ? 1 : parsed.Value;

        if (s.Length != 0 && s[0] == '/')
        {
            while (Head(s) == "/")
            {
                s = Tail(s);
                double d = 2;
                if (Digits.IndexOf(CharAt(s, 0)) >= 0)
                {
                    (string r2, int? d2) = ParseNum(s);
                    s = r2;
                    d = d2.Value;
                }

                den *= d;
            }
        }

        den *= _defaultLen;

        if (CharAt(s, 0) == '-')
        {
            tie = "~";
            s = Tail(s);
        }

        int currentDots = state.NextDots;
        state.NextDots = 0;

        int currentDotsDelta = 0;
        int denFactor = 1;
        int nextDotsDelta = 0;
        int nextDenFactor = 1;

        bool haveLt = false;
        bool haveGt = false;

        if (PythonRegex.MatchAt("[ \t]*[<>]", s).Success)
        {
            while (HSpace.IndexOf(CharAt(s, 0)) >= 0)
            {
                s = Tail(s);
            }

            while (CharAt(s, 0) == '>')
            {
                haveGt = true;
                s = Tail(s);
                currentDotsDelta += 1;
                nextDenFactor *= 2;
            }

            while (CharAt(s, 0) == '<')
            {
                haveLt = true;
                s = Tail(s);
                denFactor *= 2;
                nextDotsDelta += 1;
            }
        }

        if (state.InChord)
        {
            if (haveGt)
            {
                _stderr.Write("Warning: ignoring '>' in chord\n");
            }

            if (haveLt)
            {
                _stderr.Write("Warning: ignoring '<' in chord\n");
            }
        }
        else
        {
            currentDots += currentDotsDelta;
            den *= denFactor;
            state.NextDots += nextDotsDelta;
            state.NextDen *= nextDenFactor;
        }

        int[] tryDots = { 3, 2, 1 };
        foreach (int d in tryDots)
        {
            int f = 1 << d;
            int multiplier = (2 * f) - 1;
            if (num % multiplier == 0 && den % f == 0)
            {
                num /= multiplier;
                den /= f;
                currentDots += d;
            }
        }

        return (s, num, den, currentDots, tie);
    }

    /// <summary>abc2ly.py:978-1000.</summary>
    /// <param name="s">What is left of the line.</param>
    /// <param name="state">The voice's state.</param>
    /// <returns>What is left after the rest.</returns>
    private string TryParseRest(string s, AbcParserState state)
    {
        if (s == string.Empty || (s[0] != 'z' && s[0] != 'x'))
        {
            return s;
        }

        _lyricIdx = -1;

        if (state.NextBar != string.Empty)
        {
            VoicesAppend(state.NextBar);
            state.NextBar = string.Empty;
        }

        string rest = s[0] == 'z' ? "r" : "s";
        s = Tail(s);

        (string r, double num, double den, int d, string tie) = ParseDurationAndTie(s, state);
        s = r;
        VoicesAppend(rest + DurationToLilyPondDuration(num, den, d));
        if (tie != string.Empty)
        {
            _stderr.Write("Warning: ignoring tie after rest\n");
        }

        if (state.NextArticulation != string.Empty)
        {
            VoicesAppend(state.NextArticulation);
            state.NextArticulation = string.Empty;
        }

        return s;
    }

    //abc2ly.py:1003-1016.
    private static readonly Dictionary<string, string> ArticTbl
        = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { ".", "-." },
            { "T", "^\\trill" },
            { "H", "^\\fermata" },
            { "u", "^\\upbow" },
            { "K", "^\\vartoe" },       // 'K' doesn't exist in v1.6
            { "k", "^\\accent" },
            { "M", "^\\tenuto" },
            { "~", "^\"~\" " },
            { "J", string.Empty },      // ignore slide
            { "R", string.Empty },      // ignore roll
            { "S", "^\\segno" },
            { "O", "^\\coda" },
            { "v", "^\\downbow" },
        };

    /// <summary>abc2ly.py:1019-1032.</summary>
    /// <param name="s">What is left of the line.</param>
    /// <param name="state">The voice's state.</param>
    /// <returns>What is left after the articulation.</returns>
    private string TryParseArticulation(string s, AbcParserState state)
    {
        while (s != string.Empty && ArticTbl.ContainsKey(Head(s)))
        {
            state.NextArticulation += ArticTbl[Head(s)];
            if (ArticTbl[Head(s)] == string.Empty)
            {
                _stderr.Write("Warning: ignoring `" + Head(s) + "'\n");
            }

            s = Tail(s);
        }

        // s7m2 input doesn't care about spaces
        if (PythonRegex.MatchAt("[ \t]*\\(", s).Success)
        {
            s = s.TrimStart();
        }

        while (Head(s) == "(" && Digits.IndexOf(CharAt(s, 1)) < 0)
        {
            state.NextArticulation += "(";
            s = Tail(s);
        }

        return s;
    }

    // Remember accidental for rest of bar.

    /// <summary>abc2ly.py:1037-1041.</summary>
    /// <param name="note">The note name.</param>
    /// <param name="octave">The octave.</param>
    /// <param name="acc">The alteration.</param>
    /// <param name="state">The voice's state.</param>
    private static void SetBarAcc(int note, int octave, int acc, AbcParserState state)
    {
        if (acc == Undefined)
        {
            return;
        }

        state.InAccidentals[note + (octave * 7)] = acc;
    }

    // Get accidental set in this bar or UNDEF if not set.

    /// <summary>abc2ly.py:1046-1050.</summary>
    /// <param name="note">The note name.</param>
    /// <param name="octave">The octave.</param>
    /// <param name="state">The voice's state.</param>
    /// <returns>The alteration, or <see cref="Undefined"/>.</returns>
    private static int GetBarAcc(int note, int octave, AbcParserState state)
    {
        int nOct = note + (octave * 7);
        return state.InAccidentals.TryGetValue(nOct, out int found) ? found : Undefined;
    }

    /// <summary>abc2ly.py:1058-1062. If we are parsing a beam, close it off.</summary>
    /// <param name="state">The voice's state.</param>
    private void CloseBeamState(AbcParserState state)
    {
        if (state.ParsingBeam && _globalOptions.Beams)
        {
            state.ParsingBeam = false;
            VoicesAppendBack("]");
        }
    }

    /// <summary>abc2ly.py:1066-1178. WAT IS ABC EEN ONTZETTENDE PROGRAMMEERPOEP !</summary>
    /// <param name="s">What is left of the line.</param>
    /// <param name="state">The voice's state.</param>
    /// <returns>What is left after the note.</returns>
    private string TryParseNote(string s, AbcParserState state)
    {
        if (s == string.Empty)
        {
            return s;
        }

        string articulation = string.Empty;
        int acc = Undefined;
        if ("^=_".IndexOf(s[0]) >= 0)
        {
            char c = s[0];
            s = Tail(s);
            if (c == '^')
            {
                acc = 1;
            }

            if (c == '=')
            {
                acc = 0;
            }

            if (c == '_')
            {
                acc = -1;
            }
        }

        int octave = state.BaseOctave;
        if ("ABCDEFG".IndexOf(CharAt(s, 0)) >= 0)
        {
            s = char.ToLowerInvariant(s[0]).ToString() + Tail(s);
            octave -= 1;
        }

        int notename;
        if ("abcdefg".IndexOf(CharAt(s, 0)) >= 0)
        {
            notename = PyMod(s[0] - 'a' + 5, 7);
            s = Tail(s);
        }
        else
        {
            return s;                // failed; not a note!
        }

        _lyricIdx = -1;

        if (state.NextBar != string.Empty)
        {
            VoicesAppend(state.NextBar);
            state.NextBar = string.Empty;
        }

        while (CharAt(s, 0) == ',')
        {
            octave -= 1;
            s = Tail(s);
        }

        while (CharAt(s, 0) == '\'')
        {
            octave += 1;
            s = Tail(s);
        }

        (string rest, double num, double den, int currentDots, string tie)
            = ParseDurationAndTie(s, state);
        s = rest;
        if (state.InChord && !state.IsFirstChordNote)
        {
            state.ChordNum = num;
            state.ChordDen = den;
            state.ChordCurrentDots = currentDots;
            state.IsFirstChordNote = true;
        }

        if (_globalOptions.Beams && state.ParsingBeam && num / den > 1.0 / 8.0)
        {
            CloseBeamState(state);
        }

        if (PythonRegex.MatchAt("[ \t]*\\)", s).Success)
        {
            s = s.TrimStart();
        }

        int slurEnd = 0;
        while (Head(s) == ")")
        {
            slurEnd += 1;
            s = Tail(s);
        }

        int barAcc = GetBarAcc(notename, octave, state);
        string pit = PitchToLilyPondName(notename, acc, barAcc, _globalKey[notename]);
        string octv = OctaveToLilyPondQuotes(octave);
        string mod = acc != Undefined && (acc == _globalKey[notename] || acc == barAcc)
            ? "!"
            : string.Empty;

        if (state.InChord)
        {
            VoicesAppend(pit + octv + mod + tie);
        }
        else
        {
            VoicesAppend(
                pit + octv + mod + DurationToLilyPondDuration(num, den, currentDots) + tie);
        }

        SetBarAcc(notename, octave, acc, state);
        if (!state.InChord)
        {
            if (state.NextArticulation != string.Empty)
            {
                articulation += state.NextArticulation;
                state.NextArticulation = string.Empty;
            }

            if (articulation != string.Empty)
            {
                VoicesAppend(articulation);
            }
        }

        if (slurEnd != 0)
        {
            VoicesAppend(string.Concat(System.Linq.Enumerable.Repeat(")", slurEnd)));
        }

        if (!state.InChord && state.ParsingTuplet != 0)
        {
            state.ParsingTuplet -= 1;
            if (state.ParsingTuplet == 0)
            {
                CloseBeamState(state);
                VoicesAppend("}");
            }
        }

        if (_globalOptions.Beams
            && !state.ParsingBeam
            && !state.InChord
            && ("^=_ABCDEFGabcdefg".IndexOf(CharAt(s, 0)) >= 0
                || (s[0] == '[' && CharAt(s, 2) != ':'))
            && num / den <= 1.0 / 8.0)
        {
            state.ParsingBeam = true;
            VoicesAppendBack("[");
        }

        return s;
    }

    /// <summary>abc2ly.py:1181-1186.</summary>
    /// <param name="s">What is left of the line.</param>
    /// <param name="state">The voice's state.</param>
    /// <returns>What is left after the space.</returns>
    private string JunkSpace(string s, AbcParserState state)
    {
        while (s != string.Empty && "\t\n\r ".IndexOf(s[0]) >= 0)
        {
            s = Tail(s);
            CloseBeamState(state);
        }

        return s;
    }

    /// <summary>abc2ly.py:1189-1206.</summary>
    /// <param name="s">What is left of the line.</param>
    /// <param name="state">The voice's state.</param>
    /// <returns>What is left after the guitar chord.</returns>
    private static string TryParseGuitarChord(string s, AbcParserState state)
    {
        if (Head(s) == "\"")
        {
            s = Tail(s);
            string gc = string.Empty;
            string position;
            if (CharAt(s, 0) == '_' || s[0] == '^')
            {
                position = s[0].ToString();
                s = Tail(s);
            }
            else
            {
                position = "^";
            }

            while (s != string.Empty && s[0] != '"')
            {
                gc += s[0];
                s = Tail(s);
            }

            if (s != string.Empty)
            {
                s = Tail(s);
            }

            gc = PythonRegex.Sub("#", "\\#", gc);        // escape '#'s
            state.NextArticulation = position + "\"" + gc + "\"" + state.NextArticulation;
        }

        return s;
    }

    /// <summary>abc2ly.py:1209-1219.</summary>
    /// <param name="s">What is left of the line.</param>
    /// <returns>What is left after the escape.</returns>
    private static string TryParseEscape(string s)
    {
        if (s == string.Empty || s[0] != '\\')
        {
            return s;
        }

        s = Tail(s);
        if (Head(s) == "K")
        {
            //⚠ DIVERGENCE FROM UPSTREAM — abc2ly.py:1215-1219 is broken here.
            //Upstream writes `key_table = compute_key()', calling a one-argument
            //function with NO argument. That is a TypeError in python, uncaught, so
            //abc2ly dies mid-file and writes NO OUTPUT AT ALL for any document
            //containing `\K' (MEASURED against the pinned 2.27.2: traceback, no .ly).
            //`key_table' is also a local nothing reads, so even had the call worked it
            //would have done nothing.
            //The whole escape is therefore consumed and ignored. ⚠ Upstream eats only
            //the BACKSLASH and leaves the `K' behind — which is harmless there only
            //because it never gets that far; left behind here it would fall through to
            //"Huh?  Don't understand" and put a stray K in the diagnostics. `\K' is one
            //token, so one token is what is consumed.
            s = Tail(s);
        }

        return s;
    }

    // |] thin-thick double bar line
    // || thin-thin double bar line
    // [| thick-thin double bar line
    // :| left repeat
    // |: right repeat
    // :: left-right repeat
    // |1 volta 1
    // |2 volta 2

    // TODO:
    //
    // * In '|[1' or ':|[2', allow space after '|'.
    // * Support '... :| ... :|'.

    //abc2ly.py:1233-1248.
    private static readonly Dictionary<string, string> BarDict
        = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "|]", "\\bar \"|.\"" },
            { "||", "\\bar \"||\"" },
            { "[|", "\\bar \".|\"" },
            { ":|", "}" },
            { "|:", "\n\\repeat volta 2 {" },
            { "::", "}\n\\repeat volta 2 {" },
            { "[1", "\n  \\alternative {\n    \\volta 1 {" },
            { "|1", "\n  \\alternative {\n    \\volta 1 {" },
            { "|[1", "\n  \\alternative {\n    \\volta 1 {" },
            { "[2", "}\n    \\volta 2 {" },
            { "|2", "}\n    \\volta 2 {" },
            { ":|2", "}\n    \\volta 2 {" },
            { ":|[2", "}\n    \\volta 2 {" },
            { "|", "\\bar \"|\"" },
        };

    private static readonly string[] Alternative1Opener = { "[1", "|1", "|[1" };
    private static readonly string[] Alternative2Opener = { "[2", "|2", ":|2", ":|[2" };
    private static readonly string[] RepeatEnder = { ":|" };
    private static readonly string[] RepeatOpener = { "|:" };       // implicitly closes alternatives
    private static readonly string[] RepeatEnderOpener = { "::" };  // implicitly closes alternatives

    /// <summary>abc2ly.py:1267-1379.</summary>
    /// <param name="stringValue">What is left of the line.</param>
    /// <param name="state">The voice's state.</param>
    /// <returns>What is left after the bar line.</returns>
    private string TryParseBar(string stringValue, AbcParserState state)
    {
        string bs = string.Empty;
        string braces = string.Empty;
        if (_currentVoiceIdx < 0)
        {
            SelectVoice("default", string.Empty);
        }

        // Try the longer one first.
        foreach (int trylen in new[] { 4, 3, 2, 1 })
        {
            string prefix = stringValue.Length <= trylen
                ? stringValue
                : stringValue.Substring(0, trylen);
            if (prefix == string.Empty || !BarDict.ContainsKey(prefix))
            {
                continue;
            }

            string s = prefix;
            stringValue = Slice(stringValue, trylen);
            int? rep = _repeatState[_currentVoiceIdx];

            if (Array.IndexOf(Alternative1Opener, s) >= 0)
            {
                if (rep == Alternative1)
                {
                    _stderr.Write(
                        "Warning: already in first alternative, ignoring `" + s + "'\n");
                    break;
                }

                if (rep == Alternative2)
                {
                    _stderr.Write(
                        "Warning: already in second alternative, ignoring `" + s + "'\n");
                    break;
                }

                if (rep == Repeat)
                {
                    rep = Alternative1;
                }
                else
                {
                    if (_implicitRepeat[_currentVoiceIdx])
                    {
                        _stderr.Write("Warning: not in a repeat, ignoring `" + s + "'\n");
                        break;
                    }

                    // Assume an implicit repeat sign at the beginning of the piece.
                    _implicitRepeat[_currentVoiceIdx] = true;
                    rep = Alternative1;
                }
            }
            else if (Array.IndexOf(Alternative2Opener, s) >= 0)
            {
                if (rep == Alternative2)
                {
                    _stderr.Write(
                        "Warning: already in second alternative, ignoring `" + s + "'\n");
                    break;
                }

                if (rep == Repeat)
                {
                    _stderr.Write(
                        "Warning: no first alternative, ignoring `" + s + "'\n");
                    break;
                }

                if (rep == Alternative1)
                {
                    rep = Alternative2;
                }
                else
                {
                    _stderr.Write("Warning: not in a repeat, ignoring `" + s + "'\n");
                    break;
                }
            }
            else
            {
                if (Array.IndexOf(RepeatEnder, s) >= 0)
                {
                    if (rep == null)
                    {
                        if (_implicitRepeat[_currentVoiceIdx])
                        {
                            _stderr.Write(
                                "Warning: not in a repeat, ignoring `" + s + "'\n");
                            break;
                        }

                        // Assume an implicit repeat sign at the beginning of the piece.
                        _implicitRepeat[_currentVoiceIdx] = true;
                    }

                    rep = null;
                }
                else if (Array.IndexOf(RepeatOpener, s) >= 0)
                {
                    if (rep == Alternative1 || rep == Alternative2)
                    {
                        braces = "} } }";
                    }
                    else if (rep == Repeat)
                    {
                        braces = "}";
                    }

                    rep = Repeat;
                }
                else if (Array.IndexOf(RepeatEnderOpener, s) >= 0)
                {
                    if (rep == null)
                    {
                        if (_implicitRepeat[_currentVoiceIdx])
                        {
                            _stderr.Write(
                                "Warning: not in a repeat, ignoring `" + s + "'\n");
                            break;
                        }

                        // Assume an implicit repeat sign at the beginning of the piece.
                        _implicitRepeat[_currentVoiceIdx] = true;
                    }
                    else if (rep == Alternative1 || rep == Alternative2)
                    {
                        braces = "} }";
                    }

                    rep = Repeat;
                }
            }

            _repeatState[_currentVoiceIdx] = rep;
            bs = braces + BarDict[s];
            break;
        }

        if (Head(stringValue) == "|")
        {
            state.NextBar = "|\n";
            stringValue = Tail(stringValue);
            state.ClearBarAccidentals();
            CloseBeamState(state);
        }

        if (Head(stringValue) == "}")
        {
            CloseBeamState(state);
        }

        if ((bs != string.Empty || state.NextBar != string.Empty)
            && state.ParsingTuplet != 0)
        {
            state.ParsingTuplet = 0;
            VoicesAppend("}");
        }

        if (bs != string.Empty)
        {
            state.ClearBarAccidentals();
            CloseBeamState(state);
            if (!state.HasMeter)
            {
                _needUnmeteredBar = true;
                VoicesAppend("\\cadenzaMeasure");
            }

            VoicesAppend(bs);
        }

        return stringValue;
    }

    /// <summary>abc2ly.py:1382-1388.</summary>
    /// <param name="s">What is left of the line.</param>
    /// <param name="state">The voice's state.</param>
    /// <returns>What is left after the bracket escape.</returns>
    private string BracketEscape(string s, AbcParserState state)
    {
        Match m = PythonRegex.MatchAt("^([^\\]]*)] *(.*)$", s);
        if (m.Success)
        {
            string cmd = m.Groups[1].Value;
            s = m.Groups[2].Value;
            TryParseHeaderLine(cmd, state);
        }

        return s;
    }

    /// <summary>abc2ly.py:1391-1481.</summary>
    /// <param name="s">What is left of the line.</param>
    /// <param name="state">The voice's state.</param>
    /// <returns>What is left after the chord delimiter.</returns>
    private string TryParseChordDelims(string s, AbcParserState state)
    {
        string @out = string.Empty;
        string tie = string.Empty;
        if (Head(s) == "[")
        {
            if (PythonRegex.MatchAt("\\[[0-9]", s).Success)      // repeat, not chord
            {
                return s;
            }

            s = Tail(s);
            if (PythonRegex.MatchAt("[A-Z]:", s).Success)        // bracket escape, not chord
            {
                return BracketEscape(s, state);
            }

            if (state.NextBar != string.Empty)
            {
                VoicesAppend(state.NextBar);
                state.NextBar = string.Empty;
            }

            @out = "<";
        }
        else if (Head(s) == "+")                                 // deprecated since ABC 1.6
        {
            s = Tail(s);
            if (state.PlusChord)
            {
                @out = ">";
                state.PlusChord = false;
            }
            else
            {
                if (state.NextBar != string.Empty)
                {
                    VoicesAppend(state.NextBar);
                    state.NextBar = string.Empty;
                }

                @out = "<";
                state.PlusChord = true;
            }
        }
        else if (Head(s) == "]")
        {
            state.InChord = false;

            s = Tail(s);
            @out = ">";

            int defLen = _defaultLen;
            int nextDen = state.NextDen;
            int nextDots = state.NextDots;

            _defaultLen = 1;
            state.NextDen = 1;
            state.NextDots = 0;

            (string rest, double num, double den, int currentDots, string parsedTie)
                = ParseDurationAndTie(s, state);
            s = rest;
            tie = parsedTie;
            state.ChordNum *= num;
            state.ChordDen *= den;
            state.ChordCurrentDots += currentDots;

            _defaultLen = defLen;
            state.NextDen *= nextDen;
            state.NextDots += nextDots;
        }

        if (@out == string.Empty)
        {
            return s;
        }

        if (PythonRegex.MatchAt("[ \t]*\\)", s).Success)
        {
            s = s.TrimStart();
        }

        int slurEnd = 0;
        while (Head(s) == ")")
        {
            slurEnd += 1;
            s = Tail(s);
        }

        if (@out == ">")
        {
            @out += DurationToLilyPondDuration(
                state.ChordNum, state.ChordDen, state.ChordCurrentDots);
            @out += tie;

            if (state.NextArticulation != string.Empty)
            {
                @out += state.NextArticulation;
                state.NextArticulation = string.Empty;
            }

            VoicesAppend(
                @out + (slurEnd > 0
                    ? string.Concat(System.Linq.Enumerable.Repeat(")", slurEnd))
                    : string.Empty));

            if (state.ParsingTuplet != 0)
            {
                state.ParsingTuplet -= 1;
                if (state.ParsingTuplet == 0)
                {
                    CloseBeamState(state);
                    VoicesAppend("}");
                }
            }

            if (_globalOptions.Beams
                && !state.ParsingBeam
                && ("^=_ABCDEFGabcdefg".IndexOf(CharAt(s, 0)) >= 0
                    || (s[0] == '[' && CharAt(s, 2) != ':'))
                && state.ChordNum / state.ChordDen <= 1.0 / 8.0)
            {
                state.ParsingBeam = true;
                VoicesAppend("[");
            }
        }
        else
        {
            if (slurEnd != 0)
            {
                _stderr.Write("Warning: ignoring `)' in chord\n");
            }

            state.InChord = true;
            state.IsFirstChordNote = false;
            VoicesAppend(@out);
        }

        return s;
    }

    /// <summary>abc2ly.py:1484-1496.</summary>
    /// <param name="s">What is left of the line.</param>
    /// <param name="state">The voice's state.</param>
    /// <returns>What is left after the grace delimiter.</returns>
    private string TryParseGraceDelims(string s, AbcParserState state)
    {
        if (Head(s) == "{")
        {
            if (state.NextBar != string.Empty)
            {
                VoicesAppend(state.NextBar);
                state.NextBar = string.Empty;
            }

            s = Tail(s);
            VoicesAppend("\\grace {");
        }
        else if (Head(s) == "}")
        {
            s = Tail(s);
            CloseBeamState(state);
            VoicesAppend("}");
        }

        return s;
    }

    /// <summary>abc2ly.py:1499-1538.</summary>
    /// <param name="s">The comment's text, after the leading percent.</param>
    /// <returns>The text, unchanged.</returns>
    private string TryParseComment(string s)
    {
        if (CharAt(s, 0) == '%')
        {
            if (s.StartsWith("%MIDI", StringComparison.Ordinal))
            {
                // The nobarlines option is necessary for an abc to LilyPond translator
                // for exactly the same reason abc2midi needs it: abc requires the user
                // to enter the note that will be printed, and MIDI and LilyPond expect
                // entry of the pitch that will be played.
                //
                // In standard 19th century musical notation, the algorithm for
                // translating between printed note and pitch involves using the
                // barlines to determine the scope of the accidentals.
                //
                // Since ABC is frequently used for music in styles that do not use this
                // convention, such as most music written before 1700, or ethnic music
                // in non-western scales, it is necessary to be able to tell a
                // translator that the barlines should not affect its interpretation of
                // the pitch.
                if (s.Contains("nobarlines"))
                {
                    _noBarLines = true;
                }
            }
            else if (s.StartsWith("%LY", StringComparison.Ordinal))
            {
                int p = s.IndexOf("voices", StringComparison.Ordinal);
                if (p > -1)
                {
                    VoicesAppend(Slice(s, p + 7));
                    VoicesAppend("\n");
                }

                p = s.IndexOf("slyrics", StringComparison.Ordinal);
                if (p > -1)
                {
                    SlyricsAppend(Slice(s, p + 8));
                }
            }
        }

        // Write other kinds of appending if we ever need them.
        return s;
    }

    /// <summary>abc2ly.py:1545-1602.</summary>
    /// <param name="text">The ABC document.</param>
    /// <param name="fn">What to call it in diagnostics.</param>
    private void ParseFile(string text, string fn)
    {
        List<string> ls = ReadLines(text);

        SelectVoice("default", string.Empty);
        _lineNo = 0;
        _parserState = _stateList[_currentVoiceIdx];

        foreach (string line in ls)
        {
            string ln = line;
            _lineNo += 1;

            Match m = PythonRegex.MatchAt("^([^%]*)%(.*)$", ln);  // add comments to current voice
            if (m.Success)
            {
                if (m.Groups[2].Value != string.Empty)
                {
                    TryParseComment(m.Groups[2].Value);
                    VoicesAppend("% " + m.Groups[2].Value + "\n");
                }

                ln = m.Groups[1].Value;
            }

            string origLn = ln;

            ln = JunkSpace(ln, _parserState);
            ln = TryParseHeaderLine(ln, _parserState);

            // If `ln' is not empty at this point, the parsing of header lines is
            // finished, and the music block starts.
            if (ln != string.Empty)
            {
                _parserState.InMusic = true;
                if (!_parserState.HasMeter)
                {
                    VoicesAppend("\\once \\omit Staff.TimeSignature\n");
                    VoicesAppend("\\cadenzaOn\n");
                }
            }

            // Try nibbling characters off until the line doesn't change.
            string prevLn = string.Empty;
            while (ln != prevLn)
            {
                prevLn = ln;
                ln = TryParseChordDelims(ln, _parserState);
                ln = TryParseRest(ln, _parserState);
                ln = TryParseArticulation(ln, _parserState);
                ln = TryParseNote(ln, _parserState);
                ln = TryParseBar(ln, _parserState);
                ln = TryParseEscape(ln);
                ln = TryParseGuitarChord(ln, _parserState);
                ln = TryParseTupletBegin(ln, _parserState);
                ln = TryParseGroupEnd(ln, _parserState);
                ln = TryParseGraceDelims(ln, _parserState);
                ln = JunkSpace(ln, _parserState);
            }

            if (ln != string.Empty)
            {
                Error(
                    fn + ": " + _lineNo.ToString(CultureInfo.InvariantCulture)
                    + ": Huh?  Don't understand\n");
                string left = origLn.Substring(0, origLn.Length - ln.Length);
                _stderr.Write(left + "\n");
                _stderr.Write(new string(' ', left.Length) + ln + "\n");
            }
        }
    }

    /// <summary>abc2ly.py:1673-1701, the driver's conversion half.</summary>
    /// <param name="abcText">The ABC document.</param>
    /// <returns>The LilyPond source.</returns>
    /// <remarks>
    /// The <c>\version</c> line is upstream's own frozen
    /// <see cref="LastVerifiedOutputVersion"/>; the TAGLINE reads the ported release,
    /// because there upstream substitutes <c>@TOPLEVEL_VERSION@</c>. Two
    /// version-shaped strings, two different meanings — see D63 and the Importers
    /// PORT-COVERAGE.
    /// </remarks>
    internal string Convert(string abcText)
    {
        StringBuilder outFile = new StringBuilder();

        _header["tagline"] =
            "LilyPond " + LilyPortInfo.CompatibleWithVersion
            + " was here -- automatically converted from ABC";

        ParseFile(abcText, _globalOptions.SourceName ?? string.Empty);

        // Don't substitute @VERSION@. We want this to reflect the last version that was
        // verified to work.
        outFile.Append("\\version \"").Append(LastVerifiedOutputVersion).Append("\"\n");

        DumpHeader(outFile, _header);
        DumpGlobal(outFile);
        DumpSlyrics(outFile);
        DumpVoices(outFile);
        DumpScore(outFile);
        DumpLyrics(outFile);
        return outFile.ToString();
    }

    /// <summary>The voice names, in the order every dump walks them.</summary>
    /// <returns>The names, sorted.</returns>
    private List<string> SortedVoiceNames()
    {
        List<string> ks = new List<string>(_voiceIdxDict.Keys);
        ks.Sort(StringComparer.Ordinal);
        return ks;
    }

    /// <summary>
    /// python's <c>file.readlines()</c> over text opened with universal newlines.
    /// </summary>
    /// <param name="text">The document.</param>
    /// <returns>The lines, each still carrying the newline that ended it.</returns>
    /// <remarks>
    /// The newline MATTERS: every nibbling function reads <c>s[0]</c> without checking
    /// the length, and it is the line's own trailing newline that keeps them from
    /// walking off the end. A document whose last line has none makes abc2ly raise, and
    /// this port raises with it.
    /// </remarks>
    private static List<string> ReadLines(string text)
    {
        string normalized = (text ?? string.Empty).Replace("\r\n", "\n").Replace("\r", "\n");
        List<string> lines = new List<string>();
        int start = 0;
        for (int i = 0; i < normalized.Length; i++)
        {
            if (normalized[i] == '\n')
            {
                lines.Add(normalized.Substring(start, i - start + 1));
                start = i + 1;
            }
        }

        if (start < normalized.Length)
        {
            lines.Add(normalized.Substring(start));
        }

        return lines;
    }

    /// <summary>python's <c>s[1:]</c>.</summary>
    /// <param name="s">The text.</param>
    /// <returns>Everything after the first character.</returns>
    private static string Tail(string s) => s.Length > 1 ? s.Substring(1) : string.Empty;

    /// <summary>python's <c>s[:1]</c>.</summary>
    /// <param name="s">The text.</param>
    /// <returns>The first character, or empty.</returns>
    private static string Head(string s)
        => s.Length > 0 ? s.Substring(0, 1) : string.Empty;

    /// <summary>python's <c>s[n:]</c>.</summary>
    /// <param name="s">The text.</param>
    /// <param name="n">Where to start.</param>
    /// <returns>The rest.</returns>
    private static string Slice(string s, int n)
        => n >= s.Length ? string.Empty : s.Substring(n);

    /// <summary>
    /// python's <c>s[i]</c> — INCLUDING the <c>IndexError</c> when there is no such
    /// character.
    /// </summary>
    /// <param name="s">The text.</param>
    /// <param name="i">Which character.</param>
    /// <returns>The character.</returns>
    private static char CharAt(string s, int i)
        => i >= 0 && i < s.Length
            ? s[i]
            : throw new ImportAbortedException("string index out of range");

    /// <summary>
    /// python's list indexing, where a negative index counts back from the end.
    /// </summary>
    /// <param name="list">The list.</param>
    /// <param name="idx">The index, possibly negative.</param>
    /// <returns>The index a C# list wants.</returns>
    /// <remarks>
    /// ⚠ LOAD-BEARING. <c>current_lyric_idx</c> is -1 and is never assigned again, so
    /// every <c>W:</c> line word-wraps onto the LAST entry of the lyrics list. Read as
    /// a C# index it would be an error; read as python's, it is the whole mechanism.
    /// </remarks>
    private static int PyIndex(List<string> list, int idx)
        => idx < 0 ? list.Count + idx : idx;

    /// <summary>python's <c>//</c>.</summary>
    /// <param name="a">The dividend.</param>
    /// <param name="b">The divisor.</param>
    /// <returns>The quotient, rounded towards negative infinity.</returns>
    private static int FloorDiv(int a, int b)
    {
        int q = a / b;
        return (a % b != 0) && ((a < 0) != (b < 0)) ? q - 1 : q;
    }

    /// <summary>python's <c>%</c>, whose result takes the divisor's sign.</summary>
    /// <param name="a">The dividend.</param>
    /// <param name="b">The divisor.</param>
    /// <returns>The remainder.</returns>
    private static int PyMod(int a, int b)
    {
        int r = a % b;
        return r != 0 && ((r < 0) != (b < 0)) ? r + b : r;
    }
}
