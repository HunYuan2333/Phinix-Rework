using System;
using System.Collections.Generic;
using System.Linq;
using PhinixClient.Framework;
using UnityEngine;
using Verse;
internal static partial class Program
{
    private static void CheckButtonStateContrast()
    {
        var colors=new Dictionary<string,ClientRgba>{
            ["ui.background"]=new ClientRgba(.08f,.08f,.07f),["ui.surface"]=new ClientRgba(.13f,.125f,.11f),
            ["ui.text.primary"]=new ClientRgba(.93f,.91f,.86f),["ui.text.secondary"]=new ClientRgba(.73f,.7f,.65f),
            ["ui.separator"]=new ClientRgba(.49f,.46f,.4f),["ui.error"]=new ClientRgba(.98f,.45f,.38f),
            ["ui.accent"]=new ClientRgba(.94f,.66f,.38f),["ui.action.primary"]=new ClientRgba(.94f,.66f,.38f),
            ["ui.button.background"]=new ClientRgba(.223529f,.207843f,.176471f),["ui.button.hover"]=new ClientRgba(.286275f,.266667f,.227451f),
            ["ui.button.pressed"]=new ClientRgba(.223529f,.207843f,.176471f),["ui.control.disabled"]=new ClientRgba(.13f,.125f,.11f)};
        Func<ClientThemeSnapshot> palette=()=>new ClientThemeSnapshot(1,colors,new Dictionary<string,float>());
        var rect=new Rect(0,0,160,32);
        try
        {
            Mouse.Over=true;Input.MouseDown=true;GUI.SurfaceColors.Clear();Widgets.LabelColors.Clear();
            ClientWidgets.Button(rect,"Pressed",palette());
            Check(Widgets.LabelColors.Last().r==.93f && GUI.SurfaceColors[0].r==.223529f,"Pressed normal button retains readable body text on dark fill");
            Check(GUI.SurfaceColors.Any(c=>c.r==.94f),"Pressed state adds an accent outline, not just a new text color");
            colors["ui.button.pressed"]=new ClientRgba(.85f,.85f,.85f);
            Widgets.LabelColors.Clear();ClientWidgets.Button(rect,"Bright custom press",palette());
            Check(Widgets.LabelColors.Last().r==0,"Explicit bright custom pressed fill remains usable through contrasting text");
            Input.MouseDown=false;Widgets.LabelColors.Clear();GUI.SurfaceColors.Clear();
            ClientWidgets.Button(rect,"Remove",palette(),ClientButtonKind.Danger);
            Check(Widgets.LabelColors.Last().r==.93f&&GUI.SurfaceColors.Any(c=>c.r==.98f),"Danger uses readable text plus its error outline");
            colors["ui.text.primary"]=new ClientRgba(.93f,.91f,.86f,.8f);
            Widgets.LabelColors.Clear();ClientWidgets.Button(rect,"Readable alpha",palette());
            Check(Widgets.LabelColors.Last().a==.8f,"Readable custom text alpha is preserved");
            colors["ui.text.primary"]=new ClientRgba(.93f,.91f,.86f);
            colors["ui.background"]=new ClientRgba(1,1,1);colors["ui.action.primary"]=new ClientRgba(.8f,.8f,.8f);
            Widgets.LabelColors.Clear();ClientWidgets.Button(rect,"Primary",palette(),ClientButtonKind.Primary);
            Check(Widgets.LabelColors.Last().r==0,"Light custom palette gets dark primary text instead of white on light fill");
            colors["ui.action.primary"]=new ClientRgba(.03f,.03f,.03f);
            Widgets.LabelColors.Clear();ClientWidgets.IconButton(rect,"Primary",Texture2D.whiteTexture,new Vector2(16,16),palette(),ClientButtonKind.Primary);
            Check(Widgets.LabelColors.Last().r==1,"Icon primary uses the same contrast rule on dark custom fill");
            Widgets.LabelColors.Clear();ClientWidgets.Button(rect,"Custom",palette(),ClientButtonKind.Primary,textColor:new Color(.3f,.4f,.5f));
            Check(Widgets.LabelColors.Last().r==.3f,"An explicit caller text color remains authoritative");
            Widgets.LabelColors.Clear();ClientWidgets.Button(rect,"Disabled",palette(),ClientButtonKind.Danger,enabled:false);
            Check(Widgets.LabelColors.Last().r==.73f&&GUI.enabled,"Disabled text remains secondary and GUI state is restored");
        }
        finally{Mouse.Over=false;Input.MouseDown=false;GUI.SurfaceColors.Clear();Widgets.LabelColors.Clear();}
    }
}
