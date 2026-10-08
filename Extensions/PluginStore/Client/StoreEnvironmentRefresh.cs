using System;
using System.Threading;
using System.Threading.Tasks;
using PhinixClient.Framework;

namespace Phinix.PluginStore
{
    internal static class StoreEnvironmentRefresh
    {
        internal static async Task<ClientEnvironmentSnapshot> Capture(Func<ClientEnvironmentSnapshot> capture,
            Action<Action> enqueue,CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var completion=new TaskCompletionSource<ClientEnvironmentSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
            using(token.Register(()=>completion.TrySetCanceled()))
            {
                try
                {
                    enqueue(()=>
                    {
                        if(token.IsCancellationRequested) { completion.TrySetCanceled(); return; }
                        try { completion.TrySetResult(capture()); }
                        catch(Exception ex) { completion.TrySetException(ex); }
                    });
                }
                catch(Exception ex) { completion.TrySetException(ex); }
                return await completion.Task.ConfigureAwait(false);
            }
        }
    }
}
