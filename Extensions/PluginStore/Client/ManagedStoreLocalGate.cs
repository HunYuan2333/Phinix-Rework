using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Xml;
using System.Xml.Linq;
using PhinixClient.Framework;
using Utils.Framework.ManagedExtensions;

namespace Phinix.PluginStore
{
    internal static class ManagedStoreLocalGate
    {
        // Inactive legacy mods still reserve CLR names. Never erase or migrate their files implicitly.
        internal static void Check(ClientEnvironmentSnapshot environment,IEnumerable<ManagedStoreRecord> packages,CancellationToken token)
        {
            var names=new HashSet<string>(packages.Where(p=>!p.IsWorkshop).SelectMany(p=>p.Manifest.Assemblies).SelectMany(ManagedExtensionManifestReader.AssemblyNames),StringComparer.OrdinalIgnoreCase);
            long bytes=0; int files=0,mods=0;
            foreach(var mod in environment.InstalledMods)
            {
                string currentPath=mod.RootDirectory;
                try
                {
                    token.ThrowIfCancellationRequested(); if(++mods>1024) throw Error("LocalLimit");
                    if(string.Equals(mod.RootDirectory,environment.HostModRoot,ClientPathOwnership.Comparison)) continue;
                    CheckLinks(mod.RootDirectory);
                    var folders=new HashSet<string>(StringComparer.OrdinalIgnoreCase) {mod.RootDirectory,Path.Combine(mod.RootDirectory,"Common")};
                    if(Directory.Exists(mod.RootDirectory))
                    {
                        int count=0;
                        foreach(string child in Directory.EnumerateDirectories(mod.RootDirectory))
                        {
                            if(++count>512) throw Error("LocalLimit");
                            if(System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(child),@"\A[0-9]+\.[0-9]+\z")) folders.Add(child);
                        }
                    }
                    string load=Path.Combine(mod.RootDirectory,"LoadFolders.xml"); currentPath=load; CheckLinks(load);
                    if(File.Exists(load))
                    {
                        if(new FileInfo(load).Length>65536) throw Error("LocalLimit");
                        try
                        {
                            using(var reader=XmlReader.Create(load,new XmlReaderSettings {DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=65536}))
                            {
                                int count=0;
                                foreach(var item in XDocument.Load(reader).Descendants("li"))
                                {
                                    if(++count>128) throw Error("LocalLimit");
                                    folders.Add(ResolveLoadFolder(mod,load,item.Value));
                                }
                            }
                        }
                        catch(XmlException ex) { throw Detail("LocalIdentityUncertain",mod,load,"LocalLoadFolderInvalid",ex); }
                    }
                    foreach(string folder in folders)
                    {
                        string assemblies=Path.Combine(folder,"Assemblies"); currentPath=assemblies; CheckLinks(assemblies);
                        if(!Directory.Exists(assemblies)) continue;
                        foreach(string file in Directory.EnumerateFiles(assemblies))
                        {
                            token.ThrowIfCancellationRequested(); if(++files>4096) throw Error("LocalLimit");
                            if(!file.EndsWith(".dll",StringComparison.OrdinalIgnoreCase)) continue;
                            currentPath=file; CheckLinks(file); long length=new FileInfo(file).Length; bytes+=length;
                            if(length<=0 || length>ManagedExtensionManifestReader.MaxFileBytes || bytes>ManagedExtensionManifestReader.MaxExpandedBytes) throw Error("LocalLimit");
                            try
                            {
                                using(var stream=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.Read))
                                { if(names.Contains(ManagedExtensionMetadataReader.ReadIdentity(PayloadValidator.ReadBounded(stream,length,token)).Name)) throw Detail("LegacyModAssemblyConflict",mod,file,"LocalAssemblyNameConflict"); }
                            }
                            catch(ManagedExtensionValidationException ex) { throw Detail("LocalIdentityUncertain",mod,file,ex.Code,ex); }
                        }
                    }
                }
                catch(StoreValidationException ex)
                {
                    if(ex.LocalIdentity==null) ex.LocalIdentity=Diagnostic(mod,currentPath,ex.Code=="LocalIdentityUncertain"?"LocalLinkRejected":ex.Code);
                    throw;
                }
                catch(Exception ex) when(ex is IOException || ex is UnauthorizedAccessException)
                { throw Detail("LocalIdentityUncertain",mod,currentPath,"LocalFileUnavailable",ex); }
            }
        }
        private static string ResolveLoadFolder(ClientInstalledModSnapshot mod,string load,string value)
        {
            // Normalize local separators and dot segments before containment checks.
            // This also lets Unix checks inspect Windows-style relative subdirectories.
            string folder=value.Trim().Replace('\\','/');
            if(folder.Length>256 || folder.Contains(":") || folder.StartsWith("//",StringComparison.Ordinal) || folder.Split('/').Any(p=>p==".."))
                throw Detail("LocalIdentityUncertain",mod,load,"LocalLoadFolderInvalid");
            folder=string.Join("/",folder.TrimStart('/').Split('/').Where(p=>p.Length!=0 && p!="."));
            try
            {
                string path=Path.GetFullPath(Path.Combine(mod.RootDirectory,folder.Replace('/',Path.DirectorySeparatorChar)));
                if(!ClientPathOwnership.Contains(mod.RootDirectory,path)) throw Detail("LocalIdentityUncertain",mod,load,"LocalLoadFolderInvalid");
                return path;
            }
            catch(Exception ex) when(ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException)
            { throw Detail("LocalIdentityUncertain",mod,load,"LocalLoadFolderInvalid",ex); }
        }
        private static StoreValidationException Detail(string code,ClientInstalledModSnapshot mod,string path,string reason,Exception inner=null)
        { return new StoreValidationException(code,"Local mod identity verification: "+code,inner) {LocalIdentity=Diagnostic(mod,path,reason)}; }
        private static LocalIdentityDiagnostic Diagnostic(ClientInstalledModSnapshot mod,string path,string reason)
        {
            string relative=ClientPathOwnership.Contains(mod.RootDirectory,path)?path.Substring(mod.RootDirectory.Length).TrimStart(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar):Path.GetFileName(path);
            return new LocalIdentityDiagnostic(mod.PackageId,relative.Length==0?"/":relative.Replace('\\','/'),reason);
        }
        private static void CheckLinks(string path)
        {
            for(string current=path;!string.IsNullOrEmpty(current);current=Path.GetDirectoryName(current))
            { try { if((File.GetAttributes(current)&FileAttributes.ReparsePoint)!=0) throw Error("LocalIdentityUncertain"); } catch(FileNotFoundException) {} catch(DirectoryNotFoundException) {} }
        }
        private static StoreValidationException Error(string code) { return new StoreValidationException(code,"Local mod identity verification: "+code); }
    }
}
