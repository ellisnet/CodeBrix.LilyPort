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
using System.Text;

namespace CodeBrix.LilyPort.Importers; //was previously: python/musicxml.py (Xml_node and its immediate subclasses);

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port.

/// <summary>
/// One node of a demarshalled MusicXML document.
/// </summary>
/// <remarks>
/// A node presents itself as a dictionary of children keyed by MusicXML element name.
/// The class variable <see cref="MaxOccursByChild"/> specifies which elements to track
/// with this dictionary.
/// <para>
/// When <c>max_occurs_by_child</c> is 1, a successful lookup accesses the child node.
/// For optional elements, the lookup may fail. When it is 2 (meaning not limited) the
/// lookup accesses a list of child nodes, which may be empty; the class specifying 2 is
/// responsible for initializing an empty list in its instances so that the lookup
/// cannot fail.
/// </para>
/// <para>
/// ⚠ THE CLASS HIERARCHY IS THE LOOKUP. Upstream asks <c>isinstance</c>, so
/// <c>get_named_children("octave")</c> also answers <c>&lt;display-octave&gt;</c>
/// elements, whose class derives from <c>Octave</c>. The port keeps the same
/// derivations and the same test for exactly that reason — flattening them would
/// silently drop children upstream finds.
/// </para>
/// </remarks>
internal class MusicXmlNode
{
    /// <summary>The children, in document order, text and comments included.</summary>
    internal List<MusicXmlNode> Children { get; set; } = new List<MusicXmlNode>();

    /// <summary>
    /// The children this class tracks by name: a node, a list of nodes, or a scalar
    /// value for a leaf the schema reduces to one.
    /// </summary>
    internal Dictionary<string, object> Content { get; set; } = new Dictionary<string, object>();

    /// <summary>The text a text node carries; null for an element.</summary>
    internal string Data { get; set; }

    /// <summary>The XML attributes, exactly as they were written.</summary>
    internal Dictionary<string, string> AttributeDict { get; set; } = new Dictionary<string, string>();

    /// <summary>The node this one hangs under, or null at the root.</summary>
    internal MusicXmlNode Parent { get; set; }

    /// <summary>Builds the node, giving it the element name its class is registered under.</summary>
    /// <remarks>
    /// ⚠ Upstream's element name is a CLASS attribute (see
    /// <see cref="MusicXmlClassMap.GetName"/>), so a node the converter builds by hand —
    /// the <c>&lt;words&gt;</c> nodes a lyric or a dynamics markup is assembled from —
    /// answers its element name without having been demarshalled. Setting it here is that
    /// class attribute.
    /// </remarks>
    protected MusicXmlNode()
        => ElementName = MusicXmlClassMap.GetName(GetType()) ?? "xml_node";

    /// <summary>The MusicXML element name.</summary>
    internal string ElementName { get; set; }

    /// <summary>The state of the import this node belongs to.</summary>
    /// <remarks>
    /// Upstream reaches its diagnostics through the <c>ly</c> module and its counters
    /// through module globals; a library has neither, so the state travels with the
    /// tree. Set once, during demarshalling.
    /// </remarks>
    internal MusicXmlImportState State { get; set; }

    /// <summary>
    /// The children this class tracks by name, and how many of each it expects.
    /// </summary>
    /// <remarks>
    /// ⚠ NULL AND EMPTY ARE DIFFERENT HERE, and the difference is upstream's. A class
    /// that declares no <c>max_occurs_by_child</c> at all raises <c>AttributeError</c>
    /// when the demarshaller looks, which it CATCHES — so the child becomes an
    /// ordinary node. A class that declares one but does not name the child raises
    /// <c>KeyError</c>, which it does NOT catch on the value path, and the script ends.
    /// Null stands for the first case; an empty dictionary would silently turn it into
    /// the second.
    /// </remarks>
    internal virtual Dictionary<string, int> MaxOccursByChild => null;

    /// <summary>python's <c>key in node</c>.</summary>
    /// <param name="key">The element name.</param>
    /// <returns>Whether the node tracks a child of that name.</returns>
    internal bool Has(string key) => Content.ContainsKey(key);

    /// <summary>python's <c>node[key]</c>, which raises when the key is absent.</summary>
    /// <param name="key">The element name.</param>
    /// <returns>The tracked child.</returns>
    internal object Item(string key)
    {
        if (!Content.TryGetValue(key, out object value))
        {
            throw new KeyNotFoundException(key);
        }

        return value;
    }

    /// <summary>python's <c>node.get(key, default)</c>.</summary>
    /// <param name="key">The element name.</param>
    /// <param name="defaultValue">What to answer when the key is absent.</param>
    /// <returns>The tracked child, or the default.</returns>
    internal object Get(string key, object defaultValue = null)
        => Content.TryGetValue(key, out object value) ? value : defaultValue;

    /// <summary>The tracked child as a node.</summary>
    /// <param name="key">The element name.</param>
    /// <returns>The node, or null when absent.</returns>
    internal MusicXmlNode GetNode(string key) => Get(key) as MusicXmlNode;

