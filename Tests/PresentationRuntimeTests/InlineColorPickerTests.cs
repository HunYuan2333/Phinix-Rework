using System;
using System.Linq;
using System.Globalization;
using PhinixClient;
using PhinixClient.Framework;
using UnityEngine;
using Verse;
internal static partial class Program
{
    private static void CheckInlineColorPicker()
    {
        Reject(()=>new ClientPresentationColorPicker(new ClientRgba(float.NaN,0,0),"Brightness"),"Picker rejects non-finite values");
        var descriptor=new ClientPresentationColorPicker(new ClientRgba(.4f,0,0,.25f),"Brightness");
        var field=new ClientPresentationTextField("color","Pick a color","").WithColorPicker(descriptor);
        Reject(()=>field.WithSlider(new ClientPresentationSlider(0,1,.2f)),"Picker and numeric slider cannot occupy the same field");
        Reject(()=>new ClientPresentationTextField("a","a","").WithSlider(new ClientPresentationSlider(0,1,.2f)).WithColorPicker(descriptor),"Slider cannot silently turn into a picker");
        Reject(()=>new ClientPresentationRow("a","a").WithControls(null,field),"Row controls reject unsupported picker layout");
        var service=new ClientPresentationService(new Lifetime(),new Theme(),()=>true,_=>{});
        using(var session=service.Open(new Module(),"inline-color"))
        {
            long revision=0;bool current=true;
            Action<ClientPresentationTextField> publish=f=>session.Publish(new ClientPresentationPage("inline-color","",++revision,new[]{new ClientPresentationSection("color-section","","",fields:new[]{f})}),null,()=>current);
            Action draw=()=>session.Draw(new ClientPresentationBounds(0,0,620,900));
            publish(field);Event.current.type=EventType.Repaint;int windows=Find.WindowStack.Last==null?0:1;Widgets.WheelCalls=0;Widgets.ColorBoxCalls=0;Widgets.ColorBoxRects.Clear();draw();
            Check(Widgets.WheelCalls==1&&Widgets.ColorBoxCalls==60,"Shared renderer calls native wheel and sixty embedded preset cells");
            Check((Find.WindowStack.Last==null?0:1)==windows,"Embedded picker creates no window");
            Check(!session.TryDequeueInput(out _),"Painting the picker does not emit an edit");
            Widgets.WheelValue=new Color(0,1,0);Widgets.WheelConsumesEvent=true;Event.current.type=EventType.MouseDown;draw();
            Check(session.TryDequeueInput(out var input)&&input.Id=="color","Native wheel emits an ordinary draft input even after consuming the event");
            var channels=input.Value.Split(',').Select(v=>float.Parse(v,CultureInfo.InvariantCulture)).ToArray();
            Check(Math.Abs(channels[1]-.4f)<.001&&channels[3]==.25f,"Wheel changes hue while preserving dark brightness and opacity");
            var next=new ClientPresentationTextField("color","Pick a color","").WithColorPicker(new ClientPresentationColorPicker(new ClientRgba(channels[0],channels[1],channels[2],channels[3]),"Brightness"));
            publish(next);Event.current.type=EventType.MouseDown;Widgets.SliderValue=.2f;draw();
            Check(Widgets.IncomingWheelDrags.Last(),"Wheel drag ownership survives publication of the next draft revision");
            Check(session.TryDequeueInput(out input)&&input.Value.EndsWith(",0.25",StringComparison.Ordinal),"Brightness emits a draft without changing opacity");
            publish(next);Widgets.ColorBoxCalls=0;Widgets.PresetPick=9;Event.current.type=EventType.MouseDown;draw();
            Check(session.TryDequeueInput(out input)&&input.Value=="1,1,1,0.25","Native preset cells select white and preserve transparency");
            var disabled=new ClientPresentationTextField("color","Pick a color","",disabledReason:"Unavailable").WithColorPicker(descriptor);
            publish(disabled);Widgets.WheelValue=new Color(0,0,1);Event.current.type=EventType.MouseDown;draw();
            Check(!session.TryDequeueInput(out _)&&GUI.enabled,"Disabled picker cannot emit edits and restores GUI state");
            publish(field);current=false;Event.current.type=EventType.MouseDown;Widgets.WheelValue=new Color(0,0,1);draw();
            Check(!session.TryDequeueInput(out _),"Stale picker context cannot emit an accepted edit");current=true;
            publish(field);Event.current.type=EventType.Repaint;Widgets.ColorBoxRects.Clear();session.Draw(new ClientPresentationBounds(0,0,220,1000));
            Check(Widgets.ColorBoxRects.Count==60&&Widgets.ColorBoxRects.All(r=>r.x>=0&&r.xMax<=220),"Narrow picker wraps all preset cells inside the available width");
            Widgets.WheelConsumesEvent=false;Widgets.WheelValue=null;Widgets.PresetPick=-1;Event.current.type=EventType.Layout;
        }
    }
}
