namespace PhinixClient.Framework
{
    public enum ClientThemePresentationSurface { Editor, Selector, Export }
    // Owner facts for the existing typed action registry, not theme authority or mutable draft access.
    public class ClientThemePresentationActionContext
    {
        public const string PointId = "builtin.host.theme.actions";
        public ClientThemePresentationActionContext(string pageId, long revision, ClientThemePresentationSurface surface,
            string themeId, string selectedKey, bool canApply)
        { PageId=pageId;Revision=revision;Surface=surface;ThemeId=themeId;SelectedKey=selectedKey;CanApply=canApply; }
        public string PageId { get; } public long Revision { get; }
        public ClientThemePresentationSurface Surface { get; }
        public string ThemeId { get; } public string SelectedKey { get; } public bool CanApply { get; }
    }
}
