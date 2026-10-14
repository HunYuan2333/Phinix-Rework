using System;
using System.IO;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
var reports=new List<object>();
foreach(string path in args)
{
    using var stream=File.OpenRead(path);
    using var pe=new PEReader(stream);
    var md=pe.GetMetadataReader();
    string framework=null,productVersion=null,compatibilityVersion=null;
    foreach(var ah in md.GetAssemblyDefinition().GetCustomAttributes())
    {
        var a=md.GetCustomAttribute(ah);EntityHandle parent=default;
        if(a.Constructor.Kind==HandleKind.MemberReference)parent=md.GetMemberReference((MemberReferenceHandle)a.Constructor).Parent;
        else if(a.Constructor.Kind==HandleKind.MethodDefinition)parent=md.GetMethodDefinition((MethodDefinitionHandle)a.Constructor).GetDeclaringType();
        string name=parent.Kind==HandleKind.TypeReference?md.GetString(md.GetTypeReference((TypeReferenceHandle)parent).Name):parent.Kind==HandleKind.TypeDefinition?md.GetString(md.GetTypeDefinition((TypeDefinitionHandle)parent).Name):"";
        if(name=="TargetFrameworkAttribute" || name=="AssemblyInformationalVersionAttribute")
        {
            var reader=md.GetBlobReader(a.Value);reader.ReadUInt16();string value=reader.ReadSerializedString();
            if(name=="TargetFrameworkAttribute")framework=value;else productVersion=value;
        }
    }
    foreach(var handle in md.TypeDefinitions)
    {
        var type=md.GetTypeDefinition(handle);
        if(md.GetString(type.Namespace)!="PhinixClient" || md.GetString(type.Name)!="Client")continue;
        foreach(var fieldHandle in type.GetFields())
        {
            var field=md.GetFieldDefinition(fieldHandle);
            if(md.GetString(field.Name)!="CompatibilityVersion" || field.GetDefaultValue().IsNil)continue;
            var constant=md.GetConstant(field.GetDefaultValue());
            if(constant.TypeCode==ConstantTypeCode.String)compatibilityVersion=Encoding.Unicode.GetString(md.GetBlobBytes(constant.Value));
        }
    }
    var references=new List<object>();
    foreach(var handle in md.AssemblyReferences)
    {
        var r=md.GetAssemblyReference(handle);references.Add(new{name=md.GetString(r.Name),version=r.Version.ToString()});
    }
    reports.Add(new{path,identity=AssemblyName.GetAssemblyName(path).FullName,framework,references,productVersion,compatibilityVersion});
}
Console.WriteLine(JsonSerializer.Serialize(reports,new JsonSerializerOptions{WriteIndented=true}));
