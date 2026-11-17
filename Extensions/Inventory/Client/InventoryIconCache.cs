using System;
using System.Collections.Generic;
using PhinixClient.Framework;
using UnityEngine;
using Verse;

namespace Phinix.InventoryExtension.Client
{
    internal sealed class InventoryIconCache
    {
        private readonly Dictionary<string, ClientTextureImage> images = new Dictionary<string, ClientTextureImage>(StringComparer.Ordinal);
        internal IClientPresentationImage Get(string defName)
        {
            string key = string.IsNullOrWhiteSpace(defName) || defName.Length > 512 ? "" : defName;
            if (images.TryGetValue(key, out var image)) return image;
            Texture texture = null;
            if (key.Length > 0)
                try { texture = DefDatabase<ThingDef>.GetNamedSilentFail(key)?.uiIcon; }
                catch (System.Exception boundaryError30) { global::Phinix.InventoryExtension.InventoryDiagnostics.Report("InventoryIconCache/Get", boundaryError30);  /* Optional visual metadata cannot make an inventory record unusable. */ }
            if (texture == null)
            {
                try { texture = DefDatabase<ThingDef>.GetNamedSilentFail("UnknownItem")?.uiIcon; }
                catch (System.Exception boundaryError31) { global::Phinix.InventoryExtension.InventoryDiagnostics.Report("InventoryIconCache/Get", boundaryError31);  }
            }
            image = ClientTextureImage.FromTexture(texture ?? BaseContent.BadTex);
            images.Add(key, image);
            return image;
        }
        internal void Clear()
        {
            foreach (var image in images.Values) image?.Invalidate();
            images.Clear();
        }
    }
}
