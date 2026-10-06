using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Xml;
using System.Xml.Linq;
using Phinix.PluginStore;
using PhinixClient.Framework;

// Explicit read-only reproduction of local-mod collision checks. No network, loads or writes.
internal static class LocalIdentityCheck
{
    internal static int Check(string[] args)
    {
        try
        {
            // --local-identities source-id catalog-json host-mod-root mod-container [mod-container ...]
            string host=ClientEnvironmentPaths.NormalizeAbsolute(args[3]);
            var mods=new List<ClientInstalledModSnapshot>();
            foreach(string root in args.Skip(4))
            foreach(string mod in Directory.EnumerateDirectories(ClientEnvironmentPaths.NormalizeAbsolute(root)))
            {
                string about=Path.Combine(mod,"About","About.xml");
                if(!File.Exists(about)) continue;
                if(mods.Count>=1024 || new FileInfo(about).Length>65536) throw new InvalidOperationException("Local mod limits exceeded.");
                using(var reader=XmlReader.Create(about,new XmlReaderSettings {DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=65536}))
                {
                    string id=XDocument.Load(reader).Root?.Element("packageId")?.Value.Trim().ToLowerInvariant();
                    mods.Add(new ClientInstalledModSnapshot(id,mod,false));
                }
            }
            byte[] bytes;
            using(var file=File.OpenRead(args[2])) bytes=PayloadValidator.ReadBounded(file,CatalogReader.MaxCatalogBytes,CancellationToken.None);
            var catalog=ManagedStoreCatalogReader.Read(bytes,args[1]);
            var environment=new ClientEnvironmentSnapshot(null,host,"1.6","0.9.7","1.6.0",mods,null,null,null);
            ManagedStoreLocalGate.Check(environment,catalog.Packages,CancellationToken.None);
            Console.WriteLine("Production local identity/collision gate passed; installed mods="+mods.Count+"; planned packages="+catalog.Packages.Count+". No code loaded or files written.");
            return 0;
        }
        catch(StoreValidationException ex)
        {
            Console.Error.WriteLine("Rejected: "+ex.Code);
            if(ex.LocalIdentity!=null) Console.Error.WriteLine("mod="+ex.LocalIdentity.ModId+"; file="+ex.LocalIdentity.RelativePath+"; reason="+ex.LocalIdentity.Reason);
            return 1;
        }
        catch(Exception ex) { Console.Error.WriteLine("Local check failed: "+ex.GetType().Name); return 1; }
    }
}
