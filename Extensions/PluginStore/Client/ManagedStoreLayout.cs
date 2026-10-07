using UnityEngine;

namespace Phinix.PluginStore
{
    // Store-specific composition: narrow windows navigate between list and detail.
    internal struct ManagedStoreLayout
    {
        internal Rect List, Detail;
        internal bool Compact;
        internal static ManagedStoreLayout Calculate(Rect rect, bool showDetail = false)
        {
            float w = Mathf.Max(0, rect.width), h = Mathf.Max(0, rect.height);
            if (w >= 720)
            {
                float list = Mathf.Min(360, w * .32f);
                return new ManagedStoreLayout {
                    List = new Rect(rect.x, rect.y, list, h),
                    Detail = new Rect(rect.x + list + 12, rect.y, w - list - 12, h)
                };
            }
            var empty = new Rect(rect.x + w, rect.y + h, 0, 0);
            var full = new Rect(rect.x, rect.y, w, h);
            return new ManagedStoreLayout { Compact = true, List = showDetail ? empty : full, Detail = showDetail ? full : empty };
        }
        internal static ManagedStoreDetailLayout DetailRegions(Rect rect, bool compact, bool hasSelection)
        {
            float w = Mathf.Max(0, rect.width), h = Mathf.Max(0, rect.height);
            // Actions get space before prose. Even very short windows retain the first action.
            float actions = hasSelection ? Mathf.Min(112, h) : 0;
            float back = compact && hasSelection ? Mathf.Min(32, h - actions) : 0;
            float gap = Mathf.Min(8, Mathf.Max(0, h - actions - back));
            return new ManagedStoreDetailLayout {
                Back = new Rect(rect.x, rect.y, w, back),
                Content = new Rect(rect.x, rect.y + back, w, Mathf.Max(0, h - back - actions - gap)),
                Actions = new Rect(rect.x, rect.y + h - actions, w, actions)
            };
        }
    }
    internal struct ManagedStoreDetailLayout
    {
        internal Rect Back, Content, Actions;
    }
    internal struct ManagedStoreBadgeLayout
    {
        internal Rect Official, Route;
        internal bool OfficialText, RouteText;
        internal static ManagedStoreBadgeLayout Calculate(Rect rect, bool official, float officialLabelWidth, float routeLabelWidth)
        {
            float width=Mathf.Max(0,rect.width), height=Mathf.Max(0,rect.height);
            float icon=Mathf.Min(28,width), gap=official?Mathf.Min(6,Mathf.Max(0,width-2*icon)):0;
            float routeFull=Mathf.Max(28,routeLabelWidth+32), officialFull=Mathf.Max(28,officialLabelWidth+32);
            float officialWidth=official?Mathf.Min(icon,width/2):0;
            if(official && width>=officialFull+routeFull+gap) officialWidth=officialFull;
            float available=Mathf.Max(0,width-officialWidth-gap);
            float routeWidth=Mathf.Min(routeFull,available);
            return new ManagedStoreBadgeLayout {
                Official=new Rect(rect.x,rect.y,officialWidth,height),
                Route=new Rect(rect.x+officialWidth+gap,rect.y,routeWidth,height),
                OfficialText=official && officialWidth>=officialFull,
                RouteText=routeWidth>=routeFull
            };
        }
    }
}
