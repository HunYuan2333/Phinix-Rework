# Branch-Local Docs

`docs/branch-local/` is reserved for branch-specific working docs.

- Shared docs stay at `docs/` root.
- The current shared set is limited to:
  - `docs/设计哲学.md`
  - `docs/设计哲学合规审计报告.md`
- Everything else that is mainly planning, iteration notes, migration notes, audit notes, or branch-only working context should live under a branch subdirectory such as `docs/branch-local/main/` or `docs/branch-local/dev/`.

## Merge Behavior

`docs/branch-local/**` is configured with `merge=ours` in `.gitattributes`.

Each local clone should also enable the merge driver once:

```powershell
git config merge.ours.driver true
```

That is an intentional workflow choice:

- when merging into `main`, `main` keeps its own branch-local docs
- when merging into `dev`, `dev` keeps its own branch-local docs

This avoids branch-only notes being repeatedly merged back and forth.

## Important Limitation

This is a workflow compromise, not a universal documentation strategy.

- Do not move architecture baseline docs that must stay synchronized across branches into `branch-local/`
- If a branch-local doc becomes stable, long-lived, and cross-branch relevant, promote it back to `docs/` root
