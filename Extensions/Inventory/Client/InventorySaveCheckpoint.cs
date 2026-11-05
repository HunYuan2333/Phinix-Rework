using System;
using System.IO;
using System.Xml;
using Utils.Framework;

namespace Phinix.InventoryExtension.Client
{
    // A read-back lease over the actual completed save. A save callback alone is not evidence.
    internal sealed class InventorySaveCheckpoint : IDisposable
    {
        private readonly FileStream file;
        internal InventoryState State { get; }
        private InventorySaveCheckpoint(FileStream file, InventoryState state) { this.file = file; State = state; }
        internal static InventorySaveCheckpoint Open(string path, string saveId, InventoryState expected, string expectedJournalIdentity = null)
        {
            if (string.IsNullOrEmpty(saveId)) throw new InvalidDataException("Checkpoint save identity is missing.");
            InventoryLedger.Validate(expected);
            var file = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
            try
            {
                if (file.Length > 512L * 1024 * 1024) throw new InvalidDataException("Checkpoint save exceeds the read-back limit.");
                string identity = null, json = null, journalIdentity = null;
                int components = 0;
                var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null,
                    MaxCharactersInDocument = 512L * 1024 * 1024, CloseInput = false };
                using (var reader = XmlReader.Create(file, settings))
                {
                    var ancestry = new string[4];
                    while (reader.Read())
                    {
                        if (reader.NodeType == XmlNodeType.Element && reader.Depth < ancestry.Length) ancestry[reader.Depth] = reader.Name;
                        if (reader.Depth != 3 || ancestry[0] != "savegame" || ancestry[1] != "game" || ancestry[2] != "components" || reader.Name != "li" || reader.NodeType != XmlNodeType.Element || reader.GetAttribute("Class") !=
                            "Phinix.InventoryExtension.Client.InventoryGameComponent") continue;
                        components++;
                        using (var node = reader.ReadSubtree())
                        {
                            node.Read();
                            while (node.Read())
                            {
                                if (node.NodeType != XmlNodeType.Element || node.Depth != 1) continue;
                                // ReadSubtree avoids skipping adjacent elements after ReadElementContentAsString.
                                if (node.Name == "phinixInventorySaveId")
                                { if (identity != null) throw new InvalidDataException("Duplicate checkpoint identity."); using (var value = node.ReadSubtree()) { value.Read(); identity = value.ReadElementContentAsString(); } }
                                else if (node.Name == "phinixInventoryJournalIdentity")
                                { if (journalIdentity != null) throw new InvalidDataException("Duplicate checkpoint authority."); using (var value = node.ReadSubtree()) { value.Read(); journalIdentity = value.ReadElementContentAsString(); } }
                                else if (node.Name == "phinixInventorySnapshot")
                                { if (json != null) throw new InvalidDataException("Duplicate checkpoint snapshot."); using (var value = node.ReadSubtree()) { value.Read(); json = value.ReadElementContentAsString(); } }
                            }
                        }
                    }
                }
                if (components != 1 || identity != saveId || string.IsNullOrEmpty(json) || expectedJournalIdentity != null && journalIdentity != expectedJournalIdentity)
                    throw new InvalidDataException("Checkpoint save identity or inventory component does not match.");
                var state = FrameworkSerialization.DeserializePayload<InventoryState>(json);
                InventoryLedger.Validate(state);
                if (InventoryJournal.Fingerprint(state) != InventoryJournal.Fingerprint(expected))
                    throw new InvalidDataException("Checkpoint save does not match the captured inventory snapshot.");
                // Force the file read back above to stable storage while denying concurrent write/delete.
                file.Flush(true);
                return new InventorySaveCheckpoint(file, state);
            }
            catch { file.Dispose(); throw; }
        }
        public void Dispose() { file.Dispose(); }
    }
}
