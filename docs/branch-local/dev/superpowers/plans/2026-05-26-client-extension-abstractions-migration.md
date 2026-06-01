# Client Extension Abstractions Migration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move `ClientExtensionAbstractions` out of `Common` into a client-owned boundary without changing its public assembly or namespaces.

**Architecture:** Keep the source and assembly shape stable, but relocate the project to a client-side path so `Common` stops implying ownership of UI- and client-facing contracts. Update all project and solution references to the new path, then tighten the boundary regression harness around the new location.

**Tech Stack:** .NET Framework 4.7.2, old-style MSBuild csproj files, solution-level project references, console regression harness.

---

### Task 1: Tighten the boundary harness first

**Files:**
- Modify: `Tests/CommonBoundaryTests/Program.cs`

- [ ] **Step 1: Add a failing path assertion for the new client-owned project location**
- [ ] **Step 2: Rebuild the harness**
- [ ] **Step 3: Run the harness and verify it fails because the project is still under `Common`**

### Task 2: Relocate the project and update references

**Files:**
- Create: `Client/ClientExtensionAbstractions/*`
- Modify: `Phinix.sln`
- Modify: `Client/Source/Client.csproj`
- Modify: `Extensions/Chat/Client/ChatExtension.Client.csproj`
- Modify: `Extensions/Trade/Client/TradeExtension.Client.csproj`

- [ ] **Step 1: Move the project files into a client-owned directory**
- [ ] **Step 2: Repoint all project references to the new path**
- [ ] **Step 3: Keep assembly name and namespaces unchanged to avoid downstream churn**

### Task 3: Re-run boundary verification

**Files:**
- Verify: `Tests/CommonBoundaryTests/Program.cs`

- [ ] **Step 1: Rebuild the harness with the migrated paths**
- [ ] **Step 2: Run the harness and verify it passes**
- [ ] **Step 3: Check git diff/status to confirm only migration-related files changed**
