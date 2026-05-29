param(
    [int]$Rounds = 5,
    [string]$BatchId = "five-seed-trials",
    [string]$CodexCommand = "codex",
    [string]$CodexModel = "gpt-5.3-codex",
    [switch]$AllowCodexSourceEdits,
    [switch]$SkipLocalTrialRun
)

$ErrorActionPreference = "Stop"

function Resolve-RepoRoot {
    $dir = Get-Item -LiteralPath (Get-Location)
    while ($null -ne $dir) {
        if (Test-Path -LiteralPath (Join-Path $dir.FullName "AquaSynth.sln")) {
            return $dir.FullName
        }

        $dir = $dir.Parent
    }

    throw "Could not find AquaSynth.sln above the current directory."
}

function New-Timestamp {
    return [DateTimeOffset]::UtcNow.ToString("yyyyMMddTHHmmssfff")
}

function Invoke-LoggedProcess {
    param(
        [string]$FilePath,
        [string[]]$ArgumentList,
        [string]$LogPath,
        [hashtable]$Environment = @{}
    )

    $logDir = Split-Path -Parent $LogPath
    New-Item -ItemType Directory -Force -Path $logDir | Out-Null
    $oldValues = @{}
    foreach ($key in $Environment.Keys) {
        $oldValues[$key] = [Environment]::GetEnvironmentVariable($key, "Process")
        [Environment]::SetEnvironmentVariable($key, [string]$Environment[$key], "Process")
    }

    try {
        $display = "$FilePath $($ArgumentList -join ' ')"
        Add-Content -LiteralPath $LogPath -Value "[$([DateTimeOffset]::UtcNow.ToString("O"))] $display"
        & $FilePath @ArgumentList *>&1 | Tee-Object -FilePath $LogPath -Append
        if ($LASTEXITCODE -ne 0) {
            throw "Command failed with exit code $LASTEXITCODE`: $display"
        }
    }
    finally {
        foreach ($key in $Environment.Keys) {
            [Environment]::SetEnvironmentVariable($key, $oldValues[$key], "Process")
        }
    }
}

function Invoke-CodexAgent {
    param(
        [string]$RepoRoot,
        [string]$Prompt,
        [string]$OutputPath,
        [string]$LogPath,
        [string]$CodexCommand,
        [string]$CodexModel,
        [bool]$AllowSourceEdits
    )

    $promptPath = [IO.Path]::ChangeExtension($OutputPath, ".prompt.md")
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $OutputPath) | Out-Null
    Set-Content -LiteralPath $promptPath -Value $Prompt

    $args = @(
        "exec",
        "--cd", $RepoRoot,
        "--full-auto",
        "--output-last-message", $OutputPath
    )
    if ($CodexModel.Length -gt 0) {
        $args += @("--model", $CodexModel)
    }
    if (-not $AllowSourceEdits) {
        $args += @(
            "--config", 'shell_environment_policy.inherit="all"',
            "--config", 'sandbox_permissions=["workspace-write"]'
        )
    }
    $args += "-"

    $previousErrorActionPreference = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try {
        Get-Content -LiteralPath $promptPath -Raw | & $CodexCommand @args 2>&1 | Tee-Object -FilePath $LogPath -Append
        $exitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousErrorActionPreference
    }

    if ($exitCode -ne 0) {
        throw "External Codex agent failed with exit code $exitCode. See $LogPath."
    }
}

