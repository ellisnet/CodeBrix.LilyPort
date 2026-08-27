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
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace CodeBrix.LilyPort.Importers; //was previously: python/musicxml.py (minidom_demarshal_node) and scripts/musicxml2ly.py (read_xml, read_musicxml);

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port.

/// <summary>
/// Reads a MusicXML document into the class hierarchy the converter walks.
/// </summary>
internal static class MusicXmlReader
{
    /// <summary>
    /// How the XML is read.
    /// </summary>
    /// <remarks>
    /// ⚠ WHITESPACE IS SIGNIFICANT TO THIS PORT and the settings say so. Upstream reads
    /// with <c>xml.dom.minidom</c>, which keeps every text node including the
    /// indentation between elements, and several places in the converter step over
    /// those nodes by name (<c>Hash_text</c>) rather than by content — a reader that
    /// dropped them would change which child is 'first'.
    /// <para>
    /// The DOCTYPE every MusicXML file carries is IGNORED rather than resolved:
    /// expat, which minidom uses, does not fetch an external DTD either, and a
    /// converter that reached out to w3.org for a schema while importing a local file
    /// would be doing something upstream does not.
    /// </para>
    /// </remarks>
    private static readonly XmlReaderSettings ReaderSettings = new XmlReaderSettings
    {
        DtdProcessing = DtdProcessing.Ignore,
        IgnoreWhitespace = false,
        IgnoreComments = false,
        IgnoreProcessingInstructions = true,
        XmlResolver = null,
    };

    /// <summary>Reads a document from text.</summary>
    /// <param name="state">The import this document belongs to.</param>
    /// <param name="xmlText">The document.</param>
    /// <returns>The root node.</returns>
    internal static MusicXmlNode ReadXml(MusicXmlImportState state, string xmlText)
    {
        using (StringReader text = new StringReader(xmlText))
        using (XmlReader reader = XmlReader.Create(text, ReaderSettings))
        {
            return ReadDocument(state, reader);
        }
    }

    /// <summary>Reads a document from bytes.</summary>
    /// <param name="state">The import this document belongs to.</param>
    /// <param name="xmlBytes">The document.</param>
    /// <returns>The root node.</returns>
    internal static MusicXmlNode ReadXml(MusicXmlImportState state, byte[] xmlBytes)
    {
        using (MemoryStream stream = new MemoryStream(xmlBytes, writable: false))
        using (XmlReader reader = XmlReader.Create(stream, ReaderSettings))
        {
            return ReadDocument(state, reader);
        }
    }

    private static MusicXmlNode ReadDocument(MusicXmlImportState state, XmlReader reader)
    {
        XDocument document;
        try
        {
            document = XDocument.Load(reader, LoadOptions.PreserveWhitespace);
        }
        catch (XmlException exception)
        {
            //Upstream's parse raises here and nothing catches it, so the script ends
            //without writing a file.
            throw new ImportAbortedException(exception.Message);
        }

        return DemarshalNode(state, document.Root, null);
    }

    /// <summary>
    /// Reads a compressed MusicXML container, following its manifest to the score.
    /// </summary>
    /// <param name="state">The import this document belongs to.</param>
    /// <param name="mxlData">The container.</param>
    /// <returns>The root node, or null when the container names no score.</returns>
    internal static MusicXmlNode ReadCompressed(MusicXmlImportState state, byte[] mxlData)
    {
        using (MemoryStream stream = new MemoryStream(mxlData, writable: false))
        using (ZipArchive archive = OpenArchive(stream))
        {
            byte[] containerXml = ReadEntry(archive, "META-INF/container.xml");
            if (containerXml == null || containerXml.Length == 0)
            {
                return null;
            }

            MusicXmlNode container = ReadXml(state, containerXml);
            if (container == null)
            {
                return null;
            }

            MusicXmlNode rootfiles = container.GetMaybeExistNamedChild("rootfiles");
            if (rootfiles == null)
            {
                return null;
            }

            List<MusicXmlNode> rootfileList = rootfiles.GetNamedChildren("rootfile");
            string musicXmlFile = rootfileList.Count > 0
                ? rootfileList[0].Attribute("full-path")
                : null;
            if (string.IsNullOrEmpty(musicXmlFile))
            {
                return null;
            }

            byte[] rawBytes = ReadEntry(archive, musicXmlFile);
            if (rawBytes == null)
            {
                //python's ZipFile.read raises KeyError for a name it does not hold.
                throw new ImportAbortedException(
                    "There is no item named '" + musicXmlFile + "' in the archive");
            }

            return ReadXml(state, rawBytes);
        }
    }

    private static ZipArchive OpenArchive(Stream stream)
    {
        try
        {
            return new ZipArchive(stream, ZipArchiveMode.Read);
        }
        catch (InvalidDataException exception)
        {
            throw new ImportAbortedException(exception.Message);
        }
    }

    private static byte[] ReadEntry(ZipArchive archive, string name)
    {
        ZipArchiveEntry entry = archive.GetEntry(name);
        if (entry == null)
        {
            return null;
        }

        using (Stream entryStream = entry.Open())
        using (MemoryStream buffer = new MemoryStream())
        {
            entryStream.CopyTo(buffer);
            return buffer.ToArray();
        }
    }

