using System;
using System.IO;
using System.Text;
using System.Security.Cryptography;
using System.Xml;
using System.Xml.Linq;
using Phinix.InventoryExtension;
using Utils.Framework;

namespace Phinix.InventoryExtension.Client
{
    public sealed partial class BuiltInInventoryClientExtension
    {
        public event EventHandler SavedCheckpoint;
        // Read the actual completed save, not the in-memory ledger/API success or record counts.
        public InventorySavedDepositReceipt ReadSavedDepositReceipt(InventoryDeposit deposit)
        {
            if(!Ready(out _) || journal==null || attachedSave?.JournalIdentity!=journal.Identity || deposit==null)return null;
            string file=attachedIdentity;if(!File.Exists(file)||new FileInfo(file).Length>512L*1024*1024)return null;
            try
            {
                var settings=new XmlReaderSettings {DtdProcessing=DtdProcessing.Prohibit,MaxCharactersInDocument=512L*1024*1024};
                using(var input=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.Read))
                using(var reader=XmlReader.Create(input,settings))
                {
                    XElement match=null;
                    while(reader.Read())
                    {
                        if(reader.NodeType!=XmlNodeType.Element || reader.GetAttribute("Class")!=typeof(InventoryGameComponent).FullName)continue;
                        using(var subtree=reader.ReadSubtree())
                        {var node=XElement.Load(subtree);if(match!=null)throw new InvalidDataException("Ambiguous inventory save component.");match=node;}
                    }
                    if(match==null || (string)match.Element("phinixInventorySaveId")!=attachedSave.SaveId ||
                        (string)match.Element("phinixInventoryJournalIdentity")!=journal.Identity)return null;
                    string json=(string)match.Element("phinixInventorySnapshot");if(string.IsNullOrWhiteSpace(json))return null;
                    var saved=new InventoryLedger();saved.Restore(FrameworkSerialization.DeserializePayload<InventoryState>(json));
                    if(saved.CheckDeposit(deposit).Status!=InventoryDepositStatus.AlreadyCommitted)return null;
                    string fingerprint;using(var sha=SHA256.Create())fingerprint=BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(attachedSave.SaveId+"\n"+journal.Identity+"\n"+json))).Replace("-","");
                    return new InventorySavedDepositReceipt {SaveIdentity=GetStatus().SaveIdentity,JournalIdentity=journal.Identity,Sequence=saved.Sequence,CheckpointFingerprint=fingerprint};
                }
            }
            catch(Exception error) when(error is IOException || error is XmlException || error is System.Runtime.Serialization.SerializationException || error is InvalidOperationException)
            {host?.Log?.Invoke("[Inventory] Saved receipt unavailable: "+error.GetType().Name,Utils.LogLevel.WARNING);return null;}
        }
    }
}
