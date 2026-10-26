using System;
using System.Collections.Generic;
using System.Globalization;
using PhinixClient.Framework;

namespace PhinixClient
{
    internal sealed class ThemeDraftInput
    {internal bool IsFloat,Invalid,Dirty;internal string[] Values;}
    // Owner-side raw input validation only; applying/persisting themes remains catalog authority.
    internal static class ThemeDraftValues
    {
        internal static void Change(ClientThemeDraft draft,string key,ThemeDraftInput input,bool isFloat,float minimum,float maximum,int index,string raw)
        {
            if(index<0||index>=input.Values.Length)return;
            input.Values[index]=raw;input.Dirty=true;var values=new float[input.Values.Length];bool valid=input.IsFloat==isFloat;
            for(int i=0;i<values.Length;i++)valid&=float.TryParse(input.Values[i],NumberStyles.Float,CultureInfo.InvariantCulture,out values[i])&&!float.IsNaN(values[i])&&!float.IsInfinity(values[i])&&
                (isFloat?values[i]>=minimum&&values[i]<=maximum:values[i]>=0&&values[i]<=1);
            input.Invalid=!valid;if(!valid)return;
            draft.InheritedKeys.Remove(key);
            if(isFloat){draft.Colors.Remove(key);draft.Floats[key]=values[0];}
            else {draft.Floats.Remove(key);draft.Colors[key]=new ClientRgba(values[0],values[1],values[2],values[3]);}
        }
        internal static string Hex(ClientRgba color)
        {
            Func<float,int> channel=v=>(int)Math.Round(v*255,MidpointRounding.AwayFromZero);
            return "#"+channel(color.Red).ToString("X2")+channel(color.Green).ToString("X2")+channel(color.Blue).ToString("X2")+channel(color.Alpha).ToString("X2");
        }
        internal static bool TryHex(string text,float alpha,out ClientRgba color)
        {
            color=default(ClientRgba);string value=(text??"").Trim();if(value.StartsWith("#",StringComparison.Ordinal))value=value.Substring(1);
            if(value.Length!=6&&value.Length!=8)return false;
            var channels=new float[4];channels[3]=alpha;
            for(int i=0;i<value.Length/2;i++)
            {if(!byte.TryParse(value.Substring(i*2,2),NumberStyles.AllowHexSpecifier,CultureInfo.InvariantCulture,out var b))return false;channels[i]=b/255f;}
            color=new ClientRgba(channels[0],channels[1],channels[2],channels[3]);return true;
        }
        internal static bool TryPicker(string text,out ClientRgba color)
        {
            color=default(ClientRgba);var parts=(text??"").Split(',');if(parts.Length!=4)return false;var channels=new float[4];
            for(int i=0;i<4;i++)if(!float.TryParse(parts[i],NumberStyles.Float,CultureInfo.InvariantCulture,out channels[i])||float.IsNaN(channels[i])||float.IsInfinity(channels[i])||channels[i]<0||channels[i]>1)return false;
            color=new ClientRgba(channels[0],channels[1],channels[2],channels[3]);return true;
        }
        internal static void Clear(ClientThemeDraft draft,IDictionary<string,ThemeDraftInput> inputs,string key,bool inherit)
        {draft.Colors.Remove(key);draft.Floats.Remove(key);draft.InheritedKeys.Remove(key);if(inherit)draft.InheritedKeys.Add(key);inputs.Remove(key);}
        internal static void Reset(ClientThemeDraft draft,IDictionary<string,ThemeDraftInput> inputs)
        {draft.Colors.Clear();draft.Floats.Clear();draft.InheritedKeys.Clear();inputs.Clear();}
    }
}
