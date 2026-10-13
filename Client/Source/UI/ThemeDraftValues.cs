using System;
using System.Collections.Generic;
using System.Globalization;
using PhinixClient.Framework;
using UnityEngine;

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
            else {draft.Floats.Remove(key);draft.Colors[key]=new Color(values[0],values[1],values[2],values[3]);}
        }
        internal static void Clear(ClientThemeDraft draft,IDictionary<string,ThemeDraftInput> inputs,string key,bool inherit)
        {draft.Colors.Remove(key);draft.Floats.Remove(key);draft.InheritedKeys.Remove(key);if(inherit)draft.InheritedKeys.Add(key);inputs.Remove(key);}
        internal static void Reset(ClientThemeDraft draft,IDictionary<string,ThemeDraftInput> inputs)
        {draft.Colors.Clear();draft.Floats.Clear();draft.InheritedKeys.Clear();inputs.Clear();}
    }
}
