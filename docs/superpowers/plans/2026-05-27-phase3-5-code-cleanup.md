# Phase 3.5 Code Cleanup Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Finish the code-only Phase 3.5 cleanup by removing legacy runtime APIs without touching Docker yet.

**Architecture:** Keep the existing client/server behavior stable while replacing abrupt thread termination with cooperative stop flags in `Connections`, and replace legacy crypto RNG usage in `Authentication` with the modern BCL API. Add a tiny repo-local regression harness that locks the cleanup to source-level API bans and preserved client-key behavior.

**Tech Stack:** C#, SDK-style multi-target projects (`net472` + `net10.0`), repo-local console regression tests

---
