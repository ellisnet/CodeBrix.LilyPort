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

using System.Collections.Generic;

namespace CodeBrix.LilyPort.Importers; //was previously: scripts/abc2ly.py (class Parser_state);

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port.

/// <summary>
/// Where one ABC voice has got to: what is open, what the next note inherits, and which
/// accidentals this bar has already seen.
/// </summary>
/// <remarks>
/// One of these per voice, held in the converter's state list — a voice picked up again
/// after another has been written resumes exactly where it left off.
/// </remarks>
internal sealed class AbcParserState
{
    /// <summary>Gets or sets whether the music block has started.</summary>
    internal bool InMusic { get; set; }

    /// <summary>Gets or sets whether a meter has been declared.</summary>
    internal bool HasMeter { get; set; }

    /// <summary>
    /// Gets the accidentals set so far in this bar, keyed by note plus octave times
    /// seven.
    /// </summary>
    internal Dictionary<int, int> InAccidentals { get; private set; }
        = new Dictionary<int, int>();

    /// <summary>Gets or sets what to hang on the next note.</summary>
    internal string NextArticulation { get; set; } = string.Empty;

    /// <summary>Gets or sets the bar line owed before the next note.</summary>
    internal string NextBar { get; set; } = string.Empty;

    /// <summary>Gets or sets the dots the next note inherits from a broken rhythm.</summary>
    internal int NextDots { get; set; }

    /// <summary>Gets or sets the denominator the next note inherits.</summary>
    internal int NextDen { get; set; } = 1;

    /// <summary>Gets or sets how many notes of the open tuplet are still to come.</summary>
    internal int ParsingTuplet { get; set; }

    /// <summary>Gets or sets whether a chord is open.</summary>
    internal bool InChord { get; set; }

    /// <summary>Gets or sets whether the open chord has taken its first note.</summary>
    internal bool IsFirstChordNote { get; set; }

    /// <summary>Gets or sets the open chord's numerator.</summary>
    internal double ChordNum { get; set; } = -1;

    /// <summary>Gets or sets the open chord's denominator.</summary>
    internal double ChordDen { get; set; } = -1;

    /// <summary>Gets or sets the open chord's dots.</summary>
    internal int ChordCurrentDots { get; set; } = -1;

    /// <summary>
    /// Gets or sets whether a chord opened with the deprecated <c>+</c> delimiter is
    /// open.
    /// </summary>
    internal bool PlusChord { get; set; }

    /// <summary>Gets or sets the octave every note in this voice starts from.</summary>
    internal int BaseOctave { get; set; }

    /// <summary>Gets or sets whether the meter is being written as common time.</summary>
    internal bool CommonTime { get; set; } = true;

    /// <summary>Gets or sets whether a beam is open.</summary>
    internal bool ParsingBeam { get; set; }

    /// <summary>Forgets this bar's accidentals.</summary>
    internal void ClearBarAccidentals() => InAccidentals = new Dictionary<int, int>();
}
