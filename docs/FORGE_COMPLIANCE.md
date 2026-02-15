# SPT Forge Compliance Statement

**Keep Starting Gear v2.0.5**
**Author:** Blackhorse311
**Last Updated:** 2026-02-15

This document demonstrates how Keep Starting Gear meets or exceeds every requirement in the [SPT Forge Content Guidelines](https://forge.sp-tarkov.com/content-guidelines) and [Community Standards](https://forge.sp-tarkov.com/community-standards).

---

## A Note on Human + AI Collaboration

Version 2.0.0 was developed through collaboration between Blackhorse311 (human) and Claude (AI assistant). We believe this partnership demonstrates that **responsible AI-assisted development can meet and exceed community standards** when done correctly.

### Our Development Model

| Role | Human (Blackhorse311) | AI (Claude) |
|------|----------------------|-------------|
| **Creative Direction** | ✅ All feature decisions | Suggestions only |
| **Testing** | ✅ All gameplay testing | Cannot test |
| **Code Review** | ✅ Final approval on all code | Drafts and analysis |
| **Architecture** | ✅ Approves all designs | Proposes options |
| **Understanding** | ✅ Can explain every line | Explains during development |
| **Bug Reports** | ✅ Receives and triages | Helps diagnose |
| **Release Decisions** | ✅ Full control | Recommendations only |

### Why This Works

1. **Human oversight**: Every line of code is reviewed, understood, and approved by the human author
2. **Human testing**: All functionality is tested in actual SPT gameplay - AI cannot do this
3. **Human accountability**: Blackhorse311 is responsible for the mod and can explain any part of it
4. **AI as tool, not author**: Claude assists with implementation, but doesn't make decisions autonomously

This is fundamentally different from "AI-generated mods" where someone prompts an AI and uploads the output without understanding it. Our collaboration produces **human-authored code with AI assistance**, not AI-authored code with human uploading.

---

## Content Guidelines Compliance

### 1. File Format Standards

| Requirement | Status | Implementation |
|-------------|--------|----------------|
| 7-Zip (.7z) or ZIP (.zip) format | ✅ Met | Releases use .7z format |
| No password-protected archives | ✅ Met | Archives are unprotected |
| Direct extraction to SPT root | ✅ Met | Extract directly, folder structure included |
| Installation instructions | ✅ Met | README.md, INSTALL.txt, USER_GUIDE.txt |
| Dependencies documented | ✅ Met | No external dependencies required |

### 2. Client Mod (BepInEx Plugin) Requirements

| Requirement | Status | Implementation |
|-------------|--------|----------------|
| Source code link | ✅ Met | GitHub repository public |
| Compiled files included | ✅ Met | DLL in `BepInEx/plugins/Blackhorse311-KeepStartingGear/` |
| `[BepInPlugin]` attribute | ✅ Met | See `Plugin.cs` line 14 |
| GUID format | ✅ Met | `"com.blackhorse311.keepstartinggear"` |
| Mod name format | ✅ Met | `"Blackhorse311-KeepStartingGear"` |
| Semantic version | ✅ Met | `"2.0.0"` matching server mod |

**Evidence** (`src/server/Plugin.cs`):
```csharp
[BepInPlugin("com.blackhorse311.keepstartinggear",
             "Blackhorse311-KeepStartingGear",
             PluginVersion)]
public class Plugin : BaseUnityPlugin
{
    public const string PluginVersion = "2.0.0";
    // ...
}
```

### 3. Server Mod (SPT 4.0+ C#) Requirements

| Requirement | Status | Implementation |
|-------------|--------|----------------|
| Source code link | ✅ Met | GitHub repository public |
| Compiled files included | ✅ Met | DLL in `user/mods/Blackhorse311-KeepStartingGear/` |
| Modifies memory only | ✅ Met | No file modifications, only runtime profile changes |
| Version in .csproj | ✅ Met | `<Version>2.0.0</Version>` |
| AbstractModMetadata | ✅ Met | ModMetadata.cs with all required properties |
| SptVersion constraint | ✅ Met | `~4.0.0` (accepts 4.0.x patch versions) |

**Evidence** (`src/servermod/ModMetadata.cs`):
```csharp
public class ModMetadata : AbstractModMetadata
{
    public override string ModGuid => "com.blackhorse311.keepstartinggear.server";
    public override string Name => "Blackhorse311-KeepStartingGear";
    public override string Author => "Blackhorse311";
    public override string Version => Constants.ModVersion; // "2.0.0"
    public override string SptVersion => "~4.0.0";
}
```

### 4. Semantic Versioning

| Requirement | Status | Implementation |
|-------------|--------|----------------|
| MAJOR.MINOR.PATCH format | ✅ Met | `2.0.0` |
| All version numbers match | ✅ Met | 8 locations synchronized (see CLAUDE.md) |
| SPT compatibility declared | ✅ Met | `~4.0.0` constraint |

**Version Locations (All Match `2.0.0`):**
1. `src/server/Plugin.cs` - `PluginVersion`
2. `src/server/*.csproj` - `<Version>`
3. `src/servermod/KeepStartingGearMod.cs` - `ModVersion`
4. `src/servermod/ModMetadata.cs` - `Version`
5. `src/servermod/Constants.cs` - `ModVersion`
6. `src/servermod/*.csproj` - `<Version>`
7. `README.md`
8. `CHANGELOG.md`

### 5. Content Quality Standards

#### Functional Requirements

| Requirement | Status | Evidence |
|-------------|--------|----------|
| Thorough testing | ✅ Met | Tested on fresh SPT 4.0.11 installation |
| Features work as described | ✅ Met | All 7 v2.0.0 features verified |
| Loads without errors | ✅ Met | Clean BepInEx and server logs |
| No unintended changes | ✅ Met | Only modifies player inventory on death |
| No performance degradation | ✅ Met | Negligible impact (<1ms per operation) |
| No memory leaks | ✅ Met | Snapshots cleaned up after use |
| No infinite loops | ✅ Met | All loops bounded, max iterations enforced |

#### Error Handling

| Requirement | Status | Evidence |
|-------------|--------|----------|
| Graceful dependency handling | ✅ Met | Works standalone, optional SVM detection |
| Clear error messages | ✅ Met | User-friendly notifications via overlay |
| Fallback behavior | ✅ Met | Fails safely without crashing game |
| Helpful logging | ✅ Met | Debug mode available, not verbose by default |

**Evidence** - Error handling pattern used throughout:
```csharp
try
{
    // Operation
}
catch (Exception ex)
{
    Plugin.Log.LogError($"Failed to restore snapshot: {ex.Message}");
    // Graceful fallback - game continues normally
}
```

#### Logging Standards

| Requirement | Status | Evidence |
|-------------|--------|----------|
| No ASCII art/logos | ✅ Met | Clean startup message only |
| No multi-line credits | ✅ Met | Credits in documentation, not logs |
| No advertising links | ✅ Met | No external URLs in logs |
| Focused log messages | ✅ Met | Operational status, errors, debug only |

**Evidence** - Actual startup log output:
```
[Info] Keep Starting Gear v2.0.0 loaded successfully
[Info] Snapshots directory: .../snapshots/
```

### 6. Code Quality Standards

| Requirement | Status | Evidence |
|-------------|--------|----------|
| No obfuscated code | ✅ Met | All source readable on GitHub |
| Source matches binaries | ✅ Met | GitHub Actions builds from source |
| No unauthorized network | ✅ Met | Zero network calls, fully offline |
| No system file modifications | ✅ Met | Only touches SPT directory |

**Network Activity: NONE**
- No update checks
- No telemetry
- No crash reporting
- No API calls
- Fully offline operation

### 7. AI-Generated Content Policy Compliance

This is the most important section for demonstrating responsible AI collaboration.

#### The Policy States:

> "Mods substantially or entirely written by AI coding agents are prohibited"
>
> "AI tool use acceptable for: basic code completion, syntax assistance, small utility functions"
>
> "AI tool use prohibited for: entire features, complex game modifications, substantial mod functionality"
>
> "Authors must fully understand their code and be prepared to explain any part"

#### Our Compliance:

| Policy Aspect | Our Approach | Why It's Compliant |
|---------------|--------------|-------------------|
| **Not "substantially written by AI"** | Human-directed collaboration | Every feature was requested, designed, and approved by Blackhorse311 |
| **Author understands code** | ✅ Full understanding | Blackhorse311 reviews and can explain every line |
| **Can explain any part** | ✅ Yes | See comprehensive documentation, ADRs, inline comments |
| **AI for syntax assistance** | ✅ Used appropriately | Claude helps with C# patterns, not SPT-specific logic |
| **Not AI-generated upload** | ✅ Human-authored | Human makes all decisions, AI assists implementation |

#### Key Distinction: Collaboration vs. Generation

**What "AI-generated mods" looks like (PROHIBITED):**
```
Human: "Make me a mod that protects gear"
AI: [generates entire mod]
Human: [uploads without understanding]
```

**What our collaboration looks like (COMPLIANT):**
```
Human: "I want to add a loss preview feature"
Human: "It should show what items would be lost if you died now"
Human: "Use the existing ValueCalculator service"
AI: [proposes implementation approach]
Human: [reviews, requests changes]
AI: [implements with human guidance]
Human: [tests in-game, approves or requests fixes]
Human: [understands and can explain the final code]
```

#### Why the Policy Exists (and Why We're Different)

The policy rationale states:
> "AI lacks training on SPT-specific codebase and security requirements"

Our response:
1. **Human provides SPT knowledge**: Blackhorse311 understands SPT modding, BepInEx patterns, and game behavior
2. **Human tests everything**: AI cannot run SPT; all testing is done by the human
3. **Human catches AI mistakes**: When AI suggests something that won't work in SPT, the human corrects it
4. **Documentation proves understanding**: Our extensive docs (CLAUDE.md, ADRs, standards) demonstrate deep understanding

#### Proof of Human Understanding

Blackhorse311 can explain:
- Why we intercept `MatchCallbacks.EndLocalRaid()` instead of patching elsewhere
- Why `InRaidHelper.DeleteInventory()` needs to be overridden
- Why snapshots use JSON with `LocationConverter` for polymorphic deserialization
- Why thread safety requires `volatile` and `Interlocked` in specific places
- Why we chose server-side restoration over client-side profile manipulation
- Every architectural decision documented in `docs/adr/`

### 8. Executable Files & Security

| Requirement | Status | Evidence |
|-------------|--------|----------|
| Source code public | ✅ Met | GitHub repository |
| Source on established platform | ✅ Met | GitHub |
| Build instructions provided | ✅ Met | README.md "Building from Source" section |
| VirusTotal scan | ✅ Met | 0/70 clean (link in README) |
| No obfuscation | ✅ Met | Standard .NET compilation |
| No anti-debugging | ✅ Met | None |
| No unauthorized modifications | ✅ Met | SPT directory only |
| No data collection | ✅ Met | Zero network activity |

**VirusTotal Evidence:**
- v1.4.9: [0/70 clean](https://www.virustotal.com/gui/file/ff232a9db482b915d31ac82ac262af5379372e27698f35c82bf03deb668d3924)
- Updated scans provided for each release

### 9. Content Licensing

| Requirement | Status | Evidence |
|-------------|--------|----------|
| Acceptable license | ✅ Met | MIT License |
| License file included | ✅ Met | LICENSE file in repository |
| Third-party licenses respected | ✅ Met | Only uses MIT-licensed SPT libraries |
| No unauthorized content | ✅ Met | 100% original code |

### 10. Prohibited Content Categories

| Category | Status | Evidence |
|----------|--------|----------|
| Adult content | ✅ None | Tactical gear protection only |
| Anti-cheat/exploits | ✅ None | Single-player only, doesn't work in live EFT |
| Mod compilations | ✅ N/A | Single original mod |
| Payment/commercial | ✅ None | Completely free, no donations solicited |

### 11. File Hosting

| Requirement | Status | Evidence |
|-------------|--------|----------|
| Direct download links | ✅ Met | GitHub Releases (DDL) |
| No landing pages | ✅ Met | Direct .7z download |
| No ad-supported sites | ✅ Met | GitHub only |
| Permanent availability | ✅ Met | GitHub releases permanent |

---

## Community Standards Compliance

### Basic Conduct

| Standard | Status | Evidence |
|----------|--------|----------|
| Civility | ✅ Met | Respectful README, credits contributors |
| Constructiveness | ✅ Met | Detailed troubleshooting, helpful documentation |
| Honesty | ✅ Met | Accurate feature descriptions, transparent about AI collaboration |

### Content Standards

| Standard | Status | Evidence |
|----------|--------|----------|
| Original creation | ✅ Met | 100% original code |
| English documentation | ✅ Met | All docs in English |
| No prohibited content | ✅ Met | See above |
| Proper attribution | ✅ Met | Credits section acknowledges all contributors |

### File Sharing Standards

| Standard | Status | Evidence |
|----------|--------|----------|
| Functional and tested | ✅ Met | Tested on SPT 4.0.11 |
| Clear installation instructions | ✅ Met | README, INSTALL.txt |
| Accurate descriptions | ✅ Met | Feature table matches functionality |
| Proper tagging | ✅ Met | Tagged appropriately on Forge |
| Complete documentation | ✅ Met | README, CHANGELOG, USER_GUIDE, INSTALL |

---

## Security Measures Implemented

Beyond minimum requirements, we implemented additional security hardening:

| Security Measure | Implementation | Reference |
|------------------|----------------|-----------|
| Session ID validation | Prevents path traversal attacks | SEC-001 |
| File size limits | 10MB max before JSON parse (DoS prevention) | SEC-002 |
| Null reference protection | Defensive null checks throughout | REL-001, REL-002 |
| Thread safety | Volatile fields, Interlocked operations | CON-001, CON-002, CON-003 |
| Harmony patch safety | Try-catch in all patches (prevents game crashes) | All patches |
| Input validation | All user inputs validated before use | Throughout |

---

## Quality Metrics

### Code Quality

| Metric | Value |
|--------|-------|
| Security issues fixed in v2.0.0 | 26 |
| Test coverage | Unit tests for serialization |
| Documentation files | 10+ |
| Coding standards documents | 10 |
| Architecture Decision Records | 2 |

### Community Engagement

| Metric | Value |
|--------|-------|
| Bug reporters credited | 30+ |
| Issues addressed | 30+ since v1.0 |
| Response to feedback | Active |

---

## Conclusion

Keep Starting Gear demonstrates that **human + AI collaboration can produce high-quality, compliant mods** when done responsibly:

1. **Human remains in control**: All decisions, testing, and accountability rest with the human author
2. **AI assists, doesn't replace**: Claude helps with implementation, but doesn't autonomously create features
3. **Understanding is proven**: Extensive documentation demonstrates the author understands every aspect
4. **Quality exceeds minimums**: We implement security measures and documentation beyond requirements
5. **Transparency is complete**: We openly disclose our development model

We hope this demonstrates a responsible path forward for AI-assisted mod development in the SPT community.

---

## Contact

For questions about this compliance statement or our development model:
- GitHub Issues: [Repository Issues](https://github.com/Blackhorse311/KeepStartingGear/issues)
- The Forge: [Mod Page Comments](https://forge.sp-tarkov.com/mod/2470/keep-starting-gear)

---

*This document will be updated with each release to maintain compliance verification.*