    /// <summary>The text a reducer sees: the element's own text children, joined.</summary>
    /// <param name="node">The element.</param>
    /// <returns>The text.</returns>
    /// <remarks>
    /// Upstream's four reducers all start with the same comprehension over
    /// <c>childNodes</c> filtered to <c>TEXT_NODE</c> — so a comment between two runs
    /// of text is skipped, and a nested element's text is NOT included.
    /// </remarks>
    private static string DirectText(XElement node)
    {
        StringBuilder builder = new StringBuilder();
        foreach (XNode child in node.Nodes())
        {
            if (child is XText text && !(child is XCData))
            {
                builder.Append(text.Value);
            }
        }

        return builder.ToString();
    }

    /// <summary>Reads one XML node into the class hierarchy.</summary>
    /// <param name="state">The import this document belongs to.</param>
    /// <param name="node">The XML node.</param>
    /// <param name="parent">The node already built for its parent.</param>
    /// <returns>The node built, or null when the schema reduced it to a value.</returns>
    private static MusicXmlNode DemarshalNode(
        MusicXmlImportState state, XNode node, MusicXmlNode parent)
    {
        string name = NodeName(node);

        //For certain leaf elements of the schema, instead of creating a full child
        //node, we just create a value.
        Func<string, object> toValue = MusicXmlClassMap.GetValueReader(name);
        if (toValue != null && node is XElement valueElement)
        {
            object value = toValue(DirectText(valueElement));
            Dictionary<string, int> parentMap = parent?.MaxOccursByChild;
            if (parentMap != null)
            {
                //A parent that declares a map but not this name raises KeyError, which
                //upstream does NOT catch here.
                if (!parentMap.TryGetValue(name, out int declared))
                {
                    throw new ImportAbortedException(name);
                }

                //TODO (upstream's): Create lists when `max_occurs_by_child' > 1?
                if (declared != 1)
                {
                    throw new ImportAbortedException("assertion failed for <" + name + ">");
                }

                parent.Content[name] = value;
                return null;
            }
        }

        //Create a node
        MusicXmlNode built = MusicXmlClassMap.CreateNode(name);
        built.State = state;
        built.Parent = parent;

        if (node is XElement element)
        {
            List<MusicXmlNode> children = new List<MusicXmlNode>();
            foreach (XNode child in element.Nodes())
            {
                MusicXmlNode childNode = DemarshalNode(state, child, built);
                if (childNode != null)
                {
                    children.Add(childNode);
                }
            }

            //⚠ python appends the RESULT of every recursive call, so a child the schema
            //reduced to a value leaves `None' in the children list; nothing ever reads
            //those entries by index, and every walk over them asks isinstance first.
            //The port drops them instead of carrying nulls that every `is' test would
            //have to guard.
            built.Children = children;

            foreach (XAttribute attribute in element.Attributes())
            {
                if (attribute.IsNamespaceDeclaration)
                {
                    continue;
                }

                //⚠ THE QUALIFIED NAME, not the local one. python's minidom answers
                //`attributes.items()' with the names exactly as the document spells them,
                //and two of the names this converter reads carry a prefix:
                //`xml:space="preserve"' decides whether a text element's line breaks
                //survive into a `\center-column', and `xml:lang' is in the carry-over
                //exclusion set. XLinq splits a prefix off into a namespace, so it is put
                //back here.
                built.AttributeDict[QualifiedName(element, attribute)] = attribute.Value;
            }
        }
        else if (node is XText text)
        {
            built.Data = text.Value;
        }


        Dictionary<string, int> map = parent?.MaxOccursByChild;
        int maxOccurs = 0;
        if (map != null)
        {
            map.TryGetValue(name, out maxOccurs);
        }

        if (maxOccurs == 1)
        {
            parent.Content[name] = built;
        }
        else if (maxOccurs == 2)
        {
            //The parent's constructor is required to initialize an empty list.
            ((List<MusicXmlNode>)parent.Content[name]).Add(built);
        }

        return built;
    }

    private static string NodeName(XNode node)
    {
        switch (node)
        {
            case XElement element:
                return element.Name.LocalName;
            case XComment _:
                return "#comment";
            case XText _:
                return "#text";
            default:
                return "#unknown";
        }
    }
    /// <summary>The name an attribute has in the document, prefix and all.</summary>
    /// <param name="element">The element the attribute is on.</param>
    /// <param name="attribute">The attribute.</param>
    /// <returns>The qualified name.</returns>
    private static string QualifiedName(XElement element, XAttribute attribute)
    {
        XNamespace space = attribute.Name.Namespace;
        if (space == XNamespace.None)
        {
            return attribute.Name.LocalName;
        }

        string prefix = space == XNamespace.Xml
            ? "xml"
            : element.GetPrefixOfNamespace(space);
        return string.IsNullOrEmpty(prefix)
            ? attribute.Name.LocalName
            : prefix + ":" + attribute.Name.LocalName;
    }

}
