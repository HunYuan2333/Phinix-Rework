using System;
using System.Collections.Generic;
using System.Linq;
using PhinixClient.Framework;
using Utils.Framework;
namespace Phinix.TradeExtension.Client
{
    // Ordinary plugin presentation owner. One point per activation, independent sessions per window.
    internal sealed class TradeOfferPresentationFactory : IDisposable
    {
        internal readonly IPhinixExtensionModule Owner;
        internal readonly IClientPresentationService Renderer;
        internal readonly IClientExtensionPointService Points;
        internal readonly IClientTheme Theme;
        internal readonly IClientActionPoint<TradeOfferPresentationActionContext> Point;
        private readonly IClientExtensionPointScope scope;
        private readonly Dictionary<string,TradeWindow> windows=new Dictionary<string,TradeWindow>();
        internal TradeOfferPresentationFactory(IPhinixExtensionModule owner,IClientPresentationService renderer,IClientExtensionPointService points,IClientTheme theme)
        {
            Owner=owner;Renderer=renderer;Points=points;Theme=theme;scope=points.ForModule(owner);
            try {Point=scope.DefineActions<TradeOfferPresentationActionContext>(TradeOfferPresentationActionContext.PointId);
                scope.ContributeActions<TradeOfferPresentationActionContext>(TradeOfferPresentationActionContext.PointId,c=>windows.TryGetValue(c.PageId,out var w)?w.PresentationActions():Array.Empty<ClientActionDefinition>());}
            catch {scope.Dispose();throw;}
        }
        internal void Add(string id,TradeWindow window)=>windows.Add(id,window);
        internal void Remove(string id)=>windows.Remove(id);
        public void Dispose(){var failures=new List<Exception>();foreach(var w in windows.Values.ToArray())TradeLifetimeCleanup.Try(()=>w.Close(),failures);windows.Clear();TradeLifetimeCleanup.Try(()=>scope.Dispose(),failures);TradeLifetimeCleanup.ThrowIfFailed(failures);}
    }
}
