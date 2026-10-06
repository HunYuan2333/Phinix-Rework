using System;
using UnityEngine;

namespace PhinixClient.Framework
{
    internal struct ManagedExtensionManagerCardLayout
    {
        internal Rect Title, State, Modules, ModuleButton, Diagnostic, Toggle, Removal;
    }
    internal static class ManagedExtensionManagerLayout
    {
        internal const float CardHeight=148f;
        internal static ManagedExtensionManagerCardLayout Card(Rect rect)
        {
            float padding=Math.Min(6f,Math.Max(0f,rect.width)/2f);
            float x=rect.x+padding, width=Math.Max(0f,rect.width-padding*2f), y=rect.y+Math.Min(6f,Math.Max(0f,rect.height));
            var result=new ManagedExtensionManagerCardLayout
            {
                Title=Row(x,y,width,24f,rect.yMax), State=Row(x,y+25f,width,22f,rect.yMax),
                Modules=Row(x,y+48f,width,18f,rect.yMax), Diagnostic=Row(x,y+69f,width,20f,rect.yMax)
            };
            float gap=Math.Min(6f,width), button=Math.Max(0f,(width-gap)/2f);
            float moduleButton=Math.Min(68f,width);
            result.Modules.width=Math.Max(0f,width-moduleButton-gap);
            result.ModuleButton=Row(x+width-moduleButton,y+48f,moduleButton,20f,rect.yMax);
            result.Toggle=Row(x,y+100f,button,30f,rect.yMax);
            result.Removal=Row(x+button+gap,y+100f,button,30f,rect.yMax);
            return result;
        }
        private static Rect Row(float x,float y,float width,float height,float bottom)
        { return new Rect(x,Math.Min(y,bottom),width,Math.Max(0f,Math.Min(height,bottom-y))); }
    }
}
