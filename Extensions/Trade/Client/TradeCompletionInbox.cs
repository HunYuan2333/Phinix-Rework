using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Utils.Framework;

namespace Phinix.TradeExtension.Client
{
    [DataContract]
    internal sealed class TradeCompletionReceipt
    {
        [DataMember(Order=0)] public int Schema = 1;
        [DataMember(Order=1)] public string Endpoint;
        [DataMember(Order=2)] public string Recipient;
        [DataMember(Order=3)] public string TradeId;
        [DataMember(Order=4)] public bool Completed;
        [DataMember(Order=5)] public string OtherParty;
        [DataMember(Order=6)] public FrameworkItemPayload[] Items;
        [DataMember(Order=7)] public string Fingerprint;
    }

    [DataContract]
    internal sealed class TradeCompletionAttempt
    {
        [DataMember(Order=0)] public int Schema = 1;
        [DataMember(Order=1)] public string ReceiptFingerprint;
        [DataMember(Order=2)] public string Mode;
        [DataMember(Order=3)] public string State;
        [DataMember(Order=4)] public string Fingerprint;
    }

    [DataContract]
    internal sealed class TradeCompletionSaveBinding
    {
        [DataMember(Order=0)] public int Schema=1;
        [DataMember(Order=1)] public string ReceiptFingerprint;
        [DataMember(Order=2)] public string SaveIdentity;
        [DataMember(Order=3)] public string Fingerprint;
    }

