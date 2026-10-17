using System.Collections.Generic;
using System.Linq;
using PhinixClient.Framework;

namespace Phinix.ChatExtension.Client
{
    internal static class MemberSidebarPresentation
    {
        // Keep every contributed action. Only the owner's global filter is placed in the toolbar.
        internal static ClientPresentationPage Build(long revision, string owner, string status, string searchLabel,
            string filter, string memberName, string moreLabel, IEnumerable<ClientPresentationAction> actions,
            IEnumerable<ClientPresentationRow> rows, string empty, string selected)
        {
            var all = actions.ToArray();
            var tools = all.Where(a => a.Action.OwnerId == owner && a.Action.Id == "collapse-blocked").ToArray();
            var selectedActions = all.Except(tools).ToArray();
            var extras = selectedActions.Where(a => a.Action.OwnerId != owner).ToArray();
            var sections = new List<ClientPresentationSection> {
                new ClientPresentationSection("member-tools", "", "", tools,
                    fields: new[] { new ClientPresentationTextField("member-filter", "", filter, searchLabel) })
                    .WithInlineToolbar()
            };
            if (selectedActions.Length > 0)
                sections.Add(new ClientPresentationSection("member-actions", memberName ?? "", "", selectedActions)
                    .WithWrappedActions().WithActionMenus(extras.Length == 0 ? null : new[] {
                        new ClientPresentationActionMenu("member-menu", moreLabel, extras.Select(a => a.Id)) }));
            int headers = sections.Count;
            sections.Add(new ClientPresentationSection("member-rows", status, "", list:
                new ClientPresentationList("members-list", rows, empty, selected).WithCompactRows()));
            return new ClientPresentationPage("members", "", revision, sections).WithFixedHeaderSections(headers);
        }
    }
}
