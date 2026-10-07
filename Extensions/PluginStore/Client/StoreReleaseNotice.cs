using System;

namespace Phinix.PluginStore
{
    internal sealed class StoreReleaseNotice
    {
        internal const string SettingsKey="plugin-store.notice.optional-plugins-20261007";
        private readonly Func<bool> wasShown;
        private readonly Action markShown;
        private readonly Action<Action> defer;
        private readonly Action show;
        private readonly Action<string> diagnostic;
        private bool active;
        private int generation;

        internal StoreReleaseNotice(Func<bool> wasShown,Action markShown,Action<Action> defer,Action show,Action<string> diagnostic)
        {
            this.wasShown=wasShown??throw new ArgumentNullException(nameof(wasShown));
            this.markShown=markShown??throw new ArgumentNullException(nameof(markShown));
            this.defer=defer??throw new ArgumentNullException(nameof(defer));
            this.show=show??throw new ArgumentNullException(nameof(show));
            this.diagnostic=diagnostic;
        }
        internal void Start()
        {
            if(active) return;
            active=true;
            int expected=++generation;
            try
            {
                if(wasShown()) return;
                defer(()=> {
                    if(!active || expected!=generation) return;
                    try
                    {
                        if(wasShown()) return;
                        show();
                        // Persist only after the main-thread window request succeeds.
                        markShown();
                    }
                    catch(Exception ex) { Report(ex); }
                });
            }
            catch(Exception ex) { Report(ex); }
        }
        internal void Stop() { active=false; generation++; }
        private void Report(Exception exception)
        { try { diagnostic?.Invoke("StoreReleaseNoticeFailed:"+exception.GetType().Name); } catch { } }
    }
}
