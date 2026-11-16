using System;

namespace PhinixClient.Framework
{
    // No business dispatcher or plugin scope: notification survives a full queue and failed activation.
    internal sealed class ClientErrorNotificationController
    {
        private readonly ClientErrorService errors;
        private readonly Func<bool> ready, isOpen;
        private readonly Action open;
        internal ClientErrorNotificationController(ClientErrorService errors, Func<bool> ready, Func<bool> isOpen, Action open)
        { this.errors = errors; this.ready = ready; this.isOpen = isOpen; this.open = open; }
        internal void Pump()
        {
            if (errors.HasPending) Show();
        }
        internal void Show()
        {
            long revision = errors.Revision;
            try
            {
                if (!ready()) return;
                if (!isOpen()) open();
                errors.AcknowledgeNotifications(revision);
            }
            catch (Exception error)
            {
                errors.ReportHost(error, "ErrorWindowFailed", "OpenErrorWindow", false);
                errors.AcknowledgeNotifications(errors.Revision);
            }
        }
        internal bool Copy(string id, Action<string> copy)
        {
            try
            {
                string report = errors.BuildReport(id);
                if (report == null) return false;
                copy(report);
                return true;
            }
            catch (Exception error)
            {
                errors.ReportHost(error, "ErrorCopyFailed", "CopyErrorDetails", false);
                return false;
            }
        }
    }
}
