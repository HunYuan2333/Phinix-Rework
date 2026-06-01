# Client Official Extension Runtime Loading Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the client load official extensions at runtime from built DLLs instead of directly project-referencing `ChatExtension.Client` and `TradeExtension.Client`.

**Architecture:** Keep the current server transitional model as the template. The client will continue using `PhinixExtensionRegistry` for discovery, but the host will explicitly preload official extension assemblies before framework startup, and the build pipeline will compile/copy official extension DLLs into the client assemblies directory without compile-time implementation references from `Client.csproj`.

**Tech Stack:** C#, .NET Framework 4.7.2 client project, MSBuild legacy csproj, RimWorld/Unity mod layout, existing framework v2 extension registry.

---

### Task 1: Add client-side official extension assembly preloading

**Files:**
- Modify: `Client/Source/Client.cs`
- Test: manual startup validation through compile + framework API resolution

- [ ] **Step 1: Add a failing startup check target**

Document the expected red case before code changes:

```text
If ChatExtension.Client.dll and TradeExtension.Client.dll are not loaded into the AppDomain
before PhinixFrameworkClient constructs its registry state, TryResolveExtensionApi for
IFrameworkChatClientApi / IFrameworkTradeClientApi will fail and Client startup will throw.
```

- [ ] **Step 2: Verify the current client depends on compile-time references**

Run:

```powershell
rg -n "ChatExtension.Client|TradeExtension.Client|TryResolveExtensionApi" Client/Source
```

Expected:

```text
Client.csproj directly references ChatExtension.Client and TradeExtension.Client
Client.cs throws if built-in chat/trade APIs are not registered
```

- [ ] **Step 3: Implement client-side runtime preloading helper**

Add a helper in `Client/Source/Client.cs` mirroring the server behavior:

```csharp
private static void EnsureOfficialExtensionAssemblyLoaded(string assemblyName)
{
    if (string.IsNullOrWhiteSpace(assemblyName))
    {
        return;
    }

    if (AppDomain.CurrentDomain
        .GetAssemblies()
        .Any(assembly => string.Equals(assembly.GetName().Name, assemblyName, StringComparison.OrdinalIgnoreCase)))
    {
        return;
    }

    try
    {
        Assembly.Load(new AssemblyName(assemblyName));
        return;
    }
    catch
    {
        string assemblyPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory ?? ".", assemblyName + ".dll");
        if (!File.Exists(assemblyPath))
        {
            return;
        }

        Assembly.LoadFrom(assemblyPath);
    }
}
```

- [ ] **Step 4: Call the preload helper before `PhinixFrameworkClient` is constructed**

Insert calls in the client constructor before `frameworkClient = new PhinixFrameworkClient(...)`:

```csharp
EnsureOfficialExtensionAssemblyLoaded("ChatExtension.Client");
EnsureOfficialExtensionAssemblyLoaded("TradeExtension.Client");
```

- [ ] **Step 5: Re-run code search to confirm the new loading entrypoint exists**

Run:

```powershell
rg -n "EnsureOfficialExtensionAssemblyLoaded|ChatExtension.Client|TradeExtension.Client" Client/Source/Client.cs
```

Expected:

```text
Client.cs now explicitly preloads both official client extension assemblies before framework init
```

### Task 2: Remove client compile-time implementation references and replace them with build-copy steps

**Files:**
- Modify: `Client/Source/Client.csproj`

- [ ] **Step 1: Write the failing build expectation**

Document the intended red condition:

```text
After removing direct ProjectReference entries, the client build will no longer transitively
produce ChatExtension.Client.dll / TradeExtension.Client.dll unless the build pipeline explicitly
builds those projects and copies their outputs.
```

- [ ] **Step 2: Remove `ProjectReference` entries for client implementation assemblies**

Delete these references from `Client/Source/Client.csproj`:

```xml
<ProjectReference Include="..\..\Extensions\Chat\Client\ChatExtension.Client.csproj">
  <Project>{0E62A5A2-C95D-4CE4-B77B-6D5C698A2101}</Project>
  <Name>ChatExtension.Client</Name>
</ProjectReference>
<ProjectReference Include="..\..\Extensions\Trade\Client\TradeExtension.Client.csproj">
  <Project>{0D9D0722-46D1-4330-9177-C5E4D2452102}</Project>
  <Name>TradeExtension.Client</Name>
</ProjectReference>
```

- [ ] **Step 3: Add official client extension project items**

Add a dedicated item group:

```xml
<ItemGroup>
  <OfficialClientExtensionProject Include="..\..\Extensions\Chat\Client\ChatExtension.Client.csproj" />
  <OfficialClientExtensionProject Include="..\..\Extensions\Trade\Client\TradeExtension.Client.csproj" />
</ItemGroup>
```

- [ ] **Step 4: Build and collect official client extension outputs during `AfterBuild`**

Add MSBuild calls analogous to the server project:

```xml
<MSBuild
  Projects="@(OfficialClientExtensionProject)"
  Targets="Build"
  Properties="Configuration=$(Configuration)" />

<MSBuild
  Projects="@(OfficialClientExtensionProject)"
  Targets="GetTargetPath"
  Properties="Configuration=$(Configuration)">
  <Output TaskParameter="TargetOutputs" ItemName="_OfficialClientExtensionOutput" />
</MSBuild>

<Copy
  SourceFiles="@(_OfficialClientExtensionOutput)"
  DestinationFolder="$(TargetDir)"
  SkipUnchangedFiles="true" />
```

- [ ] **Step 5: Keep existing mod packaging copies, now sourcing from `$(TargetDir)`**

Preserve the existing copy operations for:

```xml
<Copy SourceFiles="$(TargetDir)\ChatExtension.Client.dll" DestinationFiles="$(SolutionDir)\Client\Common\Assemblies\ChatExtension.Client.dll" />
<Copy SourceFiles="$(TargetDir)\TradeExtension.Client.dll" DestinationFiles="$(SolutionDir)\Client\Common\Assemblies\TradeExtension.Client.dll" />
```

Expected:

```text
The client project no longer compile-time references official extension implementations,
but the built DLLs still land in the mod assemblies output.
```

### Task 3: Verify the transitional model and document next unification step

**Files:**
- Modify: `docs/cs架构重构和技术迁移需求分析-基于当前实现修订版.md` only if implementation reveals new constraints

- [ ] **Step 1: Build the client project**

Run:

```powershell
dotnet build Client/Source/Client.csproj
```

Expected:

```text
Build succeeds and produces ChatExtension.Client.dll / TradeExtension.Client.dll in the client target directory
```

- [ ] **Step 2: Sanity-check runtime loading references**

Run:

```powershell
rg -n "EnsureOfficialExtensionAssemblyLoaded|OfficialClientExtensionProject" Client/Source
```

Expected:

```text
Client host code and project file both show the new transitional runtime-loading model
```

- [ ] **Step 3: Record the immediate follow-up**

Capture the next slice in the work summary:

```text
Extract a shared extension assembly loader or equivalent host bootstrap path so Client and Server
stop duplicating explicit preloading logic before moving to real directory scanning.
```

- [ ] **Step 4: Commit**

```bash
git add Client/Source/Client.cs Client/Source/Client.csproj docs/superpowers/plans/2026-05-27-client-official-extension-runtime-loading.md
git commit -m "refactor: runtime-load official client extensions"
```
