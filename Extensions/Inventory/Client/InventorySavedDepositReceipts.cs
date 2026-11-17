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
        // SQLite is the durable inventory authority; a game save is no longer its checkpoint.
        public InventorySavedDepositReceipt ReadSavedDepositReceipt(InventoryDeposit deposit)
        {
            if (!ReadyWithoutPending(out _) || store == null || deposit == null ||
                ledger.CheckDeposit(deposit).Status != InventoryDepositStatus.AlreadyCommitted) return null;
            string fingerprint = store.DepositFingerprint(deposit);
            return fingerprint == null ? null : new InventorySavedDepositReceipt {
                SaveIdentity = GetStatus().SaveIdentity, JournalIdentity = store.Identity,
                Sequence = ledger.Sequence, CheckpointFingerprint = fingerprint };
        }
    }
}
