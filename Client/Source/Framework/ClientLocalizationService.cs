using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Runtime.CompilerServices;
using System.Text;
using Utils.Framework;
using Utils.Framework.ManagedExtensions;

namespace PhinixClient.Framework
{
    // Game-independent service. The host publishes game language on its main thread.
    internal sealed class ClientLocalizationService : IClientLocalizationService, IExtensionModuleLifecycleObserver, IDisposable
    {
        private readonly HashSet<Type> registered;
        private readonly Func<Type,ExtensionLocalizationCatalog> load;
        private readonly Func<bool> isMainThread;
        private readonly Action<string,string,string,string> audit;
        private readonly Dictionary<IPhinixExtensionModule,Localizer> localizers=new Dictionary<IPhinixExtensionModule,Localizer>(ModuleIdentity.Instance);
        private readonly HashSet<IPhinixExtensionModule> activating=new HashSet<IPhinixExtensionModule>(ModuleIdentity.Instance);
        private readonly object sync=new object();
        private string locale;
        private bool disposed;
        internal ClientLocalizationService(IEnumerable<Type> registered,Func<Type,ExtensionLocalizationCatalog> load,Func<bool> isMainThread,Action<string,string,string,string> audit)
        { this.registered=new HashSet<Type>(registered); this.load=load; this.isMainThread=isMainThread; this.audit=audit; }
        public void OnActivating(IPhinixExtensionModule module)
        {
            MainThread();
            if(module==null || !registered.Contains(module.GetType()) ||
                !string.Equals(module.GetType().GetCustomAttributes(typeof(PhinixExtensionAttribute),true).Cast<PhinixExtensionAttribute>().SingleOrDefault()?.ExtensionId??module.GetType().Name,module.ExtensionId,StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("LocalizationModuleNotRegistered");
            lock(sync) { if(disposed) throw new ObjectDisposedException(nameof(ClientLocalizationService)); activating.Add(module); }
        }
        public IClientLocalizer ForModule(IPhinixExtensionModule module)
        {
            MainThread();
            lock(sync)
            {
                if(disposed || module==null || !activating.Contains(module)) throw new InvalidOperationException("LocalizationModuleNotActive");
                Localizer result;
                if(localizers.TryGetValue(module,out result) && !result.Closed) return result;
                try
                {
                    var catalog=load(module.GetType())??ExtensionLocalizationCatalog.Empty;
                    result=new Localizer(this,module.ExtensionId,catalog,locale); localizers[module]=result;
                    Report(module.ExtensionId,"LocalizationBound"); return result;
                }
                catch(ManagedExtensionValidationException error) { Report(module.ExtensionId,error.Code,error.ResourcePath); throw; }
                catch(Exception) { Report(module.ExtensionId,"LocalizationBindingFailed"); throw; }
            }
        }
        public void OnStopped(IPhinixExtensionModule module)
        {
            lock(sync)
            {
                Localizer value; if(module!=null && localizers.TryGetValue(module,out value)) { value.Dispose(); localizers.Remove(module); }
                if(module!=null) activating.Remove(module);
            }
        }
        internal void UpdateLanguage(string value)
        {
            MainThread(); string normalized=null;
            if(value!=null) try { normalized=ExtensionLocale.Normalize(value); } catch(ManagedExtensionValidationException) { Report("host","GameLocaleUnavailable"); }
            Localizer[] listeners;
            lock(sync) { if(disposed || locale==normalized) return; locale=normalized; listeners=localizers.Values.ToArray(); }
            foreach(var listener in listeners) listener.SetLocale(normalized);
            Report("host","LocalizationLanguageChanged");
        }
        private void MainThread() { if(!isMainThread()) throw new InvalidOperationException("Localization bindings and language updates require the main thread."); }
        private void Report(string module,string code,string resourcePath=null,string key=null) { try { audit?.Invoke(module,code,resourcePath,key); } catch { } }
        public void Dispose()
        {
            lock(sync) { if(disposed) return; disposed=true; foreach(var value in localizers.Values) value.Dispose(); localizers.Clear(); activating.Clear(); registered.Clear(); }
        }
        private sealed class ModuleIdentity : IEqualityComparer<IPhinixExtensionModule>
        {
            internal static readonly ModuleIdentity Instance=new ModuleIdentity();
            public bool Equals(IPhinixExtensionModule a,IPhinixExtensionModule b) { return ReferenceEquals(a,b); }
            public int GetHashCode(IPhinixExtensionModule module) { return RuntimeHelpers.GetHashCode(module); }
        }
        private static long auditSequence;
        internal static string AuditJson(string module,string code,string resourcePath=null,string key=null)
        {
            return "{\"schemaVersion\":1,\"event\":\"extension_localization\",\"time\":"+Quote(DateTime.UtcNow.ToString("o",CultureInfo.InvariantCulture))+",\"sequence\":"+Interlocked.Increment(ref auditSequence).ToString(CultureInfo.InvariantCulture)+",\"moduleId\":"+Quote(module)+",\"code\":"+Quote(code)+",\"resourcePath\":"+Quote(resourcePath)+",\"key\":"+Quote(key)+"}";
        }
        private static string Quote(string text)
        {
            if(text==null) return "null";
            var value=new StringBuilder("\"");
            foreach(char c in text)
                if(c=='"' || c=='\\') value.Append('\\').Append(c);
                else if(c<32 || char.IsSurrogate(c)) value.Append("\\u").Append(((int)c).ToString("x4",CultureInfo.InvariantCulture));
                else value.Append(c);
            return value.Append('"').ToString();
        }
        private sealed class Localizer : IClientLocalizer
        {
            private readonly ClientLocalizationService owner;
            private readonly string module;
            private readonly object gate=new object();
            private ExtensionLocalizationCatalog catalog;
            private readonly HashSet<string> reported=new HashSet<string>(StringComparer.Ordinal);
            private string locale;
            private Action languageChanged;
            internal Localizer(ClientLocalizationService owner,string module,ExtensionLocalizationCatalog catalog,string locale)
            { this.owner=owner; this.module=module; this.catalog=catalog; this.locale=locale; }
            internal bool Closed { get { lock(gate) return catalog==null; } }
            public string Locale { get { lock(gate) return locale; } }
            public event Action LanguageChanged
            { add { lock(gate) { if(catalog!=null) languageChanged+=value; } } remove { lock(gate) languageChanged-=value; } }
            public string Text(string key,string fallback=null)
            {
                lock(gate)
                {
                    if(catalog==null) return fallback??key??"?";
                    string result=ExtensionLanguageFile.ValidKey(key)?catalog.Resolve(key,locale):null;
                    if(result!=null) return result;
                    Missing(key,"LocalizationKeyMissing"); return fallback??key??"?";
                }
            }
            public string Format(string key,params object[] arguments)
            {
                string text=Text(key); string activeLocale=Locale;
                try
                {
                    if(arguments==null || arguments.Length>32) throw new FormatException();
                    var indexes=ExtensionLanguageFile.Parameters(text);
                    if(indexes.Any(i=>i>=arguments.Length)) throw new FormatException();
                    IFormatProvider culture=CultureInfo.InvariantCulture;
                    if(activeLocale!=null) try { culture=CultureInfo.GetCultureInfo(activeLocale); } catch(CultureNotFoundException) { }
                    // Convert each parameter once, with a bound before interpolating repeated placeholders.
                    var values=arguments.Select(a=>a==null?"":a is IFormattable?((IFormattable)a).ToString(null,culture):a.ToString()).ToArray();
                    if(values.Any(v=>v!=null && v.Length>8192)) throw new FormatException();
                    long estimated=text.Length;
                    for(int i=0;i<text.Length;i++) if(text[i]=='{')
                    {
                        if(i+1<text.Length && text[i+1]=='{') { i++; continue; }
                        int start=++i; while(text[i]!='}') i++;
                        int index=int.Parse(text.Substring(start,i-start),CultureInfo.InvariantCulture);
                        estimated+=values[index]?.Length??0;
                        if(estimated>32768) throw new FormatException();
                    }
                    string result=string.Format(culture,text,values);
                    if(result.Length>32768) throw new FormatException();
                    return result;
                }
                catch(Exception ex) when(ex is FormatException || ex is ManagedExtensionValidationException)
                { lock(gate) Missing(key,"LocalizationFormatFailed"); return text; }
            }
            private void Missing(string key,string code)
            { if(catalog!=null && reported.Count<128 && reported.Add(code+":"+(key??"?"))) owner.Report(module,code,key:ExtensionLanguageFile.ValidKey(key)?key:null); }
            internal void SetLocale(string value)
            {
                Action callback; lock(gate) { if(catalog==null) return; locale=value; callback=languageChanged; }
                if(callback==null) return;
                foreach(Action handler in callback.GetInvocationList())
                {
                    if(Closed) break;
                    try { handler(); } catch { owner.Report(module,"LocalizationListenerFailed"); }
                }
            }
            public void Dispose() { lock(gate) { catalog=null; languageChanged=null; reported.Clear(); } }
        }
    }
}
