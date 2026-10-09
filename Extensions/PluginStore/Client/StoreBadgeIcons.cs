using System;
using System.Collections.Generic;
using PhinixClient.Framework;
using UnityEngine;
using Verse;

namespace Phinix.PluginStore
{
    internal enum StoreBadgeKind { Official, Managed, Workshop, Local }

    internal sealed class StoreBadgeIcons : IDisposable
    {
        private readonly IClientMainThreadDispatcher dispatcher;
        private readonly Action<string> diagnostic;
        private readonly Dictionary<StoreBadgeKind,Texture2D> textures=new Dictionary<StoreBadgeKind,Texture2D>();
        private bool disposed;
        public StoreBadgeIcons(IClientMainThreadDispatcher dispatcher,StoreActivationDiagnostics diagnostics)
            : this(dispatcher,diagnostics.Badge) { }
        internal StoreBadgeIcons(IClientMainThreadDispatcher dispatcher,Action<string> diagnostic)
        { this.dispatcher=dispatcher??throw new ArgumentNullException(nameof(dispatcher)); this.diagnostic=diagnostic; }

        internal Texture2D Get(StoreBadgeKind kind)
        {
            if(disposed) return null;
            Texture2D texture;
            if(textures.TryGetValue(kind,out texture)) return texture;
            if(!UnityData.IsInMainThread) throw new InvalidOperationException("Create badge textures on the main thread.");
            texture=null;
            try
            {
                using(var stream=typeof(StoreBadgeIcons).Assembly.GetManifestResourceStream("Phinix.PluginStore.Assets.Badges."+kind.ToString().ToLowerInvariant()+".png"))
                {
                    if(stream==null || stream.Length>65536) throw new InvalidOperationException("Missing or oversized badge resource.");
                    byte[] bytes=new byte[(int)stream.Length]; int offset=0;
                    while(offset<bytes.Length) { int count=stream.Read(bytes,offset,bytes.Length-offset); if(count==0) throw new InvalidOperationException("Incomplete badge resource."); offset+=count; }
                    texture=new Texture2D(2,2,TextureFormat.RGBA32,false);
                    if(!ImageConversion.LoadImage(texture,bytes,true)) throw new InvalidOperationException("Invalid badge resource.");
                    texture.name="Phinix.Store.Badge."+kind; texture.filterMode=FilterMode.Bilinear;
                }
            }
            catch(Exception ex)
            {
                if(texture!=null) UnityEngine.Object.Destroy(texture);
                texture=null;
                try { diagnostic?.Invoke(kind+":"+ex.GetType().Name); } catch { }
            }
            textures.Add(kind,texture); return texture;
        }
        public void Dispose()
        {
            if(disposed) return;
            disposed=true;
            var owned=new List<Texture2D>(textures.Values); textures.Clear();
            if(owned.Count==0) return;
            Action release=()=> { foreach(var texture in owned) if(texture!=null) UnityEngine.Object.Destroy(texture); };
            if(UnityData.IsInMainThread) release(); else dispatcher.Enqueue(release);
        }
    }
}
