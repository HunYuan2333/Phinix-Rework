using System;
using System.Collections.Generic;
using System.Linq;
using Phinix.PluginStore;

internal static partial class Program
{
    private static void ManagedStoreExperienceRegression(Dictionary<string,byte[]> files)
    {
        string listing=ManagedListing(Manifest(files),new byte[]{1,2,3});
        string newer=listing.Replace("\"version\":\"1.0.0\"","\"version\":\"1.1.0\"").Replace("v1.0.0","v1.1.0");
        string newest=listing.Replace("\"version\":\"1.0.0\"","\"version\":\"1.2.0\"").Replace("v1.0.0","v1.2.0").Replace("\"state\":\"active\"","\"state\":\"withdrawn\"");
        var catalog=ManagedStoreCatalogReader.Read(Utf8(ManagedCatalog(newest,listing,newer)),ManagedSource);
        var groups=ManagedStoreListing.Build(catalog,"","en-US");
        Assert(groups.Length==1 && groups[0].Versions.Count==3,"Versions share one player list entry");
        Assert(groups[0].Preferred.Manifest.Version.ToString()=="1.1.0","Newest active version is selected rather than withdrawn latest");
        Assert(groups[0].Versions[0].Manifest.Version.ToString()=="1.2.0","Version history retains newest withdrawn version for explicit browsing");
        Assert(ManagedStoreListing.Build(catalog,"Test author","en").Length==1,"Author search finds a grouped plugin");
        Assert(ManagedStoreListing.Build(catalog,"small managed","zh-CN").Length==1,"Search uses localized fallback");
        Assert(ManagedStoreListing.Build(catalog,"absent","en").Length==0,"Search excludes unrelated groups");
        Assert(ManagedStoreListing.Build(null,"","en").Length==0,"No catalog has an empty safe listing");
        var root=groups[0].Preferred;
        var refreshed=ManagedStoreCatalogReader.Read(Utf8(ManagedCatalog(newest,listing,newer)),ManagedSource);
        var restored=ManagedStoreListing.RestoreSelection(refreshed,root);
        Assert(restored!=root && restored.Manifest.Version.CompareTo(root.Manifest.Version)==0,
            "Refreshing rebuilt records retains the exact selected version rather than resetting or selecting latest");
        Assert(ManagedStoreListing.RestoreSelection(ManagedStoreCatalogReader.Read(Utf8(ManagedCatalog(listing)),ManagedSource),root)==null,
            "A version removed from the catalog cannot retain stale action controls");
        Assert(ManagedStoreListing.RestoreSelection(null,root)==null && ManagedStoreListing.RestoreSelection(refreshed,null)==null,
            "Empty catalogs and no selection remain safe");
        var plan=new ManagedStorePlan(catalog,root,new[]{new ManagedStorePlanItem(root,null)});
        Func<ManagedStoreState,ManagedStoreCatalogSnapshot,ManagedStorePlan,ManagedStoreSnapshot> state=(s,c,p)=>new ManagedStoreSnapshot(1,s,c,null,p,null,null);
        var intent=new ManagedStoreInstallIntent(catalog,root);
        Assert(intent.Take(state(ManagedStoreState.Planning,catalog,null),root)==null,"Planning does not prematurely start a transfer");
        Assert(intent.Take(state(ManagedStoreState.PlanReady,catalog,plan),root)==plan,"Exact ready plan consumes install intent");
        Assert(intent.Take(state(ManagedStoreState.PlanReady,catalog,plan),root)==null,"Repeated GUI events cannot repeat installation");
        Assert(!ManagedStoreInstallIntent.NeedsConfirmation(plan),"Root-only plan directly installs");
        var dependency=catalog.Packages.First(p=>p!=root);
        var different=new ManagedStoreRecord("test.dependency",dependency.Name,dependency.Author,dependency.License,dependency.Summary,
            dependency.Tags,dependency.State,dependency.Manifest,dependency.DeclarationHash,dependency.Artifact,null,null,dependency.RimWorldVersions);
        Assert(ManagedStoreInstallIntent.NeedsConfirmation(new ManagedStorePlan(catalog,root,new[]{new ManagedStorePlanItem(different,null),new ManagedStorePlanItem(root,null)})),"Additional dependencies require confirmation");
        foreach(var failed in new[]{ManagedStoreState.Failed,ManagedStoreState.Canceled,ManagedStoreState.Stopped,ManagedStoreState.Ready})
        {
            var canceled=new ManagedStoreInstallIntent(catalog,root);
            Assert(canceled.Take(state(failed,catalog,null),root)==null && canceled.Take(state(ManagedStoreState.PlanReady,catalog,plan),root)==null,"Failed/canceled/replaced intent never revives: "+failed);
        }
        var changed=new ManagedStoreInstallIntent(catalog,root);
        Assert(changed.Take(state(ManagedStoreState.PlanReady,catalog,plan),dependency)==null && changed.Take(state(ManagedStoreState.PlanReady,catalog,plan),root)==null,"Selection change permanently invalidates intent");
        changed=new ManagedStoreInstallIntent(catalog,root);
        var other=new ManagedStoreCatalogSnapshot(catalog.SourceId,catalog.SnapshotId,catalog.Sha256,catalog.Packages);
        Assert(changed.Take(state(ManagedStoreState.PlanReady,other,plan),root)==null,"A replaced catalog with identical content cannot reuse stale intent");
        var profile=RepositoryProfile.Official;
        Assert(profile.SourceId=="phinix.official" && profile.Repository=="HunYuan2333/Phinix-Plugin-Index","Player profile uses the official index only");
        Assert(new RepositoryEndpoint(profile,RepositoryAccessMethod.GitHub).IdentityKey==new RepositoryEndpoint(profile,RepositoryAccessMethod.Cloudflare).IdentityKey,"Official GitHub and CF access share ownership");
        var previousProfile=new RepositoryProfile(profile.SourceId,profile.Repository,profile.RepositoryId,profile.OwnerId,profile.PublicationBranch,"https://plugins-staging.hunyuan2333.com");
        Assert(profile.GatewayOrigin=="https://plugins.hunyuan2333.com" && profile.IdentityKey==previousProfile.IdentityKey,"Formal gateway migration preserves previously installed ownership");
        Assert(new RepositoryEndpoint(profile,RepositoryAccessMethod.Cloudflare).AccessKey!=new RepositoryEndpoint(previousProfile,RepositoryAccessMethod.Cloudflare).AccessKey,"Domain migration cannot reuse the old endpoint HTTP validator");
    }
}
