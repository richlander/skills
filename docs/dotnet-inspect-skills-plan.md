# dotnet-inspect Skills Plan

## Background

PR #340 added a single `dotnet-inspect` skill to the dotnet plugin that taught the agent how to use the `dotnet-inspect` CLI tool. After reflection, a tool-shaped skill ("here's how to use this tool") isn't the best approach. It teaches *how* but not *when* — and it doesn't let the agent choose the best tool for the job.

Instead, we're pivoting to **scenario-shaped skills** that start from the developer's problem and let the best tool win. In some cases that's `dotnet-inspect`, in others it's `dotnet` CLI commands, and often it's a combination of both.

## Proposed Skills

Five skills, all under `plugins/dotnet/skills/`, each with its own eval:

### 1. `dotnet-api-migration`

**Trigger:** "Upgrade this package" / "This code uses an old API — fix it"

The developer has code that targets an older version of a package or API and needs to migrate to a newer version. The skill teaches the agent to triage what changed first, then fix systematically.

**Tool mix:**
- `dotnet-inspect diff` — classify breaking vs. additive changes between versions
- `dotnet-inspect member` — explore the new API surface after identifying changes
- `dotnet add package` / `dotnet list package` — update PackageReferences
- Build-test loop — `dotnet build` to verify the migration compiles

**Example prompts:**
- "Upgrade System.CommandLine from the beta to the latest stable version"
- "This project uses an old version of Microsoft.Extensions.AI — update it"
- "What breaking changes happened between these two versions of this package?"
- "Fix the build errors after upgrading to the latest version"

### 2. `dotnet-platform-discovery`

**Trigger:** "What does .NET give me for X?"

The developer knows the *domain* (caching, health checks, authentication, observability) but doesn't know which types, packages, or templates exist across the platform. The skill teaches the agent to survey systematically rather than hallucinate an answer.

**Tool mix:**
- `dotnet-inspect type`, `find`, `implements`, `extensions` — scan platform libraries for types
- `dotnet new list` — find relevant templates
- `dotnet list package` — check what's already referenced vs. what needs a PackageReference
- Package search — find first-party NuGet packages beyond the shared framework

**Example prompts:**
- "What does .NET give me for caching?"
- "I need to add health checks — what's built in?"
- "Map out the dependency injection abstractions"
- "What authentication handlers ship with ASP.NET Core?"
- "I'm building an observability layer — find all the ILogger*, Activity*, Meter* types"

### 3. `dotnet-dependency-analysis`

**Trigger:** "What does this pull in?" / "Why is this assembly in my output?"

The developer wants to understand the dependency graph — what's transitive, what's in the shared framework vs. NuGet-delivered, what the full closure looks like.

**Tool mix:**
- `dotnet-inspect depends` — package dependency trees, type hierarchy, library references
- `dotnet list package --include-transitive` — project-level dependency view
- `dotnet-inspect type`/`member` — drill into specific dependencies to understand what they provide
- Build output inspection — understanding what ends up in `bin/`

**Example prompts:**
- "What does Microsoft.Extensions.AI pull in?"
- "Why is Newtonsoft.Json in my build output?"
- "What's the transitive dependency closure of this package?"
- "Is System.Text.Json in the shared framework or do I need a PackageReference?"
- "Show me the dependency chain from my project to this assembly"

### 4. `dotnet-supply-chain-visibility`

**Trigger:** "What should I know about this package before I depend on it?"

Not a trust verdict — *visibility*. The developer wants to see the facts and make an informed decision.

**Tool mix:**
- `dotnet-inspect package` — metadata (authors, license, signing, repository URL)
- `dotnet-inspect` version commands — `--versions` (history), `--latest-version` (currency)
- `dotnet-inspect depends` — dependency fan-out (am I pulling in 3 packages or 30?)
- NuGet.org data — download counts, deprecation status, vulnerability advisories
- `dotnet list package --vulnerable` / `--deprecated` — project-level checks

**Example prompts:**
- "Is this package actively maintained?"
- "What's the dependency fan-out if I add this?"
- "Who publishes this package? What license?"
- "How many versions has this had? When was the last release?"
- "Are there any known vulnerabilities in my current dependencies?"

### 5. `dotnet-source-exploration`

**Trigger:** "Show me the source code" / "Where is this implemented?"

The developer wants to read the actual source of a .NET type or member — either to understand how it works, debug unexpected behavior, or learn from the implementation. The skill teaches the agent to locate and retrieve source via SourceLink.

**Tool mix:**
- `dotnet-inspect source` — get SourceLink URLs for types and members, with line numbers
- `dotnet-inspect source --cat` — fetch and print source inline (no browser needed)
- `--head N` / `--tail N` — limit output for large files
- `--platform` — locate runtime/ASP.NET Core types (e.g., `source Dictionary --platform System.Collections`)
- `dotnet-inspect member` — identify the right overload before drilling into source

**Example prompts:**
- "Show me how JsonSerializer.Serialize is implemented"
- "Where is HttpClient defined in the .NET runtime?"
- "I want to see the source for Dictionary's resize logic"
- "What does AddHttpClient actually do under the hood?"
- "Show me the source for IHostBuilder in ASP.NET Core"

## Approach

1. Close PR #340 (superseded by this plan, not wasted — it proved out eval patterns and dotnet-inspect usage)
2. Create one PR per skill, each with SKILL.md + eval.yaml
3. Skills reference this plan for context and link back to #340 for prior art

## Reference

- Original PR: #340
- Tool: [dotnet-inspect](https://github.com/pjbgf/dotnet-inspect) (installed as `dnx dotnet-inspect`)
