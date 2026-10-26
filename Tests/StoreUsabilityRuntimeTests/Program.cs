using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Runtime.Serialization;
using System.Xml.Linq;
using PhinixClient.Framework;
using Utils.Framework.ManagedExtensions;
using Verse;

internal static class Program
{
    private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;
    private static Assembly store;
    private static int checks;
    private static Type Type(string name) => store.GetType("Phinix.PluginStore." + name, true);
    private static object Empty(Type type) => FormatterServices.GetUninitializedObject(type);
    private static void Set(object target, string name, object value)
    {
        var field = target.GetType().GetField(name, Private) ?? target.GetType().GetField("<" + name + ">k__BackingField", Private);
        if (field == null) throw new MissingFieldException(name);
        field.SetValue(target, value);
    }
    private static object Make(Type type, params object[] values)
    {
        object result = Empty(type);
        for (int i = 0; i < values.Length; i += 2) Set(result, (string)values[i], values[i + 1]);
        return result;
    }
    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); checks++; }
    private static ClientActionDefinition[] Actions(object view, object snapshot, object entry, bool busy = false, string prefix = "")
        => ((IEnumerable<ClientActionDefinition>)Type("ManagedPluginStoreView").GetMethod("BuildPackagePresentationActions", Private)
            .Invoke(view, new[] { snapshot, entry, (object)busy, prefix })).ToArray();
    private static ClientActionDefinition[] RowActions(object view,object snapshot,object entry,bool busy=false,string prefix="row.0.")
        => ((IEnumerable<ClientActionDefinition>)Type("ManagedPluginStoreView").GetMethod("BuildBrowsePresentationActions",Private).Invoke(view,new[]{snapshot,entry,(object)busy,prefix})).ToArray();
    private static object Manifest(string id, string version) => Make(typeof(ManagedExtensionManifest), "PackageId", id, "Name", id,
        "Version", ManagedExtensionVersion.Parse(version));
    private static object Record(string id, string version) => Make(Type("ManagedStoreRecord"), "Id", id, "Name", id, "State", "active",
        "Manifest", Manifest(id, version));
    private static object Entry(object record, object local = null) => Activator.CreateInstance(Type("ManagedStoreEntry"), Private, null,
        new[] { "catalog:fixture:" + Guid.NewGuid().ToString("N"), record, local }, null);
    private static object Installed(string id, string version, ManagedExtensionDesiredState desired, string disableCode = null)
    {
        var package = Make(typeof(ManagedExtensionPackageSnapshot), "PackageId", id, "Version", version, "DesiredState", desired,
            "Manifest", Manifest(id, version), "ContentState", ManagedExtensionContentState.ContentVerified);
        return Make(typeof(ManagedExtensionManagementPackage), "Package", package, "DisableBlockCode", disableCode);
    }
    public static int Main(string[] args)
    {
        string refs = Path.GetFullPath(args[0]);
        AssemblyLoadContext.Default.Resolving += (context, name) => File.Exists(Path.Combine(refs, name.Name + ".dll"))
            ? context.LoadFromAssemblyPath(Path.Combine(refs, name.Name + ".dll")) : null;
        store = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(refs, "PluginStore.Client.dll"));
        var oldLanguage = LanguageDatabase.activeLanguage;
        try
        {
            // Only translations are injected. Production policy and package types come from the built DLLs.
            var language = (LoadedLanguage)Empty(typeof(LoadedLanguage));
            Set(language, "dataIsLoaded", true); Set(language, "folderName", "English"); Set(language, "workerInt", new LanguageWorker_English());
            var field = typeof(LoadedLanguage).GetField("keyedReplacements", Private);
            var replacements = (IDictionary)Activator.CreateInstance(field.FieldType);
            var replacementType = field.FieldType.GetGenericArguments()[1];
            foreach (var text in XDocument.Load(Path.Combine(args[1], "Extensions/PluginStore/Package/Languages/English/Keyed/PluginStore.xml")).Root.Elements())
            {
                var replacement = Activator.CreateInstance(replacementType);
                Set(replacement, "key", text.Name.LocalName); Set(replacement, "value", text.Value);
                replacements.Add(text.Name.LocalName, replacement);
            }
            Set(language, "keyedReplacements", replacements); LanguageDatabase.activeLanguage = language;
            object controller = Activator.CreateInstance(Type("ManagedStoreController"), Private, null, new object[5], null);
            Set(controller, "inventoryKnown", true);
            object view = Make(Type("ManagedPluginStoreView"), "controller", controller,
                "themeRepository", Activator.CreateInstance(Type("ThemeStoreController"), Private, null, new object[2], null),
                "confirmations", new Dictionary<string, ClientPresentationConfirmation>());
            object repository = Activator.CreateInstance(Type("RepositoryBrowseInfo"), new object[] { null, DateTime.UtcNow, false, false });
            object snapshot = Make(Type("ManagedStoreSnapshot"), "Repository", repository);
            object fresh = Record("fixture.fresh", "1.0.0");
            object entry = Entry(fresh);
            var install = Actions(view, snapshot, entry, prefix: "row.0.");
            Check(install[0].Id == "row.0.package-install" && install[0].DisabledReason == null, "Unselected row offers install through the existing policy.");
            Check(Actions(view, snapshot, entry, true, "row.1.")[0].DisabledReason != null, "Busy row cannot start another operation.");
            Check(install[0].Id.Length <= 64, "Row action identity fits the contribution limit.");
            Set(repository, "Offline", true);
            Check(Actions(view, snapshot, entry)[0].DisabledReason != null, "Offline install remains blocked.");
            Set(repository, "Offline", false); Set(repository, "Stale", true);
            Check(Actions(view, snapshot, entry)[0].DisabledReason != null, "Stale repository cannot install.");
            Set(repository, "Stale", false); Set(controller, "inventoryKnown", false);
            Check(Actions(view, snapshot, entry)[0].DisabledReason != null, "Unknown inventory remains blocked.");
            Set(controller, "inventoryKnown", true);
            var enabled = Entry(fresh, Installed("fixture.fresh", "1.0.0", ManagedExtensionDesiredState.Enabled));
            // A different selected package must never determine this row's action.
            Set(view, "selectedLocal", Installed("fixture.other", "1.0.0", ManagedExtensionDesiredState.PendingRemoval));
            var toggle = Actions(view, snapshot, enabled, prefix: "row.2.");
            Check(toggle[0].Id == "row.2.package-toggle" && toggle[0].DisabledReason == null, "Row policy reads its own installed record, not the selected package.");
            var blocked = Entry(fresh, Installed("fixture.fresh", "1.0.0", ManagedExtensionDesiredState.Enabled, "blocked"));
            Check(Actions(view, snapshot, blocked)[0].DisabledReason != null, "Existing disable guard is preserved.");
            var removed = Entry(fresh, Installed("fixture.fresh", "1.0.0", ManagedExtensionDesiredState.PendingRemoval));
            Check(Actions(view, snapshot, removed)[0].Id == "package-cancel-removal", "Pending removal offers restoration, not install or toggle.");
            var updated = Entry(Record("fixture.fresh", "2.0.0"), Installed("fixture.fresh", "1.0.0", ManagedExtensionDesiredState.Enabled));
            Check(Actions(view, snapshot, updated)[0].Id == "package-update", "Newer catalog version offers update.");
            var disabledUpdate = Entry(Record("fixture.fresh", "2.0.0"), Installed("fixture.fresh", "1.0.0", ManagedExtensionDesiredState.Disabled));
            Check(Actions(view, snapshot, disabledUpdate)[0].DisabledReason != null, "Disabled-package update restriction is preserved.");
            var confirmations = (Dictionary<string, ClientPresentationConfirmation>)Type("ManagedPluginStoreView").GetField("confirmations", Private).GetValue(view);
            Check(confirmations.ContainsKey("row.2.package-uninstall"), "Uninstall confirmation stays owned by its exact action identity.");
            var row=RowActions(view,snapshot,enabled);
            Check(row.Select(a=>a.Id).SequenceEqual(new[]{"row.0.package-toggle","row.0.package-uninstall"}),"Installed rows expose toggle and uninstall together.");
            Check(confirmations["row.0.package-uninstall"].Text.Contains("fixture.fresh")&&confirmations["row.0.package-uninstall"].Text.Contains("1.0.0"),"Row uninstall confirmation identifies its package and installed version.");
            Check(RowActions(view,snapshot,enabled,true).All(a=>a.DisabledReason!=null),"Busy state disables both row operations.");
            Check(RowActions(view,snapshot,removed).Single().Id=="row.0.package-cancel-removal","Pending removal row offers only its guarded restore operation.");
            Check(RowActions(view,snapshot,updated).Select(a=>a.Id).SequenceEqual(new[]{"row.0.package-update","row.0.package-uninstall"}),"Update rows retain direct uninstall without a detail detour.");
            Check(RowActions(view,snapshot,entry).Single().Id=="row.0.package-install","Uninstalled row does not offer uninstall.");
            var capacity=Type("ManagedPluginStoreView").GetMethod("BrowseCapacity",BindingFlags.Static|BindingFlags.NonPublic);
            foreach(int baseCount in new[]{0,12,15,18,22,28})
            {int size=(int)capacity.Invoke(null,new object[]{baseCount});Check(size>0&&size<=6&&baseCount+2+size*2<=32,"Pagination keeps both row operations inside the real contributor action limit: "+baseCount);}
            object plan = Make(Type("ManagedStorePlan"), "Root", fresh);
            Set(view, "pendingPlan", plan); Set(snapshot, "Plan", plan);
            var review = Actions(view, snapshot, entry, true);
            Check(review.Select(a => a.Id).SequenceEqual(new[] { "package-confirm-plan", "package-cancel-plan" }), "Plan review exposes only explicit confirm and cancel.");
            Check(review[0].DisabledReason == null && !confirmations.ContainsKey("package-confirm-plan"), "Page review has one confirmation, without a duplicate modal.");
            Check(Actions(view, snapshot, Entry(Record("fixture.other", "1.0.0")), true)[0].DisabledReason != null, "A plan cannot confirm another root package.");
            Set(snapshot, "Plan", Make(Type("ManagedStorePlan"), "Root", fresh));
            Check(Actions(view, snapshot, entry, true)[0].DisabledReason != null, "A replaced plan invalidates the old confirmation.");
            review.Single(a => a.Id == "package-cancel-plan").Execute();
            Check(Type("ManagedPluginStoreView").GetField("pendingPlan", Private).GetValue(view) == null, "Cancel drops the review without invoking download.");
            Console.WriteLine("PASS " + checks + " Store usability policy checks against actual compiled DLLs; native UI and network not executed.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally { LanguageDatabase.activeLanguage = oldLanguage; }
    }
}
