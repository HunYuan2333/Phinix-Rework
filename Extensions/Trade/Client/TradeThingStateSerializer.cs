using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml;
using RimWorld;
using Verse;

namespace Phinix.TradeExtension.Client
{
    /// <summary>
    /// Round-trips a complete Thing through RimWorld's normal Scribe contract so
    /// third-party ThingComp.PostExposeData implementations remain authoritative.
    /// Scribe is global and main-thread-sensitive; callers must use this only from
    /// the existing game/UI dispatch path.
    /// </summary>
    internal static class TradeThingStateSerializer
    {
        private const int MaxExpandedXmlBytes = 16 * 1024 * 1024;

        public static byte[] Serialize(Thing thing)
        {
            if (thing == null) throw new ArgumentNullException(nameof(thing));
            ensureScribeInactive("serialize");

            string xml = Scribe.saver.DebugOutputFor(thing);
            if (string.IsNullOrEmpty(xml))
            {
                throw new InvalidOperationException($"Scribe returned no state for trade item '{thing.def?.defName ?? "unknown"}'.");
            }

            byte[] xmlBytes = Encoding.UTF8.GetBytes(xml);
            if (xmlBytes.Length > MaxExpandedXmlBytes)
            {
                throw new InvalidOperationException($"Trade item state is too large ({xmlBytes.Length} bytes expanded).");
            }

            using (MemoryStream output = new MemoryStream())
            {
                using (GZipStream gzip = new GZipStream(output, CompressionMode.Compress, true))
                {
                    gzip.Write(xmlBytes, 0, xmlBytes.Length);
                }

                byte[] compressed = output.ToArray();
                if (compressed.Length > PhinixClient.Trade.StatefulTradeItemProtocol.MaxStatePayloadBytes)
                {
                    throw new InvalidOperationException($"Trade item state is too large ({compressed.Length} bytes compressed).");
                }

                return compressed;
            }
        }

        public static Thing Deserialize(byte[] compressedState)
        {
            if (compressedState == null || compressedState.Length == 0)
            {
                throw new InvalidOperationException("Stateful trade item has no Scribe payload.");
            }

            if (compressedState.Length > PhinixClient.Trade.StatefulTradeItemProtocol.MaxStatePayloadBytes)
            {
                throw new InvalidOperationException($"Stateful trade item payload exceeds {PhinixClient.Trade.StatefulTradeItemProtocol.MaxStatePayloadBytes} bytes.");
            }

            ensureScribeInactive("deserialize");
            string xml = decompressXml(compressedState);
            XmlDocument document = loadXml(xml);
            XmlNode thingNode = document.DocumentElement;
            if (thingNode == null)
            {
                throw new InvalidOperationException("Stateful trade item XML has no root node.");
            }

            if (thingNode.Name != "saveable" && thingNode["saveable"] != null)
            {
                thingNode = thingNode["saveable"];
            }

            Thing thing = null;
            try
            {
                Scribe.mode = LoadSaveMode.LoadingVars;
                Scribe.loader.curXmlParent = thingNode;
                Scribe.loader.curParent = null;
                Scribe.loader.curPathRelToParent = null;

                thing = ScribeExtractor.SaveableFromNode<Thing>(thingNode, new object[0]);
                if (thing == null || thing.def == null)
                {
                    throw new InvalidOperationException("Stateful trade item could not resolve its ThingDef. The receiving client may be missing a required mod.");
                }

                Scribe.loader.crossRefs.ResolveAllCrossReferences();
                Scribe.loader.initer.DoAllPostLoadInits();
                assignFreshIds(thing);
                return thing;
            }
            finally
            {
                // FinalizeLoading validates that Scribe is still in a loading
                // phase and performs the transition back to Inactive itself.
                // Setting Inactive first succeeds functionally but emits a
                // Verse.Log.Error every time the trade UI reconstructs an item.
                if (Scribe.mode != LoadSaveMode.Inactive)
                {
                    try
                    {
                        Scribe.loader.FinalizeLoading();
                    }
                    finally
                    {
                        Scribe.mode = LoadSaveMode.Inactive;
                    }
                }
            }
        }

        internal static void CollectMissingThingDefs(byte[] compressedState, ISet<string> missing)
        {
            if (missing == null) throw new ArgumentNullException(nameof(missing));
            if (compressedState == null || compressedState.Length == 0) return;

            XmlDocument document = loadXml(decompressXml(compressedState));
            XmlNodeList references = document.SelectNodes("//def|//stuff|//ingredients/li");
            if (references == null) return;
            foreach (XmlNode reference in references)
            {
                string defName = reference?.InnerText?.Trim();
                if (string.IsNullOrEmpty(defName)) continue;
                if (DefDatabase<ThingDef>.GetNamedSilentFail(defName) == null) missing.Add(defName);
            }
        }

        private static string decompressXml(byte[] compressedState)
        {
            using (MemoryStream input = new MemoryStream(compressedState, false))
            using (GZipStream gzip = new GZipStream(input, CompressionMode.Decompress))
            using (MemoryStream output = new MemoryStream())
            {
                byte[] buffer = new byte[8192];
                int read;
                while ((read = gzip.Read(buffer, 0, buffer.Length)) > 0)
                {
                    if (output.Length + read > MaxExpandedXmlBytes)
                    {
                        throw new InvalidOperationException($"Stateful trade item XML exceeds {MaxExpandedXmlBytes} bytes.");
                    }
                    output.Write(buffer, 0, read);
                }
                return Encoding.UTF8.GetString(output.ToArray());
            }
        }

        private static XmlDocument loadXml(string xml)
        {
            XmlReaderSettings settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null
            };
            XmlDocument document = new XmlDocument { XmlResolver = null };
            using (StringReader textReader = new StringReader(xml))
            using (XmlReader reader = XmlReader.Create(textReader, settings))
            {
                document.Load(reader);
            }
            return document;
        }

        private static void assignFreshIds(Thing thing)
        {
            if (thing == null || Find.UniqueIDsManager == null) return;

            thing.thingIDNumber = -1;
            thing.thingIDNumber = Find.UniqueIDsManager.GetNextThingID();

            MinifiedThing minified = thing as MinifiedThing;
            if (minified?.InnerThing != null)
            {
                assignFreshIds(minified.InnerThing);
            }
        }

        private static void ensureScribeInactive(string operation)
        {
            if (Scribe.mode != LoadSaveMode.Inactive)
            {
                throw new InvalidOperationException($"Cannot {operation} a trade item while Scribe is busy ({Scribe.mode}).");
            }
        }
    }
}
