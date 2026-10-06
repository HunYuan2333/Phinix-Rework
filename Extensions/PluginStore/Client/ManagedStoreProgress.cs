using System;

namespace Phinix.PluginStore
{
    internal enum ManagedProgressStage { Checking, Downloading, Validating, Rechecking, Committing }

    internal sealed class ManagedPackageProgress
    {
        internal ManagedPackageProgress(ManagedProgressStage stage,long received)
        { Stage=stage; Received=received; }
        internal ManagedProgressStage Stage { get; }
        internal long Received { get; }
    }

    // Bytes describe transport only: a full bar does not mean validation or commit succeeded.
    internal sealed class ManagedStoreProgress
    {
        internal ManagedStoreProgress(ManagedProgressStage stage,ManagedStoreRecord package,long received,long total,int index,int count)
        {
            if(received<0 || total<0 || received>total || index<0 || count<index) throw new ArgumentOutOfRangeException(nameof(received));
            Stage=stage; Package=package; Received=received; Total=total; Index=index; Count=count;
        }
        internal ManagedProgressStage Stage { get; }
        internal ManagedStoreRecord Package { get; }
        internal long Received { get; }
        internal long Total { get; }
        internal int Index { get; }
        internal int Count { get; }
        internal float Fraction => Total==0?0:(float)((double)Received/Total);
    }
}
