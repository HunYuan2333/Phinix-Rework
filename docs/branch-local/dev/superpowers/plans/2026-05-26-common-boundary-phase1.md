# Common Boundary Phase 1 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remove the first layer of server-only runtime pollution from `Common\Utils` without breaking current framework or extension callers.

**Architecture:** Keep the current `TextHelper.Highlight` API stable for all existing `Common` server-side callers, but replace the concrete `Pastel`/`System.Drawing` dependency with an internal ANSI formatter. Add a lightweight repo-local regression harness that guards the project boundary directly by checking project files and public behavior.

**Tech Stack:** .NET Framework 4.7.2, old-style MSBuild csproj files, console test harness, existing `Utils` project.

---

### Task 1: Add a boundary regression harness

**Files:**
- Create: `Tests/CommonBoundaryTests/CommonBoundaryTests.csproj`
- Create: `Tests/CommonBoundaryTests/Program.cs`

- [ ] **Step 1: Write the failing test harness**

```csharp
AssertFileDoesNotContain(utilsProjectFile, "Pastel");
AssertFileDoesNotContain(utilsProjectFile, "System.Drawing");
AssertFileDoesNotContain(utilsPackagesFile, "Pastel");
```

- [ ] **Step 2: Run test harness to verify it fails**

Run: `msbuild Tests\CommonBoundaryTests\CommonBoundaryTests.csproj /t:Build /p:Configuration=Debug`
Run: `Tests\CommonBoundaryTests\bin\Debug\CommonBoundaryTests.exe`
Expected: FAIL because `Common\Utils\Utils.csproj` and `packages.config` still reference `Pastel` and `System.Drawing`.

- [ ] **Step 3: Keep one behavior-level guard in the harness**

```csharp
var highlighted = TextHelper.Highlight("demo", HighlightType.Username);
Assert(highlighted.Contains("demo"), "Highlight should preserve the source text.");
Assert(highlighted.Contains("\u001b["), "Highlight should still emit ANSI formatting.");
```

- [ ] **Step 4: Re-run the harness after implementation**

Run: `Tests\CommonBoundaryTests\bin\Debug\CommonBoundaryTests.exe`
Expected: PASS

### Task 2: Remove concrete runtime pollution from `Common\Utils`

**Files:**
- Modify: `Common/Utils/TextHelper.cs`
- Modify: `Common/Utils/Utils.csproj`
- Modify: `Common/Utils/packages.config`

- [ ] **Step 1: Replace `Pastel` usage with an internal ANSI formatter**

```csharp
private const string ResetAnsiSequence = "\u001b[0m";

private static string applyAnsiHighlight(string input, string colourCode)
{
    if (string.IsNullOrEmpty(input)) return input;
    return $"\u001b[{colourCode}m{input}{ResetAnsiSequence}";
}
```

- [ ] **Step 2: Keep the existing `HighlightType` switch stable**

```csharp
case HighlightType.ConnectionID:
    return applyAnsiHighlight(str, "38;2;191;97;106");
```

- [ ] **Step 3: Remove obsolete project references**

```xml
<Reference Include="System.Drawing" />
<Reference Include="Pastel, Version=1.3.1.0, Culture=neutral, PublicKeyToken=null">
```

- [ ] **Step 4: Remove the `Pastel` package entry**

```xml
<package id="Pastel" version="1.3.1" targetFramework="net472" />
```

### Task 3: Verify the boundary slice still builds cleanly

**Files:**
- Verify: `Common/Utils/Utils.csproj`
- Verify: `Tests/CommonBoundaryTests/CommonBoundaryTests.csproj`

- [ ] **Step 1: Build the touched library**

Run: `msbuild Common\Utils\Utils.csproj /t:Build /p:Configuration=Debug`
Expected: BUILD SUCCEEDED

- [ ] **Step 2: Build and run the regression harness**

Run: `msbuild Tests\CommonBoundaryTests\CommonBoundaryTests.csproj /t:Build /p:Configuration=Debug`
Run: `Tests\CommonBoundaryTests\bin\Debug\CommonBoundaryTests.exe`
Expected: `All Common boundary tests passed.`

- [ ] **Step 3: Record the next boundary move explicitly**

The next implementation slice should extract the server-facing highlighting/logging API from `Common` into a server-oriented boundary without introducing reverse references from `Common` libraries into `Server`.

### Assumptions

- `TextHelper.Highlight` must remain callable from existing `Authentication`, `UserManagement`, and server extension code in this slice.
- A small console-based regression harness is acceptable because the repo currently has no dedicated application test project.
- This phase intentionally removes concrete runtime pollution first; it does not yet relocate the `Highlight` API itself.
