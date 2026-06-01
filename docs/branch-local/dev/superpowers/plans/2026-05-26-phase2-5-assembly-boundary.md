# Phase 2.5 Assembly Boundary Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move server-only authentication, user-management, and console-highlighting code out of `Common` assemblies without changing runtime behavior.

**Architecture:** Keep `Common` projects as shared contracts plus client/shared implementations, then introduce small server-only class libraries for `ServerAuthenticator`, `ServerUserManager`, and console highlighting. Update server and server extension references to depend on those server-only assemblies explicitly.

**Tech Stack:** .NET Framework 4.7.2, classic csproj/MSBuild, solution-level project references, CommonBoundaryTests

---

### Task 1: Lock the target boundary with tests

**Files:**
- Modify: `Tests/CommonBoundaryTests/Program.cs`

- [ ] Change the boundary assertions so they require `Common/*.csproj` to stop compiling `../../Server/...` source files.
- [ ] Add assertions that require new server-only project files to exist and be referenced instead.
- [ ] Run the boundary test executable build or solution build to confirm the new expectations fail before implementation.

### Task 2: Introduce server-only assemblies

**Files:**
- Create: `Server/ServerRuntime/ServerRuntime.csproj`
- Create: `Server/Authentication.Server/Authentication.Server.csproj`
- Create: `Server/UserManagement.Server/UserManagement.Server.csproj`
- Modify: `Phinix.sln`

- [ ] Create a tiny server runtime project that owns `Server/ConsoleHighlighting.cs`.
- [ ] Create an authentication server project that links `Server/Authentication/ServerAuthenticator.cs` and `Server/Authentication/Session.cs`.
- [ ] Create a user management server project that links `Server/UserManagement/ServerUserManager.cs` and `Server/UserManagement/ServerLoginEventArgs.cs`.
- [ ] Add the new projects to the solution.

### Task 3: Rewire project references

**Files:**
- Modify: `Common/Utils/Utils.csproj`
- Modify: `Common/Authentication/Authentication.csproj`
- Modify: `Common/UserManagement/UserManagement.csproj`
- Modify: `Server/Server.csproj`
- Modify: `Extensions/Chat/Server/ChatExtension.Server.csproj`
- Modify: `Extensions/Trade/Server/TradeExtension.Server.csproj`

- [ ] Remove `../../Server/...` compile includes from `Common` projects.
- [ ] Reference `ServerRuntime` from server-side projects that use `HighlightType` or `.Highlight(...)`.
- [ ] Reference `Authentication.Server` and `UserManagement.Server` where `ServerAuthenticator` and `ServerUserManager` are consumed.
- [ ] Keep client/shared projects untouched unless a compile error proves a shared dependency was missed.

### Task 4: Verify the boundary end-to-end

**Files:**
- Modify: `Tests/CommonBoundaryTests/Program.cs` if any assertion wording needs final adjustment

- [ ] Build the affected projects or solution.
- [ ] Run `CommonBoundaryTests` and confirm it passes with the new assembly boundaries.
- [ ] Report any remaining coupling that still exists but is intentionally deferred.
