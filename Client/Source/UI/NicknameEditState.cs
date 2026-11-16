using System;
using Connections;

namespace PhinixClient
{
    // Facts are captured on the game thread. Observing a name is not a correlated request acknowledgement.
    internal struct NicknameFacts : IEquatable<NicknameFacts>
    {
        internal readonly string Saved, Observed, Uuid;
        internal readonly bool Online;
        internal NicknameFacts(string saved, string observed, string uuid, bool online)
        { Saved = saved ?? ""; Observed = observed; Uuid = uuid ?? ""; Online = online; }
        internal string DisplayName => Online && Observed != null ? Observed : Saved;
        public bool Equals(NicknameFacts other) => Saved == other.Saved && Observed == other.Observed && Uuid == other.Uuid && Online == other.Online;
    }

    // UI draft only: no settings mutation until the explicit save action, no automatic resend.
    internal sealed class NicknameEditState
    {
        private readonly Func<NicknameFacts> capture;
        private readonly Action<string> persist;
        private readonly Func<string, bool> submit;
        private NicknameFacts baseline;
        private string draft;
        internal string Draft => draft;
        internal string Message { get; private set; } = "editing";
        internal bool Committed { get; private set; }
        internal bool Error => Message == "saveFailed" || Message == "sendUnknown";
        internal string Validation => draft.Length > 4096 ? "tooLong" : string.IsNullOrWhiteSpace(draft) || draft.IndexOfAny(new[] {'\r', '\n', '\0'}) >= 0 ? "invalid" : null;
        private readonly ClientConnectionDiagnostics faults;
        internal NicknameEditState(Func<NicknameFacts> capture, Action<string> persist, Func<string, bool> submit)
            : this(capture,persist,submit,null) { }
        internal NicknameEditState(Func<NicknameFacts> capture, Action<string> persist, Func<string, bool> submit, Action<string,Exception> report)
        { faults=new ClientConnectionDiagnostics(report);this.capture = capture; this.persist = persist; this.submit = submit; baseline = capture(); draft = baseline.DisplayName; }
        internal void Change(string value)
        { if (draft == value) return; draft = value ?? ""; Committed = false; Message = "editing"; }
        internal void Save()
        {
            if (Committed || Validation != null) return;
            var current = capture();
            if (!baseline.Equals(current)) { baseline = current; Message = "changed"; return; }
            try { persist(draft); }
            catch(Exception error) {
                faults.Report("NicknameSaveFailed",error);Message="saveFailed";
                try { baseline=capture(); } catch(Exception refresh) { faults.Report("NicknameRefreshFailed",refresh); }
                return;
            }
            // Mark before dispatch: even a throwing transport may already have sent the update.
            Committed = true;
            if (!current.Online) Message = "savedOffline";
            else
            {
                try { Message = submit(draft) ? "submitted" : "savedNotSent"; }
                catch(Exception error) { faults.Report("NicknameSendOutcomeUnknown",error);Message = "sendUnknown"; }
            }
            baseline = capture();
        }
        internal string Status(NicknameFacts current)
        {
            if (Committed && current.Online && (current.Uuid == baseline.Uuid || Message == "savedOffline") && current.Observed == draft)
                return "observed";
            if (Committed && Message == "savedOffline" && current.Online) return "savedDifferent";
            if (Committed && Message == "submitted" && (!current.Online || current.Uuid != baseline.Uuid)) return "disconnected";
            return Message;
        }
    }
}