    /// <summary>The tracked list of children.</summary>
    /// <param name="key">The element name.</param>
    /// <returns>The list; never null for a class that declared the name.</returns>
    internal List<MusicXmlNode> GetList(string key)
        => Get(key) as List<MusicXmlNode> ?? new List<MusicXmlNode>();

    /// <summary>The tracked child as the whole number the schema reduces it to.</summary>
    /// <param name="key">The element name.</param>
    /// <returns>The value.</returns>
    internal int GetInt(string key) => Convert.ToInt32(Item(key), CultureInfo.InvariantCulture);

    /// <summary>The tracked child as the string the schema reduces it to.</summary>
    /// <param name="key">The element name.</param>
    /// <returns>The value, or null when absent.</returns>
    internal string GetString(string key) => Get(key) as string;

    /// <summary>Gets one XML attribute — python's <c>getattr(node, name, None)</c>.</summary>
    /// <param name="name">The attribute name.</param>
    /// <returns>The value, or null when the attribute is absent.</returns>
    internal string Attribute(string name)
        => AttributeDict.TryGetValue(name, out string value) ? value : null;

    /// <summary>
    /// Gets one XML attribute with a fallback — python's
    /// <c>getattr(node, name, default)</c>.
    /// </summary>
    /// <param name="name">The attribute name.</param>
    /// <param name="defaultValue">What to answer when the attribute is absent.</param>
    /// <returns>The value, or the default.</returns>
    internal string Attribute(string name, string defaultValue)
        => AttributeDict.TryGetValue(name, out string value) ? value : defaultValue;

    /// <summary>python's <c>hasattr(node, name)</c> for an XML attribute.</summary>
    /// <param name="name">The attribute name.</param>
    /// <returns>Whether the attribute is present.</returns>
    internal bool HasAttribute(string name) => AttributeDict.ContainsKey(name);

    /// <summary>The element name, as upstream's class method answers it.</summary>
    /// <returns>The name.</returns>
    internal string GetName() => ElementName;

    /// <summary>The node's own text, or its children's joined together.</summary>
    /// <returns>The text.</returns>
    internal virtual string GetText()
    {
        if (!string.IsNullOrEmpty(Data))
        {
            return Data;
        }

        if (Children.Count == 0)
        {
            return string.Empty;
        }

        StringBuilder builder = new StringBuilder();
        foreach (MusicXmlNode child in Children)
        {
            builder.Append(child.GetText());
        }

        return builder.ToString();
    }

    /// <summary>Reports something about this node, naming where it happened.</summary>
    /// <param name="message">What to say.</param>
    /// <remarks>
    /// Upstream follows the warning with one <c>ly.progress</c> line per enclosing
    /// element. Those are PROGRESS-level and belong to the command-line driver, which
    /// the port deliberately does not have (D58) — so the warning is reported and the
    /// location chain is not, which is exactly what a user sees from upstream at any
    /// log level below PROGRESS. The trailing colon upstream appends is kept: it is
    /// what introduces that chain, and it is in every recorded fixture.
    /// </remarks>
    internal void Message(string message) => State.Warning(message + ":");

    /// <summary>Every child that is an instance of the given class.</summary>
    /// <typeparam name="T">The class.</typeparam>
    /// <returns>The children, in document order.</returns>
    internal List<T> GetTypedChildren<T>()
        where T : MusicXmlNode
    {
        List<T> found = new List<T>();
        foreach (MusicXmlNode child in Children)
        {
            if (child is T typed)
            {
                found.Add(typed);
            }
        }

        return found;
    }

    /// <summary>Every child the given element name selects.</summary>
    /// <param name="name">The element name.</param>
    /// <returns>The children, in document order.</returns>
    /// <remarks>
    /// A name the schema map knows selects by CLASS, so subclasses come with it; a name
    /// it does not know selects by name, which is what upstream's synthesised class per
    /// unknown name amounts to.
    /// </remarks>
    internal List<MusicXmlNode> GetNamedChildren(string name)
    {
        Type klass = MusicXmlClassMap.GetClass(name);
        List<MusicXmlNode> found = new List<MusicXmlNode>();
        foreach (MusicXmlNode child in Children)
        {
            if (klass == null
                ? child.GetType() == typeof(MusicXmlGenericNode) && child.ElementName == name
                : klass.IsInstanceOfType(child))
            {
                found.Add(child);
            }
        }

        return found;
    }

    /// <summary>The first child the given element name selects.</summary>
    /// <param name="name">The element name.</param>
    /// <returns>The child, or null.</returns>
    internal MusicXmlNode GetNamedChild(string name) => GetMaybeExistNamedChild(name);

    /// <summary>Every child a test accepts.</summary>
    /// <param name="predicate">The test.</param>
    /// <returns>The children, in document order.</returns>
    internal List<MusicXmlNode> GetChildren(Func<MusicXmlNode, bool> predicate)
    {
        List<MusicXmlNode> found = new List<MusicXmlNode>();
        foreach (MusicXmlNode child in Children)
        {
            if (predicate(child))
            {
                found.Add(child);
            }
        }

        return found;
    }

