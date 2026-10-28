# Agent Instructions

## Purpose

This file holds the general rules for coding agents working in this repository: how to start and end a session, how to change source, how to verify and deliver changes, and when to ask. Nothing in it is specific to this solution.

What is specific lives beside the code:

- `implementation.md` at the root describes the solution as a whole: its products, development environment, shared conventions, decisions, and known traps.
- `implementation.md` in a project's folder describes that project: its conventions, the user's requirements for it, and how it is built.
- `HANDOFF.md` at the root and in a project's folder records the current state of the work and its next steps.

Every coding agent working on this repository should read this file completely before modifying the project.

The current source tree is authoritative if documentation and source disagree.

------

# 1. Starting an Agent Session

Before making changes:

1. Read this `AGENTS.md` completely.
2. Read the root `implementation.md`, and the `implementation.md` of each project you will work on, if present.
3. Read the root `HANDOFF.md`, and the `HANDOFF.md` of each project you will work on, if present.
4. Inspect the current source files involved in the request.
5. Inspect related implementations elsewhere in the solution where useful.
6. Check the current build state when practical.
7. Treat the current source tree as authoritative.
8. Do not redo work that a `HANDOFF.md` identifies as completed.

If documentation conflicts with current source behavior, identify the conflict rather than silently guessing which is correct.

------

# 2. General Architecture Principles

## 2.1 Keep Applications Lightweight

Do not introduce large application frameworks, dependency-injection systems, MVVM frameworks, or other architectural layers merely for architectural purity.

Introduce abstractions when they solve a real problem such as:

- code duplication,
- testability,
- cross-application reuse,
- clear separation of rendering from calculation,
- persistent settings,
- document handling shared by several applications.

Prefer straightforward, maintainable code over unnecessary abstraction.

## 2.2 Reuse Existing Infrastructure

Before implementing functionality, inspect the other projects in the solution for an existing implementation.

Common functionality should behave consistently where appropriate, for example loading and rendering documents, zoom handling, printing, recent files, settings, file dialogs, About dialogs, toolbar styling, keyboard navigation, and document-state handling.

Do not independently reinvent functionality that already exists elsewhere in the solution without a reason.

## 2.3 Separate Calculation From Presentation

Where practical, calculations should not depend on UI objects. For example, layout calculations should produce logical placement information independently of rendering.

This improves:

- testability,
- reuse,
- debugging,
- future export/rendering capabilities.

Do not force this separation where it adds complexity without practical benefit.

------

# 3. Version-Sensitive Technologies

Do not assume APIs or syntax from older framework or library versions. The root `implementation.md` names the versions in use.

For version-sensitive technologies (frameworks, UI toolkits, wrappers of native libraries, installer toolsets, IDE tooling), verify that the proposed syntax applies to the version actually used by the project.

Before using a native library's API, inspect the actual wrapper and version used by the project rather than assuming an API from another wrapper.

Do not confuse types that share a name or a purpose across libraries, such as a UI toolkit's image control, a native library's image objects, and bitmaps.

------

# 4. Behavior Users Rely On

## Keyboard

Existing keyboard shortcuts should be treated as part of the application's user interface contract.

Before assigning a new shortcut, inspect existing commands for conflicts.

## Settings

Avoid introducing additional settings files when settings naturally belong to an application's existing configuration.

Shared settings behavior may be implemented through common code while application-specific values remain in each application's own settings.

Settings may be externally edited. Preserve existing reload/watch behavior where implemented.

## Documents

Never allow a dirty editable document to be silently discarded unless that behavior has explicitly been requested.

When closing a dirty document:

- Save should save and continue the requested operation.
- Don't Save/No should discard changes and continue the requested operation.
- Cancel should abort the requested operation.

## Consistency

The applications of a suite should look related.

When introducing UI elements, inspect corresponding implementations in the other applications. Prefer consistency in toolbar appearance, menu organization, icons, spacing, disabled states, status bars, dialogs, zoom controls, and navigation.

Do not copy UI blindly if the application's workflow requires different behavior.

## Licensing

Respect each product's licensing as the root `implementation.md` records it. Do not add license text, license files, or license metadata that a product does not have, including when copying code or dialogs from a product licensed differently.

When a third-party package is added, removed, or upgraded, update the third-party notices with the exact license texts of the new versions.

------

# 5. Source Modification Rules

These rules are mandatory when providing code changes.

## Rule 1 — Verify Version-Sensitive Syntax

Check that APIs and syntax are appropriate for the actual project versions before proposing code.

Do not provide obsolete syntax and then correct it in a later step.

## Rule 2 — Identify Files and Methods

For every source modification, explicitly identify:

- the exact file,
- the class when useful,
- the exact method being changed.

## Rule 3 — Provide Complete Methods

When modifying an existing method, provide the **complete replacement method**.

Do not provide fragments such as:

> Insert these three lines after...

unless explicitly requested.

## Rule 4 — Preserve Code Lines

Do not unnecessarily wrap generated source-code lines.

Keep a code line intact unless it exceeds approximately 100 characters.

## Rule 5 — One Considered Implementation

