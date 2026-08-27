/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 1999--2026 Han-Wen Nienhuys <hanwen@xs4all.nl>

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
using CodeBrix.LilyPort.Flower;

namespace CodeBrix.LilyPort.Engine.Fonts; //was previously: lily/all-font-metrics.cc, lily/include/all-font-metrics.hh;

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port.

/// <summary>
/// The font cache: loads an Emmentaler by name and hands out the same instance every
/// time it is asked for again.
/// <para>
/// The cache matters for more than speed. A font's identity is what the layout's
/// scaled-font table is keyed on, and it is what the backend writes into a
/// <c>named-glyph</c> expression — so loading the same file twice would produce two
/// fonts that compare unequal and defeat both.
/// </para>
/// <para>
/// DIVERGENCE, recorded in PORT-COVERAGE: upstream finds font files through
/// FontConfig, seeded with LilyPond's installed data directory. The port has no
/// FontConfig dependency and carries its fonts inside the assembly, so it asks
/// <see cref="FontAssets"/> — which a consuming application may put a directory in
/// front of, and which nothing else can make fail.
/// </para>
/// </summary>
public static class AllFontMetrics
{
    private static readonly object Gate = new object();

    private static readonly Dictionary<string, OpenTypeFontMetric> Cache
        = new Dictionary<string, OpenTypeFontMetric>(StringComparer.Ordinal);

    /// <summary>
    /// Gets the directories consulted before the assembly's own embedded fonts.
    /// Empty by default.
    /// </summary>
    public static IList<string> SearchPaths => FontAssets.SearchPaths;

    /// <summary>
    /// Loads a music font by name, or returns the instance already loaded.
    /// <para>
    /// A name this port has no font for is a FATAL ERROR, exactly as it is upstream
    /// (<c>lily/all-font-metrics.cc:163-168</c>: an empty <c>search_path_.find</c>
    /// goes straight to <c>error</c>). This method therefore never returns
    /// <see langword="null"/>.
    /// </para>
    /// </summary>
    /// <remarks>
    /// //was previously: this warned through <c>Warn.Warning</c> and returned
    /// <see langword="null"/>, and the port called that "recoverable". It is not, and
    /// nothing recovered it — <c>FontInterface.SelectFont</c> handed the null on,
    /// <c>GetDefaultFont</c> propagated it, and its ~40 call sites dereference the
    /// result immediately (<c>GetDefaultFont(me).FindByName(...)</c>). Through Scheme it
    /// surfaced as <c>wrong-type-arg ("ly:font-get-glyph" ...)</c> — an internal error
    /// naming a procedure the user never wrote, where upstream names the font it could
    /// not find and the path it looked on. MEASURED 2026-08-26 with
    /// <c>property-defaults.fonts.music = "spikefont"</c>: two warnings and then that
    /// wrong-type-arg. Reported from Fresco.Brix, whose W12B adds a music-font
    /// install/remove UI and so makes the state reachable by ordinary use.
    /// <para>
    /// The message names the port's OWN search world rather than a FontConfig path,
    /// which is ruling R18's standing answer for the two font-world queries and is the
    /// only honest thing to print here: there is no host font path to report.
    /// </para>
    /// </remarks>
    /// <param name="name">The font name without a suffix, such as <c>emmentaler-20</c>.</param>
    /// <returns>The font. Never <see langword="null"/>.</returns>
    /// <exception cref="Flower.LilyPondErrorException">There is no such font.</exception>
    public static OpenTypeFontMetric FindOtfFont(string name)
    {
        if (name == null)
        {
            throw new ArgumentNullException(nameof(name));
        }

        lock (Gate)
        {
            if (Cache.TryGetValue(name, out OpenTypeFontMetric cached))
            {
                return cached;
            }

            byte[] bytes = FontAssets.MusicFont(name);
            if (bytes == null)
            {
                Warn.Error(
                    "cannot find font '" + name + "' (search path: "
                    + DescribeSearchPath() + ")");
            }

            OpenTypeFontMetric metric = new OpenTypeFontMetric(
                new OpenTypeFont(bytes, name, Bootstrap.LilyPondScheme.Current), name);
            Cache[name] = metric;
            return metric;
        }
    }

    /// <summary>
    /// Describes where a music font is looked for, for the not-found message.
    /// </summary>
    /// <remarks>
    /// Upstream prints <c>search_path_.to_string ()</c>, a FontConfig path. The port has
    /// no such thing: it reads the override directories a consuming application put in
    /// front (normally none) and then its own embedded copies. Ruling R18 already settled
    /// that the port reports its OWN font world rather than pretending to upstream's, and
    /// this message follows it.
    /// </remarks>
    /// <returns>The description.</returns>
    private static string DescribeSearchPath()
    {
        const string Embedded = "the fonts embedded in CodeBrix.LilyPort.Engine";

        IList<string> overrides = FontAssets.SearchPaths;
        return overrides.Count == 0
            ? Embedded
            : string.Join(", ", overrides) + ", then " + Embedded;
    }

    /// <summary>
    /// Discards every loaded font. This is <c>ly:reset-all-fonts</c>, which the Scheme
    /// layer calls when the global staff size changes.
    /// </summary>
    public static void ResetAllFonts()
    {
        lock (Gate)
        {
            Cache.Clear();
        }
    }
}