    /// <summary>Every child.</summary>
    /// <returns>The children, in document order.</returns>
    internal List<MusicXmlNode> GetAllChildren() => Children;

    /// <summary>The first child of the given element name, if there is one.</summary>
    /// <param name="name">The element name.</param>
    /// <returns>The child, or null.</returns>
    internal MusicXmlNode GetMaybeExistNamedChild(string name)
    {
        List<MusicXmlNode> found = GetNamedChildren(name);
        if (found.Count == 0)
        {
            return null;
        }

        if (found.Count > 1)
        {
            //⚠ Upstream raises a python UserWarning here, which its runtime prints with
            //the source file and line that raised it. A library has no such machinery
            //and the file name would be a recording of upstream's tree, so the port
            //reports the message alone. Nothing in the corpus reaches this line.
            State.Warning(
                "more than one child of class " + name
                + ", all but the first will be ignored");
        }

        return found[0];
    }

    /// <summary>The first child of the given class, if there is one.</summary>
    /// <typeparam name="T">The class.</typeparam>
    /// <returns>The child, or null.</returns>
    internal T GetMaybeExistTypedChild<T>()
        where T : MusicXmlNode
    {
        List<T> found = GetTypedChildren<T>();
        if (found.Count == 0)
        {
            return null;
        }

        if (found.Count > 1)
        {
            State.Warning(
                "more than one child of class " + typeof(T).Name
                + ", all but the first will be ignored");
        }

        return found[0];
    }

    /// <summary>The whole number a named child's text carries.</summary>
    /// <param name="name">The element name.</param>
    /// <param name="defaultValue">What to answer when the child is absent.</param>
    /// <returns>The value.</returns>
    internal int GetNamedChildValueNumber(string name, int defaultValue)
    {
        MusicXmlNode child = GetMaybeExistNamedChild(name);
        return child != null
            ? int.Parse(child.GetText(), CultureInfo.InvariantCulture)
            : defaultValue;
    }
}

/// <summary>
/// An element the schema map does not name — upstream synthesises a class per such
/// name, and this stands for all of them.
/// </summary>
/// <remarks>
/// The synthesised classes exist only so that <c>isinstance</c> can tell one unknown
/// name from another, which is what <see cref="MusicXmlNode.GetNamedChildren"/> does by
/// comparing the name instead. Nothing else about them is reachable.
/// </remarks>
internal sealed class MusicXmlGenericNode : MusicXmlNode
{
}

/// <summary>
/// The chain of <c>&lt;direction-type&gt;</c> children, as one node.
/// </summary>
/// <remarks>Injected by the converter; never demarshalled from a document.</remarks>
internal sealed class MusicXmlLilyPondMarkup : MusicXmlNode
{
    /// <summary>Builds the node.</summary>
    internal MusicXmlLilyPondMarkup() => ElementName = "lilypond-markup";
}

/// <summary>
/// Most MusicXML elements are mapped to classes derived from this one.
/// </summary>
/// <remarks>
/// <see cref="When"/> and <see cref="MeasurePosition"/> are based on accumulated
/// musical length values derived from the elements' <c>&lt;duration&gt;</c> field.
/// </remarks>
internal class MusicXmlMusicNode : MusicXmlNode
{
    /// <summary>The moment this element sounds at, from the start of the part.</summary>
    internal PythonFraction? When { get; set; }

    /// <summary>How long this element lasts.</summary>
    internal PythonFraction? DurationValue { get; set; }

    /// <summary>The moment this element sounds at, from the start of its measure.</summary>
    internal PythonFraction? MeasurePosition { get; set; }

    /// <summary>The voice this element was assigned to.</summary>
    internal string VoiceId { get; set; }
}

/// <summary>
/// Keeps a voice alive across a staff it has nothing to say in.
/// </summary>
/// <remarks>Injected by the demarshaller; never read from a document.</remarks>
internal sealed class MusicXmlKeepAlive : MusicXmlMusicNode
{
}

/// <summary>An element that pairs with another to span a stretch of music.</summary>
internal class MusicXmlSpanner : MusicXmlMusicNode
{
    /// <summary>The element at the other end.</summary>
    internal MusicXmlSpanner PairedWith { get; set; }

    /// <summary>The output-side event this element became.</summary>
    internal object SpannerEvent { get; set; }

    /// <summary>Which end of the span this is.</summary>
    /// <returns>The value of the 'type' attribute.</returns>
    /// <remarks>
    /// Most subclasses represent elements with a required 'type' attribute; the ones
    /// that do not override this.
    /// </remarks>
    internal virtual string GetSpannerType() => Attribute("type");
}

/// <summary>An element that occupies a position within a measure.</summary>
internal class MusicXmlMeasureElement : MusicXmlMusicNode
{
    /// <summary>Which voice this element belongs to.</summary>
    /// <returns>The voice name.</returns>
    internal string GetVoiceId() => Get("voice", VoiceId) as string;
}