function Assert-CandidateBatch {
    param(
        [string]$PatchDirectory
    )

    $targetSets = @(
        @("a", "i", "u", "e", "o"),
        @("m", "n", "ng", "l", "r"),
        @("s", "z", "f", "v", "th"),
        @("p", "b", "t", "d", "k"),
        @("mix-a", "mix-m", "mix-s", "mix-p", "mix-u")
    )
    $files = @(Get-ChildItem -LiteralPath $PatchDirectory -Filter "*.aqua" -File -Recurse)
    if ($files.Count -ne 25) {
        throw "Hypothesis worker wrote $($files.Count) .aqua candidates; expected exactly 25."
    }

    $stems = $files | ForEach-Object { [IO.Path]::GetFileNameWithoutExtension($_.Name) }
    foreach ($targetSet in $targetSets) {
        foreach ($target in $targetSet) {
            $covered = $false
            foreach ($stem in $stems) {
                if ($stem.StartsWith("$target`__", [StringComparison]::OrdinalIgnoreCase)) {
                    $covered = $true
                    break
                }
            }

            if (-not $covered) {
                throw "Hypothesis worker did not write a candidate for target '$target'. Candidate files must be named <targetId>__<family>__<hypothesis-name>.aqua."
            }
        }
    }

    foreach ($stem in $stems) {
        $parts = $stem.Split([string[]]@("__"), [StringSplitOptions]::None)
        if ($parts.Count -lt 3 -or [string]::IsNullOrWhiteSpace($parts[1]) -or [string]::IsNullOrWhiteSpace($parts[2])) {
            throw "Candidate '$stem' does not follow <targetId>__<family>__<hypothesis-name>.aqua."
        }

        if ($parts[1] -cnotmatch '^[a-z0-9]+(-[a-z0-9]+)*$') {
            throw "Candidate '$stem' uses invalid family '$($parts[1])'. Family ids must be lowercase kebab-case."
        }
    }
}

$repoRoot = Resolve-RepoRoot
$codex = Get-Command $CodexCommand -ErrorAction Stop
$loopId = New-Timestamp
$loopRoot = Join-Path $repoRoot "artifacts/parity/ipa-trial-loops/$loopId"
$logRoot = Join-Path $loopRoot "logs"
New-Item -ItemType Directory -Force -Path $loopRoot, $logRoot | Out-Null
$storePath = Join-Path $loopRoot "ipa-trial-results.cc"
$workerProject = Join-Path $repoRoot "tools/IpaTrialWorker/IpaTrialWorker.csproj"

$indexPath = Join-Path $loopRoot "loop-index.md"
Set-Content -LiteralPath $indexPath -Value @"
# IPA Trial Loop $loopId

- repo: $repoRoot
- codex: $($codex.Source)
- rounds: $Rounds
- batch_id: $BatchId
- allow_source_edits: $AllowCodexSourceEdits
- trial_results_store: $storePath
- worker: $workerProject
- trial_shape: five rounds, exactly 25 candidates per round, one per phoneme lane

"@

