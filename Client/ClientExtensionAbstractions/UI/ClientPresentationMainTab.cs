using System;
using UnityEngine;
namespace PhinixClient.Framework
{
    // Public provider adapter. Delegates remain plugin-owned; rendering still uses the same presentation service.
    // Also usable by an older binary through an explicit runtime-binding adapter.
    public sealed class ClientPresentationMainTab : IMainTabProvider,IResponsiveMainTabProvider,IClientNavigationMetadata,IClientViewLifecycle
    {
        private readonly IMainTabProvider provider;
        private readonly Action opened,update,closed;
        private bool visible;
        public ClientPresentationMainTab(IMainTabProvider provider,Action viewOpened,Action viewUpdate,Action viewClosed,string navigationId)
        {
            this.provider=provider??throw new ArgumentNullException(nameof(provider));
            opened=viewOpened??throw new ArgumentNullException(nameof(viewOpened));
            update=viewUpdate??throw new ArgumentNullException(nameof(viewUpdate));
            closed=viewClosed??throw new ArgumentNullException(nameof(viewClosed));
            NavigationId=ClientPresentationLimits.Id(navigationId);
        }
        public string TabLabel=>provider.TabLabel;
        public float TabOrder=>provider.TabOrder;
        public string NavigationId {get;}
        public UiLayoutHints LayoutHints=>(provider as IResponsiveMainTabProvider)?.LayoutHints??UiLayoutHints.Default;
        public void ViewOpened(){if(visible)return;try {opened();visible=true;}catch {closed();throw;}}
        public void ViewUpdate(){if(visible)update();}
        public void ViewClosed(){if(!visible)return;visible=false;closed();}
        public void Draw(Rect rect)=>provider.Draw(rect);
    }
}