Before presenting code, reason through the approach and provide one considered implementation.

Do not present an initial implementation and immediately revise it in the same development step.

## Rule 6 — Inspect Current Source

Never assume that the source still matches an earlier conversation, ZIP file, or previous agent's memory.

Inspect the current files before modifying them.

The current source tree is authoritative.

## Rule 7 — Minimal Changes

Modify only files necessary for the requested feature or fix.

Avoid unrelated cleanup, formatting changes, renaming, or architectural refactoring.

Do not turn a focused change into a broad refactoring exercise.

------

# 6. Deliverable Rules

When source changes are delivered as a ZIP:

1. Include only files changed for the requested work unless a complete solution was explicitly requested.
2. Preserve the complete directory hierarchy.
3. Include the repository's root directory itself in the archive.

For example, for a repository named `Solution`:

```text
Solution.202609171130.zip
└── Solution/
    └── Project/
        ├── MainWindow.xaml
        └── MainWindow.xaml.cs
```

Do **not** produce:

```text
Solution.202609171130.zip
├── MainWindow.xaml
└── MainWindow.xaml.cs
```

The root directory is deliberately included so that the archive can be extracted over the working tree safely.

Generated archives should exclude build and repository artifacts unless explicitly needed:

- `bin`
- `obj`
- `publish`
- `.vs`
- `.git`

------

# 7. Build and Verification

After source changes, build the relevant project whenever the environment permits.

Prefer building the complete solution when changes could affect shared code or multiple applications.

Report separately:

- build errors,
- build warnings,
- test failures.

Do not claim that a build succeeded unless it was actually executed.

Do not claim that interactive UI behavior was tested unless the UI was actually exercised.

If only compilation was verified, say so.

If testing could not be performed, state that explicitly.

------

# 8. Regression Prevention

When fixing a bug, consider whether an automated test is practical.

Tests are particularly valuable for:

- calculations,
- parsing of user input, such as page numbers and selection expressions,
- geometry,
- settings serialization,
- metadata parsing,
- layout calculations independent of the UI.

Do not introduce fragile UI automation merely to claim test coverage.

------

# 9. Lessons Learned and Known Traps

Record durable lessons where they apply: a trap of the solution or of a technology it uses in the root `implementation.md`, a trap of one project in that project's `implementation.md`, and a trap of working in any repository here.

### Visual Studio Build Directories

Visual Studio may recreate `bin`, `obj`, or similar directories immediately after a cleanup script removes them.

The reappearance of those directories does not necessarily mean the cleanup command failed.

### File Locks

Windows COM Surrogate, Explorer preview handlers, Visual Studio, or other processes may temporarily lock files, documents included.

Consider file locking before modifying code to work around what appears to be a filesystem failure.

------

# 10. Things Not to Change Without Asking

Do not make the following changes merely because another architecture seems preferable:

- the UI framework (for example a migration to another one)
- the target framework
- the target architecture
- major dependencies, and the introduction of major frameworks or dependencies
- the solution file format
- the installer technology
- established keyboard shortcuts
- established semantics users rely on, such as page numbering
- persistent settings formats
- existing public file formats
- product and application names
- broad UI redesign

The root `implementation.md` names the current choices.

Ask before making changes with significant architectural impact.

------

# 11. Ending an Agent Session

Before handing work to another agent, update the `HANDOFF.md` of the project you changed. Update the root `HANDOFF.md` only when the work affects shared code, the build, the check projects, the scripts, or more than one project.

The handoff should record:

- current objective,
- work completed,
- exact files changed,
- important implementation decisions,
- build result,
- test result,
- unresolved problems,
- known regressions,
- recommended next step.

Do not turn a `HANDOFF.md` into a chronological development diary.

Remove obsolete information as work progresses so that the document describes the **current state of development**, not the complete history of the project.

Update an `implementation.md` when the work changes a convention, requirement, decision, or implementation detail it records, or adds one that the next agent needs to know: the root one for the solution as a whole, a project's for that project. An `implementation.md` describes how things are and why, not their history.

------

# 12. Committing Changes to git

When working on an assigned task, work in a git branch specific to that task, with a name starting with `claude/`. Commit changes to git when you are done with a task. 

When you commit your changes, collect all the files you worked on and commit them to **local repository only**. **You are allowed to push ==only== if the user explicitly instructs you to.**

When committing your changes to local, identify yourself as Claude@<machineName> with email claude+<machineName>@anthropic.com (or the appropriate Agent name, if you are not Claude). <machineName> is the local Windows machine name.

Do not put the signature 

​    Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>

(or anything similar) in the commit comments themselves.

# 13. Data Access

You must never access network or removable drives without explicit authorization. Only access local hard drives.

# 14. Authority

When instructions conflict, use the following priority:

1. The user's explicit instruction in the current task.
2. The project's `implementation.md`.
3. The root `implementation.md`.
4. This `AGENTS.md`.
5. `HANDOFF.md` files for current-state information.
6. Existing conventions inferred from source.

The current source tree remains authoritative regarding what is actually implemented.

When uncertain about a design decision that could materially affect existing behavior, ask before making the change.
