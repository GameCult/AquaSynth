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
    if ($files.Count -lt 25) {
        throw "Hypothesis worker wrote $($files.Count) .aqua candidates; expected at least 25."
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
                throw "Hypothesis worker did not write a candidate for target '$target'. Candidate files must be named <targetId>__<hypothesis-name>.aqua."
            }
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
- trial_shape: five rounds, each asking for five five-phoneme target-set candidates

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

Task:
1. Read the current loss landscape across gesture, log-mel, RMS, articulation, and primitive timeline evidence.
2. Invent the next candidate patches across five five-phoneme target sets. Cover at least one target from each set, and prefer all 25 when the evidence points to reusable changes:
   vowels: a, i, u, e, o
   nasals/approximants: m, n, ng, l, r
   fricatives: s, z, f, v, th
   stops: p, b, t, d, k
   mixed generalization: mix-a, mix-m, mix-s, mix-p, mix-u
3. Write at least twenty-five new .aqua candidates under:
$patchDir
   Name each file <targetId>__<hypothesis-name>.aqua so the scorer can infer the reference target.
4. Make the candidates test transferable hypotheses, not one-off phoneme golf. Reuse the same contour idea across multiple places/manners when that is the claim being tested.
5. If source edits are allowed, refine the DSL or lowering code only when the evidence says the graph owner is wrong rather than the patch values.
6. Write a concise hypothesis report to:
$roundDir/hypotheses.md

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

Task:
1. Evaluate whether the worker's proposed hypotheses follow from the measured evidence.
2. Call out any hypothesis that is likely full-patch dressing impersonating articulation.
3. Recommend exactly one next implementation/refinement target, with file/module owner and why.
4. Write your evaluator report to:
$roundDir/evaluation.md

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