if (-not $SkipLocalTrialRun) {
    $seedLog = Join-Path $logRoot "seed-trial-run.log"
    Invoke-LoggedProcess `
        -FilePath "dotnet" `
        -ArgumentList @(
            "run",
            "--project",
            $workerProject,
            "--",
            "seed",
            "--artifact-root",
            $loopRoot,
            "--batch-id",
            $BatchId,
            "--store",
            $storePath
        ) `
        -LogPath $seedLog
}
else {
    Add-Content -LiteralPath $indexPath -Value "- seed: skipped"
}

for ($round = 1; $round -le $Rounds; $round++) {
    $roundId = "round-{0:000}" -f $round
    $roundDir = Join-Path $loopRoot $roundId
    $patchDir = Join-Path $roundDir "proposed-patches"
    New-Item -ItemType Directory -Force -Path $roundDir | Out-Null
    New-Item -ItemType Directory -Force -Path $patchDir | Out-Null
    Add-Content -LiteralPath $indexPath -Value "- ${roundId}: $roundDir"

    $preEvidence = Join-Path $roundDir "semantic-search-before.md"
    $preDumpLog = Join-Path $logRoot "$roundId-search-before.log"
    Invoke-LoggedProcess `
        -FilePath "dotnet" `
        -ArgumentList @(
            "run",
            "--project",
            $workerProject,
            "--",
            "search",
            "--store",
            $storePath,
            "--query",
            "weak promising generalization vowel nasal fricative stop articulation owner source tract radiation gesture primitive dressing",
            "--limit",
            "40",
            "--output",
            $preEvidence
        ) `
        -LogPath $preDumpLog

    $hypothesisOutput = Join-Path $roundDir "hypothesis-agent.md"
    $hypothesisLog = Join-Path $logRoot "$roundId-hypothesis-agent.log"
    $hypothesisPrompt = @"
You are an AquaSynth IPA loss-landscape hypothesis and patch-writing worker.

Read this semantic search result from the CultCache trial-results database:
$preEvidence

The backing store is:
$storePath

Use the worker as your search organ. Do not ask for crude full dumps unless retrieval fails:
dotnet run --project tools/IpaTrialWorker/IpaTrialWorker.csproj -- search --store "$storePath" --query "<your evidence question>" --limit 20 --output "$roundDir/search-<topic>.md"
dotnet run --project tools/IpaTrialWorker/IpaTrialWorker.csproj -- show --store "$storePath" --trial-id "<trial id from search>" --output "$roundDir/detail-<candidate>.md"

Useful repo surfaces:
- docs/ipa-vocal-tract-roadmap.md
- src/AquaSynth.Core/IpaGestureExperiment.cs
- src/AquaSynth.Core/FaustExport.cs
- src/AquaSynth.Faust/IpaTrialOrchestrator.cs
- artifacts under $loopRoot

Before writing candidates:
0. Open $preEvidence and include a `PreEvidence Digest` section in $roundDir/hypotheses.md with five metric-bearing facts from it.
1. Run at least three targeted semantic searches and save the outputs under ${roundDir}:
   - one for weakest stop/plosive closure evidence;
   - one for best vowel/nasal/fricative transferable successes;
   - one for dressing/FM/AM/noise versus articulation-owner failures.
2. Open at least three detailed trial records with `show`: one weak stop/plosive, one transferable success, and one dressing-vs-articulation case when available.
3. In $roundDir/hypotheses.md, include a `Retrieval Receipts` section listing the exact search/show files you used.
4. If fewer than three search receipts and three show receipts exist on disk, stop and create the missing retrieval artifacts before writing `.aqua` files.

Task:
1. Read the current loss landscape across gesture, log-mel, RMS, articulation, and primitive timeline evidence.
2. Invent the next candidate patches across five five-phoneme target sets:
   vowels: a, i, u, e, o
   nasals/approximants: m, n, ng, l, r
   fricatives: s, z, f, v, th
   stops: p, b, t, d, k
   mixed generalization: mix-a, mix-m, mix-s, mix-p, mix-u
3. Write exactly twenty-five new .aqua candidates under:
$patchDir
   Name each file <targetId>__<family>__<hypothesis-name>.aqua so the scorer can infer the reference target and hypothesis family.
4. Make the candidates test transferable hypotheses, not one-off phoneme golf. Reuse the same contour idea across multiple places/manners when that is the claim being tested.
5. If source edits are allowed, refine the DSL or lowering code only when the evidence says the graph owner is wrong rather than the patch values.
6. Write a concise hypothesis report to:
$roundDir/hypotheses.md

Candidate design contract:
- Produce exactly 25 files: exactly one candidate for each target lane. No extras.
- Use 3-5 named hypothesis families across the 25 files, for example source-carrier, constriction-place, stop-release, nasal-branch, and dressing-control.
- Filename schema is mandatory: <targetId>__<family>__<hypothesis-name>.aqua.
- Family ids must be lowercase kebab-case and reused verbatim in filenames and `Hypothesis Families`.
- The family segment in filenames is the canonical hypothesis-family id used by the evaluator.
- The same family name should appear in filenames across multiple target sets when you are testing generalization.
- A family is transferable only if it appears in at least three target sets; otherwise label it exploratory in the report.
- Keep `.aqua` scripts parseable and self-contained. Prefer modifying values and gesture contours already visible in seed candidates before inventing new syntax.
- Do not use source edits as a way to rescue malformed candidate patches.
- Only edit source if two or more retrieved trials indicate the same owner-layer failure pattern; otherwise `Source Edit Decision: none`.
- If Source edits allowed is False, do not edit any file outside $patchDir and $roundDir/hypotheses.md.
- Before finishing, list $patchDir and verify exact count, target coverage, and filename schema in `Acceptance Checklist`.

Required `$roundDir/hypotheses.md` shape:
- `PreEvidence Digest`: five facts with exact metric keys and values from $preEvidence.
- `Retrieval Receipts`: search/show files used.
- `Loss Landscape Read`: strongest and weakest evidence by family.
- `Hypothesis Families`: 3-5 families, each with owner layer, transfer status, evidence_refs, cited trial_ids, at least one concrete metric key/value from `show`, and predicted metric movement using `gesture:+/-/flat`, `logmel:+/-/flat`, `articulation:+/-/flat`, `rms:+/-/flat`, `timeline:+/-/flat/risk`.
- `Claim Audit`: one row per family: claim -> evidence file(s) -> metric key/value(s) -> predicted movement.
- `Candidate Matrix`: exactly 25 rows with target id, filename, family, expected metric movement, and risk.
- `Source Edit Decision`: `none` unless source edits are allowed and justified by evidence.
- `Acceptance Checklist`: 25 files, one per target id, filename schema, required report sections, >=3 search receipts, >=3 show receipts.

Authority boundary:
- Source edits allowed: $AllowCodexSourceEdits
- Do not rewrite existing trial artifacts or the CultCache store directly.
- The worker process owns measurement and .cc writes; you own candidate invention and optional source refinements.
- Keep gesture_score, clean vocal/audio evidence, and full parity dressing separate.
- Patch candidates may use FM, AM, modulators, envelopes, and extra animated voices, but call out when dressing is compensating for failed articulation.
- Prefer fixing primitive owners over adding audio dressing when the evidence says air is missing.

Return a short final summary naming the files you wrote.
"@
    Invoke-CodexAgent `
        -RepoRoot $repoRoot `
        -Prompt $hypothesisPrompt `
        -OutputPath $hypothesisOutput `
        -LogPath $hypothesisLog `
        -CodexCommand $CodexCommand `
        -CodexModel $CodexModel `
        -AllowSourceEdits:$AllowCodexSourceEdits

    Assert-CandidateBatch -PatchDirectory $patchDir

    $scoreLog = Join-Path $logRoot "$roundId-score-candidates.log"
    Invoke-LoggedProcess `
        -FilePath "dotnet" `
        -ArgumentList @(
            "run",
            "--project",
            $workerProject,
            "--",
            "score",
            "--patch-root",
            $patchDir,
            "--artifact-root",
            $roundDir,
            "--batch-id",
            $roundId,
            "--store",
            $storePath,
            "--hypothesizer",
            "external-codex-$roundId"
        ) `
        -LogPath $scoreLog

    $postEvidence = Join-Path $roundDir "semantic-search-after.md"
    $postDumpLog = Join-Path $logRoot "$roundId-search-after.log"
    Invoke-LoggedProcess `
        -FilePath "dotnet" `
        -ArgumentList @(
            "run",
            "--project",
            $workerProject,
            "--",
            "search",
            "--store",
            $storePath,
            "--query",
            "latest round weak promising transfer generalization vowel nasal fricative stop articulation owner source tract radiation gesture primitive dressing",
            "--limit",
            "60",
            "--output",
            $postEvidence
        ) `
        -LogPath $postDumpLog

    $evaluatorOutput = Join-Path $roundDir "evaluator-agent.md"
    $evaluatorLog = Join-Path $logRoot "$roundId-evaluator-agent.log"
    $evaluatorPrompt = @"
You are an AquaSynth IPA trial science evaluator.

Read the updated semantic retrieval from the trial-results database:
$postEvidence

Also read the hypothesis worker output:
$hypothesisOutput

Round artifacts are under:
$roundDir

Before judging:
1. Run at least three targeted semantic searches into ${roundDir}:
   - current round candidates by hypothesizer id or round id;
   - weak regressions / failed articulation;
   - promising transfer / generalization.
2. Use `show` on at least one promising and one weak candidate from the current round.
3. If the hypothesis worker skipped the required 25-lane matrix or retrieval receipts, mark orchestration/prompt compliance as failed even if some audio metrics improved.

Use the worker as your search organ:
dotnet run --project tools/IpaTrialWorker/IpaTrialWorker.csproj -- search --store "$storePath" --query "$roundId external-codex-$roundId current round candidates" --limit 30 --output "$roundDir/eval-search-current-round.md"
dotnet run --project tools/IpaTrialWorker/IpaTrialWorker.csproj -- search --store "$storePath" --query "weak regression failed articulation stop plosive source tract radiation" --limit 30 --output "$roundDir/eval-search-weak.md"
dotnet run --project tools/IpaTrialWorker/IpaTrialWorker.csproj -- search --store "$storePath" --query "promising transfer generalization vowel nasal fricative articulation" --limit 30 --output "$roundDir/eval-search-promising.md"
dotnet run --project tools/IpaTrialWorker/IpaTrialWorker.csproj -- show --store "$storePath" --trial-id "<trial id from current round search>" --output "$roundDir/eval-detail-<candidate>.md"

Verify compliance from disk as well as the hypothesis report:
- count .aqua files under $patchDir;
- validate exactly 25 files;
- validate one target each for a, i, u, e, o, m, n, ng, l, r, s, z, f, v, th, p, b, t, d, k, mix-a, mix-m, mix-s, mix-p, mix-u;
- validate filename schema <targetId>__<family>__<hypothesis-name>.aqua.
- validate family ids are lowercase kebab-case.
- derive canonical family ids only from the filename family segment; ignore prose family names when they disagree.
- If any required section, receipt class, per-target validation, filename validation, or family extraction is incomplete, set `Round Compliance: failed`.

Task:
1. Evaluate whether the worker's proposed hypotheses follow from the measured evidence.
2. Call out any hypothesis that is likely full-patch dressing impersonating articulation.
3. Recommend exactly one next implementation/refinement target, with file/module owner and why.
4. Write your evaluator report to:
$roundDir/evaluation.md

Required `$roundDir/evaluation.md` shape:
- `Retrieval Receipts`: exactly five receipts: three search outputs plus one weak show output and one promising show output. Cite no other evidence files in this section. Only cite claims backed by these file paths; write `unknown` when evidence is absent.
- `Commands Run`: exact search/show commands and output paths used to support claims.
- `Round Compliance`: passed/failed, candidate count, target coverage, naming validity, family extraction, and whether the report had the required matrix.
- `Family Verdicts`: one row per canonical filename family with improved/flat/regressed/unknown metrics. Treat |delta| < 0.01 as flat; if deltas are not present in cited show artifacts, write `unknown` and reason=`no delta in receipts`.
- `Generalization Read`: whether effects transferred across target sets or overfit one phoneme.
- `Dressing Audit`: articulation evidence requires movement in gesture/articulation/primitive timeline metrics; FM/AM/noise/envelope evidence without those is dressing.
- `Next Target`: exactly one markdown bullet, final line of the file, and no continuation text: `<file-or-module> | <invariant> | <expected metric movement>`.

Authority boundary:
- Do not edit source files.
- Do not rewrite trial artifacts.
- Be explicit about which layer owns the failure: gesture DSL, primitive source, tract/radiation lowering, audio scoring, or orchestration.
- Treat the CultCache .cc store as the shared trial memory that future hypothesis workers will query through the worker search/show commands.

Return a short final summary naming the report you wrote.
"@
    Invoke-CodexAgent `
        -RepoRoot $repoRoot `
        -Prompt $evaluatorPrompt `
        -OutputPath $evaluatorOutput `
        -LogPath $evaluatorLog `
        -CodexCommand $CodexCommand `
        -CodexModel $CodexModel `
        -AllowSourceEdits:$false
}

Write-Host "IPA trial loop complete: $loopRoot"
