// Only the engine/window boundary is stubbed. The editor projection, catalog, drafts,
// public presentation values and validation are the production sources.
using System;
using System.Collections.Generic;
using System.Linq;
using PhinixClient.Framework;
namespace UnityEngine {public static class GUIUtility {public static string systemCopyBuffer;}}
namespace Verse
{
 public static class UnityData {public static bool IsInMainThread=>true;}
 public sealed class Listing_Standard {public UnityEngine.Rect GetRect(float h)=>new UnityEngine.Rect();}
 public static class Find {public sealed class Stack {public void Add(object w){}}public static Stack WindowStack=new Stack();}
 public static class TextTranslation {public static string Translate(this string key)=>key;}
}
namespace PhinixClient.Framework
{
 public interface IClientSettingsContext {}
 public interface IClientSettingsPanelProvider {}
}
namespace PhinixClient
{
 internal sealed class ClientExtensionPointService {}
 internal sealed class ClientLifetimeService {}
 internal sealed class ThemePresentationServices : IDisposable
 {
  internal sealed class OwnerInfo {internal string ExtensionId=>"builtin.host";}
  internal OwnerInfo Owner=new OwnerInfo();internal bool Active=>true;
  internal ThemePresentationServices(IClientPresentationService r,ClientExtensionPointService p,ClientLifetimeService l){}
  public void Dispose(){}
 }
 internal static class UiScreenSafeArea {internal static UnityEngine.Rect Current=>new UnityEngine.Rect(0,0,1040,800);}
 internal static class LocalFileLocationAdapter {internal static bool TryOpenDirectory(string p)=>false;}
 internal abstract class ThemePresentationWindow
 {
  protected ThemePresentationServices Services; protected IClientTheme Theme;public bool IsOpen=>true;
  public ClientPresentationPage Page;public List<ClientActionDefinition> Definitions;
  private sealed class BoundAction : IClientContributedAction
  {
   private readonly ClientActionDefinition definition;internal BoundAction(ClientActionDefinition d){definition=d;}
   public string OwnerId=>"builtin.host";public string Id=>definition.Id;public string Label=>definition.Label;public string DisabledReason=>definition.DisabledReason;
   public ClientActionExecutionResult TryExecute(){throw new InvalidOperationException("UI probe does not execute through fake authority");}
  }
  protected ThemePresentationWindow(ThemePresentationServices s,IClientTheme t){Services=s;Theme=t;}
  public virtual UnityEngine.Vector2 InitialSize=>new UnityEngine.Vector2();
  protected virtual bool Available=>true;protected virtual bool DataChanged=>false;protected virtual Func<bool> DataGuard=>()=>true;
  protected abstract void Project();protected virtual void Input(ClientPresentationInput input){}
  protected void Invalidate(){} public void Close(){}public virtual void PostClose(){}
  protected static string L(string key)=>key;protected static string Safe(string s)=>s??"";protected static string Name(string s)=>s??"";
  protected void Publish(string title,ClientThemePresentationSurface surface,string themeId,string key,bool canApply,IEnumerable<ClientActionDefinition> definitions,Func<IReadOnlyList<ClientPresentationAction>,IEnumerable<ClientPresentationSection>> sections,ClientPresentationSplitView split=null,int fixedHeaders=0)
  {Definitions=definitions.ToList();Page=new ClientPresentationPage("test",title,1,sections(Definitions.Select(d=>new ClientPresentationAction("builtin.host."+d.Id,new BoundAction(d))).ToArray()));if(split!=null)Page=Page.WithSplitView(split);}
  public void Render()=>Project();public void Edit(string id,string value)=>Input(new ClientPresentationInput(1,id,ClientPresentationInputKind.TextChanged,value));
  public void Act(string id){Definitions.Single(a=>a.Id==id).Execute();Render();}
 }
 internal sealed class ThemePackageExportWindow
 {
  internal ThemePackageExportWindow(ThemePresentationServices s,IClientTheme t,IClientThemeCatalog c,IClientThemePackageExporter e,IClientSettingsContext settings,ClientThemeDraft d,string p,Func<bool> parent,Action<string> done){}
  internal bool IsOpen=>false;internal void Close(){}
 }
}
