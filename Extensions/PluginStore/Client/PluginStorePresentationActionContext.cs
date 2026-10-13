using System.Collections.Generic;
using System.Linq;
using PhinixClient.Framework;

namespace Phinix.PluginStore
{
    public enum PluginStorePresentationSurface { Store, LocalFile, LocalThemes }

    public sealed class PluginStorePresentationActionContext
    {
        public const string PointId = "phinix.plugin-store.actions";
        internal PluginStorePresentationActionContext(string pageId, long revision, string selectedKey, string state, string selectedPackageId, string selectedVersion, bool busy, bool inventoryKnown, IEnumerable<ClientActionDefinition> actions, PluginStorePresentationSurface surface = PluginStorePresentationSurface.Store)
        { Surface = surface; PageId = pageId; Revision = revision; SelectedKey = selectedKey; State = state; SelectedPackageId = selectedPackageId; SelectedVersion = selectedVersion; Busy = busy; InventoryKnown = inventoryKnown; OwnerActions = actions.ToList().AsReadOnly(); }
        public PluginStorePresentationSurface Surface { get; }
        public string PageId { get; }
        public long Revision { get; }
        public string SelectedKey { get; }
        public string State { get; }
        public string SelectedPackageId { get; }
        public string SelectedVersion { get; }
        public bool Busy { get; }
        public bool InventoryKnown { get; }
        internal IEnumerable<ClientActionDefinition> OwnerActions { get; }
    }

}
