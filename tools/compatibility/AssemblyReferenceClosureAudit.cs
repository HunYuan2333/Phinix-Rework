using System.Reflection;using System.Reflection.Metadata;using System.Reflection.PortableExecutable;using System;using System.IO;using System.Linq;using System.Collections.Generic;using System.Text.Json;using Utils.Framework.ManagedExtensions;
var config=JsonSerializer.Deserialize<Dictionary<string,string[]>>(File.ReadAllText(args[0]));
var owned=config["owned"].Select(p=>{
 using var stream=File.OpenRead(p);using var pe=new PEReader(stream);var md=pe.GetMetadataReader();
 var refs=md.AssemblyReferences.Select(h=>{var r=md.GetAssemblyReference(h);var a=new AssemblyName();a.Name=md.GetString(r.Name);a.Version=r.Version;a.CultureName=md.GetString(r.Culture);
 if((r.Flags&AssemblyFlags.PublicKey)!=0)a.SetPublicKey(md.GetBlobBytes(r.PublicKeyOrToken));else a.SetPublicKeyToken(md.GetBlobBytes(r.PublicKeyOrToken));return ManagedAssemblyIdentity.FromAssemblyName(a);}).ToArray();
 return new{path=p,metadata=new{Identity=ManagedAssemblyIdentity.FromAssemblyName(AssemblyName.GetAssemblyName(p)),References=refs}};
}).ToArray();
var provided=config["providers"].Select(p=>new{path=p,identity=ManagedAssemblyIdentity.FromAssemblyName(System.Reflection.AssemblyName.GetAssemblyName(p))}).GroupBy(r=>r.identity.FullName).Select(g=>g.First()).ToArray();
var rules=new[]{new ManagedHostReferenceRule("Assembly-CSharp",ManagedHostReferenceVersionPolicy.SameReleaseFamily)};
var report=new List<object>();int failed=0;
foreach(var source in owned)foreach(var reference in source.metadata.References){
 var local=owned.Where(o=>o.metadata.Identity.Name==reference.Name).ToArray();
 var identities=local.Length>0?local.Select(o=>o.metadata.Identity):provided.Select(p=>p.identity);
 var selected=ManagedAssemblyIdentity.SelectHostReference(reference,identities,rules);
 string status=selected==null?"unresolved":"static binding available";
 if(config["game"].Single()=="1.7" && selected==null && reference.Name.StartsWith("System.",StringComparison.Ordinal) && local.Length==0){
  try{var actual=ManagedAssemblyIdentity.FromAssemblyName(Assembly.Load(new AssemblyName(reference.FullName)).GetName());
   if(provided.Any(p=>p.identity.FullName==actual.FullName) && actual.Culture==reference.Culture && actual.PublicKeyToken==reference.PublicKeyToken){selected=actual;status="CoreCLR framework binding observed";}}
  catch(Exception){}
 }
 if(selected==null)failed++;
 report.Add(new{source=source.path,required=reference.FullName,selected=selected?.FullName,kind=local.Length>0?"owned":"game/framework/Harmony",status});
}
Console.WriteLine(JsonSerializer.Serialize(new{assemblies=owned.Length,references=report.Count,unresolved=failed,bindings=report},new JsonSerializerOptions{WriteIndented=true}));return failed==0?0:1;