    // Receipt evidence only: never a game-save ACK, refund authority, or automatic replay queue.
    internal sealed partial class TradeCompletionInbox
    {
        private const int MaxRecord = 4 * 1024 * 1024, MaxTotal = 32 * 1024 * 1024, MaxRecords = 4096;
        private readonly string directory;
        private readonly object gate = new object();
        private long revision;
        public long Revision => Interlocked.Read(ref revision);
        [DllImport("libc", SetLastError=true)] private static extern int chmod(string path, uint mode);
        public TradeCompletionInbox(string directory)
        {
            if (!Path.IsPathRooted(directory)) throw new ArgumentException("Absolute inbox path required.");
            this.directory = Path.GetFullPath(directory);
            CheckEvidencePath(this.directory);Directory.CreateDirectory(this.directory); Private(this.directory, 448);
            ReadAll();ValidateDeliveryEvidence(); // Corrupt records remain in place and prevent activation.
            foreach (var path in Directory.GetFiles(this.directory,"*.attempt").Concat(Directory.GetFiles(this.directory,"*.result")))
            {
                string receipt = Path.ChangeExtension(path,".receipt");
                if (!File.Exists(receipt)) throw new InvalidDataException("Orphan delivery evidence.");
                var record = Read(receipt);
                var attempt=ReadAttempt(Path.ChangeExtension(path,".attempt"),record,null,"Attempted");
                if (path.EndsWith(".result",StringComparison.Ordinal)) ReadAttempt(path,record,attempt.Mode,null);
            }
            foreach(var path in Directory.GetFiles(this.directory,"*.binding")) ReadBinding(path,Read(Path.ChangeExtension(path,".receipt")));
        }
        private static void CheckEvidencePath(string path)
        {
            // Inspect every lexical ancestor; never resolve a link into a trusted path.
            string current=Path.GetFullPath(path);int depth=0;
            while(current!=null)
            {
                try
                {
                    if((File.GetAttributes(current)&FileAttributes.ReparsePoint)!=0)
                        throw new IOException("Trade evidence path contains an unsupported filesystem link.");
                }
                catch(FileNotFoundException) { }
                catch(DirectoryNotFoundException) { }
                current=Directory.GetParent(current)?.FullName;
                if(++depth>256)throw new IOException("Trade evidence path cannot be verified.");
            }
        }
        private static void Private(string path, uint mode)
        {
            CheckEvidencePath(path);
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException("Inbox links are unsupported.");
            if (Path.DirectorySeparatorChar == '/' && chmod(path, mode) != 0) throw new IOException("Cannot protect completion inbox.");
        }
        private static void Identity(string value, int max)
        { if (string.IsNullOrWhiteSpace(value) || value.Length > max || value.Any(char.IsControl)) throw new InvalidDataException("Invalid receipt identity."); }
        private static void Validate(TradeCompletionReceipt record)
        {
            if (record == null || record.Schema != 1) throw new InvalidDataException("Unsupported receipt.");
            Identity(record.Endpoint,512); Identity(record.Recipient,128); Identity(record.TradeId,128); Identity(record.OtherParty,128);
            if (record.Items == null || record.Items.Any(i => i == null || string.IsNullOrWhiteSpace(i.CodecId) || i.Metadata?.Any(m=>m==null)==true))
                throw new InvalidDataException("Invalid receipt batch.");
        }
        private static string Hash(string text)
        { using (var sha=SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-", "").ToLowerInvariant(); }
        private static string Key(TradeCompletionReceipt r) => Hash(r.Endpoint.ToLowerInvariant()+"\n"+r.Recipient.ToLowerInvariant()+"\n"+r.TradeId.ToLowerInvariant()+"\n"+r.Completed);
        private static string Json(TradeCompletionReceipt r) => FrameworkSerialization.SerializePayload(r);
        private static string Fingerprint(TradeCompletionReceipt r) => Hash(Json(new TradeCompletionReceipt
        { Endpoint=r.Endpoint, Recipient=r.Recipient, TradeId=r.TradeId, Completed=r.Completed, OtherParty=r.OtherParty, Items=r.Items }));
        private TradeCompletionReceipt Read(string path)
        {
            if (new FileInfo(path).Length > MaxRecord) throw new InvalidDataException("Receipt exceeds budget.");
            Private(path,384);
            var r=FrameworkSerialization.DeserializePayload<TradeCompletionReceipt>(File.ReadAllText(path,Encoding.UTF8)); Validate(r);
            if (r.Fingerprint != Fingerprint(r)) throw new InvalidDataException("Receipt content hash mismatch.");
            if (Path.GetFileName(path)!=Key(r)+".receipt") throw new InvalidDataException("Receipt identity mismatch.");
            return r;
        }
        public TradeCompletionReceipt[] ReadAll()
        {
            lock(gate)
            {
                CheckEvidencePath(directory);
                var files=Directory.GetFiles(directory,"*.receipt");
                if (files.Length>MaxRecords || files.Sum(p=>new FileInfo(p).Length)>MaxTotal) throw new InvalidDataException("Inbox exceeds budget.");
                return files.OrderBy(p=>p,StringComparer.Ordinal).Select(Read).ToArray();
            }
        }

        internal TradeCompletionReceipt GetReceipt(string endpoint,string recipient,string trade,bool completed)
        {lock(gate)return FindReceipt(endpoint,recipient,trade,completed);}

        private TradeCompletionReceipt FindReceipt(string endpoint,string recipient,string trade,bool completed)
        {
            Identity(endpoint,512); Identity(recipient,128); Identity(trade,128);
            var lookup=new TradeCompletionReceipt { Endpoint=endpoint,Recipient=recipient,TradeId=trade,Completed=completed };
            var record=Read(Path.Combine(directory,Key(lookup)+".receipt"));
            if(record.Endpoint!=endpoint || record.Recipient!=recipient || record.TradeId!=trade || record.Completed!=completed)
                throw new InvalidDataException("Delivery scope differs from recorded receipt.");
            return record;
        }
        private static string BindingHash(TradeCompletionSaveBinding r) => Hash(FrameworkSerialization.SerializePayload(new TradeCompletionSaveBinding
        {ReceiptFingerprint=r.ReceiptFingerprint,SaveIdentity=r.SaveIdentity}));
        private TradeCompletionSaveBinding ReadBinding(string path,TradeCompletionReceipt receipt)
        {
            if(new FileInfo(path).Length>4096) throw new InvalidDataException("Save binding exceeds budget.");
            Private(path,384);
            var binding=FrameworkSerialization.DeserializePayload<TradeCompletionSaveBinding>(File.ReadAllText(path,Encoding.UTF8));
            if(binding==null || binding.Schema!=1 || binding.ReceiptFingerprint!=receipt.Fingerprint ||
                !IsSaveIdentity(binding.SaveIdentity) || binding.Fingerprint!=BindingHash(binding))
                throw new InvalidDataException("Invalid completion save binding.");
            return binding;
        }
        private static bool IsSaveIdentity(string id) => id!=null && id.Length==64 && id.All(c=>(c>='0'&&c<='9')||(c>='a'&&c<='f'));
        public void BindSave(string endpoint,string recipient,string trade,bool completed,string saveIdentity)
        {
            lock(gate)
            {
                if(!IsSaveIdentity(saveIdentity)) throw new InvalidDataException("Saved game identity required.");
                var receipt=FindReceipt(endpoint,recipient,trade,completed);
                string path=Path.Combine(directory,Key(receipt)+".binding");
                if(File.Exists(path))
                {
                    if(ReadBinding(path,receipt).SaveIdentity!=saveIdentity) throw new InvalidDataException("Completion belongs to another save.");
                    return;
                }
                // Historical attempts cannot be retrospectively assigned to whichever save is open.
                if(File.Exists(Path.ChangeExtension(path,".attempt"))) throw new InvalidDataException("Unbound historical attempt requires reconciliation.");
                var binding=new TradeCompletionSaveBinding {ReceiptFingerprint=receipt.Fingerprint,SaveIdentity=saveIdentity};
                binding.Fingerprint=BindingHash(binding);
                PublishBytes(path,Encoding.UTF8.GetBytes(FrameworkSerialization.SerializePayload(binding)));
            }
        }
        public string SaveBinding(string endpoint,string recipient,string trade,bool completed,string currentSave)
        {
            lock(gate)
            {
                var receipt=FindReceipt(endpoint,recipient,trade,completed);string path=Path.Combine(directory,Key(receipt)+".binding");
                if(!File.Exists(path)) return "Unbound";
                return ReadBinding(path,receipt).SaveIdentity==currentSave?"Current":"Different";
            }
        }
        private static string AttemptHash(TradeCompletionAttempt r) => Hash(FrameworkSerialization.SerializePayload(new TradeCompletionAttempt
        { ReceiptFingerprint=r.ReceiptFingerprint,Mode=r.Mode,State=r.State }));
        private TradeCompletionAttempt ReadAttempt(string path,TradeCompletionReceipt receipt,string mode,string state)
        {
            if(new FileInfo(path).Length>4096) throw new InvalidDataException("Delivery evidence exceeds budget.");
            Private(path,384);
            var r=FrameworkSerialization.DeserializePayload<TradeCompletionAttempt>(File.ReadAllText(path,Encoding.UTF8));
            if(r==null || r.Schema!=1 || r.ReceiptFingerprint!=receipt.Fingerprint ||
                (r.Mode!="Inventory" && r.Mode!="Direct") ||
                (r.State!="Attempted" && r.State!="ReturnedSuccess" && r.State!="ReturnedFailure") ||
                (state!=null && r.State!=state) || (state==null && r.State=="Attempted") ||
                (mode!=null && r.Mode!=mode) || r.Fingerprint!=AttemptHash(r))
                throw new InvalidDataException("Invalid delivery evidence.");
            return r;
        }
        private void PublishAttempt(string path,TradeCompletionAttempt record)
        {
            record.Fingerprint=AttemptHash(record);
            byte[] bytes=Encoding.UTF8.GetBytes(FrameworkSerialization.SerializePayload(record));
            PublishBytes(path,bytes);
        }
        private void PublishBytes(string path,byte[] bytes)
        {
            CheckEvidencePath(path);
            if(Directory.GetFiles(directory).Where(f=>!f.Contains(".tmp-")).Sum(f=>new FileInfo(f).Length)+bytes.Length>128L*1024*1024)throw new IOException("Completion evidence storage is full.");
            string temporary=path+".tmp-"+Guid.NewGuid().ToString("N");
            try
            {
                CheckEvidencePath(temporary);
                using(var stream=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None))
                {Private(temporary,384);stream.Write(bytes,0,bytes.Length);stream.Flush(true);}
                CheckEvidencePath(temporary);CheckEvidencePath(path);File.Move(temporary,path);
                Interlocked.Increment(ref revision);
            }
            finally {CheckEvidencePath(temporary);if(File.Exists(temporary))File.Delete(temporary);}
        }
        // An existing attempt means Unknown/reconciliation, never permission to invoke delivery again.
        public bool TryBeginDelivery(string endpoint,string recipient,string trade,bool completed,bool inventory)
        {
            lock(gate)
            {
                var receipt=FindReceipt(endpoint,recipient,trade,completed);
                string path=Path.Combine(directory,Key(receipt)+".attempt");
                if(File.Exists(path)) {ReadAttempt(path,receipt,null,"Attempted");return false;}
                PublishAttempt(path,new TradeCompletionAttempt {ReceiptFingerprint=receipt.Fingerprint,Mode=inventory?"Inventory":"Direct",State="Attempted"});
                return true;
            }
        }
        // A local API return is evidence only, not durable game-save confirmation or refund authority.
        public void RecordDeliveryReturn(string endpoint,string recipient,string trade,bool completed,bool inventory,bool succeeded)
        {
            lock(gate)
            {
                var receipt=FindReceipt(endpoint,recipient,trade,completed);
                string mode=inventory?"Inventory":"Direct",path=Path.Combine(directory,Key(receipt)+".result"),state=succeeded?"ReturnedSuccess":"ReturnedFailure";
                ReadAttempt(Path.ChangeExtension(path,".attempt"),receipt,mode,"Attempted");
                if(File.Exists(path)) {ReadAttempt(path,receipt,mode,state);return;}
                PublishAttempt(path,new TradeCompletionAttempt {ReceiptFingerprint=receipt.Fingerprint,Mode=mode,State=state});
            }
        }
        public string DeliveryState(string endpoint,string recipient,string trade,bool completed)
        {
            lock(gate)
            {
                var receipt=FindReceipt(endpoint,recipient,trade,completed);
                string path=Path.Combine(directory,Key(receipt)+".attempt");
                if(!File.Exists(path)) return "Received";
                var attempt=ReadAttempt(path,receipt,null,"Attempted");
                string result=Path.ChangeExtension(path,".result");
                return File.Exists(result)?ReadAttempt(result,receipt,attempt.Mode,null).State:attempt.State;
            }
        }
        // True publishes once to this live delivery owner; recorded duplicates require reconciliation.
        public bool Record(string endpoint,string recipient,string trade,bool completed,string other,IEnumerable<FrameworkItemPayload> items)
        {
            lock(gate)
            {
                CheckEvidencePath(directory);
                var record=new TradeCompletionReceipt { Endpoint=endpoint,Recipient=recipient,TradeId=trade,Completed=completed,OtherParty=other,Items=(items??Array.Empty<FrameworkItemPayload>()).ToArray() };
                Validate(record); record.Fingerprint=Fingerprint(record); string json=Json(record); byte[] bytes=Encoding.UTF8.GetBytes(json);
                if (bytes.Length>MaxRecord) throw new InvalidDataException("Receipt exceeds budget.");
                string target=Path.Combine(directory,Key(record)+".receipt");
                if (File.Exists(target))
                {
                    if (Json(Read(target))!=json) throw new InvalidDataException("Completion receipt conflicts with recorded batch.");
                    return false;
                }
                CheckEvidencePath(directory);
                var files=Directory.GetFiles(directory,"*.receipt");
                // Endpoint changes and contradictory terminal events do not grant a second delivery.
                if (files.Select(Read).Any(r => string.Equals(r.Recipient,recipient,StringComparison.OrdinalIgnoreCase) && string.Equals(r.TradeId,trade,StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidDataException("Receipt scope or terminal outcome changed; reconciliation required.");
                if (files.Length>=MaxRecords || files.Sum(p=>new FileInfo(p).Length)+bytes.Length>MaxTotal) throw new IOException("Completion inbox is full.");
                string temporary=target+".tmp-"+Guid.NewGuid().ToString("N");
                try
                {
                    CheckEvidencePath(temporary);
                    using(var stream=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None))
                    { Private(temporary,384); stream.Write(bytes,0,bytes.Length); stream.Flush(true); }
                    CheckEvidencePath(temporary);CheckEvidencePath(target);File.Move(temporary,target);
                    Interlocked.Increment(ref revision);
                    return true;
                }
                finally { CheckEvidencePath(temporary);if(File.Exists(temporary)) File.Delete(temporary); }
            }
        }
    }
}
