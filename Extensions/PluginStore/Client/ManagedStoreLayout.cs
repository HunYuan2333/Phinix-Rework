using UnityEngine;

namespace Phinix.PluginStore
{
    internal struct ManagedStoreLayout
    {
        internal Rect List,Detail;
        internal static ManagedStoreLayout Calculate(Rect rect)
        {
            float w=Mathf.Max(0,rect.width),h=Mathf.Max(0,rect.height);
            if(w>=620) { float list=Mathf.Min(320,w*.38f); return new ManagedStoreLayout {List=new Rect(rect.x,rect.y,list,h),Detail=new Rect(rect.x+list+12,rect.y,Mathf.Max(0,w-list-12),h)}; }
            float top=Mathf.Min(180,h*.38f);
            float gap=Mathf.Min(8,Mathf.Max(0,h-top));
            return new ManagedStoreLayout {List=new Rect(rect.x,rect.y,w,top),Detail=new Rect(rect.x,rect.y+top+gap,w,Mathf.Max(0,h-top-gap))};
        }
    }
}
