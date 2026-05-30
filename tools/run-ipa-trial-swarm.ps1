param(
    [int]$Passes = 5,
    [string]$BatchId = "ipa-swarm-five-by-five",
    [string]$CodexCommand = "codex",
    [string]$CodexModel = "gpt-5.3-codex",
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

function Write-Status {
    param(
        [string]$StatusPath,
        [string]$Message
    )

    $line = "[$([DateTimeOffset]::UtcNow.ToString("O"))] $Message"
    Add-Content -LiteralPath $StatusPath -Value $line
    Write-Host $line
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

function Start-CodexAgentJob {
    param(
        [string]$RepoRoot,
        [string]$Prompt,
        [string]$PromptPath,
        [string]$OutputPath,
        [string]$LogPath,
        [string]$CodexCommand,
        [string]$CodexModel,
        [string]$JobName
    )

    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $OutputPath) | Out-Null
    Set-Content -LiteralPath $PromptPath -Value $Prompt

    $args = @(
        "exec",
        "--cd", $RepoRoot,
        "--full-auto",
        "--output-last-message", $OutputPath,
        "--config", 'shell_environment_policy.inherit="all"',
        "--config", 'sandbox_permissions=["workspace-write"]'
    )
    if ($CodexModel.Length -gt 0) {
        $args += @("--model", $CodexModel)
    }
    $args += "-"

    Start-Job -Name $JobName -ArgumentList $CodexCommand, $args, $PromptPath, $LogPath -ScriptBlock {
        param(
            [string]$JobCodexCommand,
            [string[]]$JobArgs,
            [string]$JobPromptPath,
            [string]$JobLogPath
        )

        $ErrorActionPreference = "Continue"
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $JobLogPath) | Out-Null
        Add-Content -LiteralPath $JobLogPath -Value "[$([DateTimeOffset]::UtcNow.ToString("O"))] $JobCodexCommand $($JobArgs -join ' ')"
        Get-Content -LiteralPath $JobPromptPath -Raw | & $JobCodexCommand @JobArgs 2>&1 | Tee-Object -FilePath $JobLogPath -Append
        [pscustomobject]@{
            ExitCode = $LASTEXITCODE
            LogPath = $JobLogPath
            PromptPath = $JobPromptPath
        }
    }
}

function Wait-CodexJobs {
    param(
        [System.Management.Automation.Job[]]$Jobs
    )

    foreach ($job in $Jobs) {
        Wait-Job -Job $job | Out-Null
    }

    $failures = @()
    foreach ($job in $Jobs) {
        $received = Receive-Job -Job $job -Keep
        $exitRecord = $received | Where-Object { $_.PSObject.Properties.Name -contains "ExitCode" } | Select-Object -Last 1
        if ($null -eq $exitRecord -or $exitRecord.ExitCode -ne 0) {
            $failures += $job.Name
        }
    }

    Remove-Job -Job $Jobs -Force
    if ($failures.Count -gt 0) {
        throw "External Codex job(s) failed: $($failures -join ', ')"
    }
}

function Assert-LaneBatch {
    param(
        [string]$PatchDirectory,
        [string[]]$Targets
    )

    $files = @(Get-ChildItem -LiteralPath $PatchDirectory -Filter "*.aqua" -File -Recurse)
    if ($files.Count -ne $Targets.Count) {
        throw "Lane wrote $($files.Count) .aqua candidates under $PatchDirectory; expected $($Targets.Count)."
    }

    $stems = $files | ForEach-Object { [IO.Path]::GetFileNameWithoutExtension($_.Name) }
    foreach ($target in $Targets) {
        $covered = $false
        foreach ($stem in $stems) {
            if ($stem.StartsWith("$target`__", [StringComparison]::OrdinalIgnoreCase)) {
                $covered = $true
                break
            }
        }

        if (-not $covered) {
            throw "Lane did not write a candidate for target '$target'."
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

function Assert-PassBatch {
    param(
        [string]$PatchDirectory
    )

    $targets = @("a", "i", "u", "e", "o", "m", "n", "ng", "l", "r", "s", "z", "f", "v", "th", "p", "b", "t", "d", "k", "mix-a", "mix-m", "mix-s", "mix-p", "mix-u")
    $files = @(Get-ChildItem -LiteralPath $PatchDirectory -Filter "*.aqua" -File -Recurse)
    if ($files.Count -ne 25) {
        throw "Pass wrote $($files.Count) .aqua candidates; expected exactly 25."
    }

    $stems = $files | ForEach-Object { [IO.Path]::GetFileNameWithoutExtension($_.Name) }
    foreach ($target in $targets) {
        if (-not ($stems | Where-Object { $_.StartsWith("$target`__", [StringComparison]::OrdinalIgnoreCase) } | Select-Object -First 1)) {
            throw "Pass did not write a candidate for target '$target'."
        }
    }
}

$repoRoot = Resolve-RepoRoot
$codex = Get-Command $CodexCommand -ErrorAction Stop
$loopId = New-Timestamp
$loopRoot = Join-Path $repoRoot "artifacts/parity/ipa-trial-swarms/$loopId"
$logRoot = Join-Path $loopRoot "logs"
$statusPath = Join-Path $loopRoot "status.log"
$indexPath = Join-Path $loopRoot "loop-index.md"
$storePath = Join-Path $loopRoot "ipa-trial-results.cc"
$workerProject = Join-Path $repoRoot "tools/IpaTrialWorker/IpaTrialWorker.csproj"
$targetSets = @(
    @{ Id = "vowels"; Targets = @("a", "i", "u", "e", "o"); Query = "vowel body tongue lip radiation formant color weak articulation transfer" },
    @{ Id = "nasals-approximants"; Targets = @("m", "n", "ng", "l", "r"); Query = "nasal branch admittance velum approximant liquid transfer evidence" },
    @{ Id = "fricatives"; Targets = @("s", "z", "f", "v", "th"); Query = "fricative place constriction turbulence sibilant labiodental dental contrast articulation" },
    @{ Id = "stops"; Targets = @("p", "b", "t", "d", "k"); Query = "stop closure release reservoir weak plosive contrast primitive timeline" },
    @{ Id = "mixed"; Targets = @("mix-a", "mix-m", "mix-s", "mix-p", "mix-u"); Query = "mixed transfer generalization vowel nasal fricative stop articulation owner primitive" }
)

New-Item -ItemType Directory -Force -Path $loopRoot, $logRoot | Out-Null
Set-Content -LiteralPath $indexPath -Value @"
# IPA Trial Swarm $loopId

- repo: $repoRoot
- codex: $($codex.Source)
- passes: $Passes
- agents_per_pass: 5
- candidates_per_agent: 5
- batch_id: $BatchId
- trial_results_store: $storePath
- worker: $workerProject
- topology: five concurrent lane hypothesizers per pass; one 25-candidate scoring barrier; one evaluator barrier; index after every barrier
- status_log: $statusPath

"@

Write-Status $statusPath "swarm initialized at $loopRoot"

if (-not $SkipLocalTrialRun) {
    Write-Status $statusPath "seed trial run starting"
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
        -LogPath (Join-Path $logRoot "seed-trial-run.log")
    Write-Status $statusPath "seed trial run complete"
}
else {
    Add-Content -LiteralPath $indexPath -Value "- seed: skipped"
    Write-Status $statusPath "seed trial run skipped"
}

for ($pass = 1; $pass -le $Passes; $pass++) {
    $passId = "pass-{0:000}" -f $pass
    $passDir = Join-Path $loopRoot $passId
    $patchRoot = Join-Path $passDir "proposed-patches"
    New-Item -ItemType Directory -Force -Path $passDir, $patchRoot | Out-Null
    Add-Content -LiteralPath $indexPath -Value "- ${passId}: $passDir"
    Write-Status $statusPath "$passId index-before starting"

    Invoke-LoggedProcess `
        -FilePath "dotnet" `
        -ArgumentList @(
            "run",
            "--project",
            $workerProject,
            "--",
            "index",
            "--store",
            $storePath,
            "--output",
            (Join-Path $passDir "vector-index-before.md")
        ) `
        -LogPath (Join-Path $logRoot "$passId-index-before.log")

    $preEvidence = Join-Path $passDir "semantic-search-before.md"
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
            "pass $pass weak promising transfer generalization vowel nasal fricative stop articulation owner source tract radiation gesture primitive dressing",
            "--limit",
            "60",
            "--output",
            $preEvidence,
            "--skip-index",
            "true"
        ) `
        -LogPath (Join-Path $logRoot "$passId-search-before.log")

    Write-Status $statusPath "$passId launching five lane hypothesizers"
    $jobs = @()
    foreach ($targetSet in $targetSets) {
        $laneId = $targetSet.Id
        $laneDir = Join-Path $passDir $laneId
        $lanePatchDir = Join-Path $patchRoot $laneId
        New-Item -ItemType Directory -Force -Path $laneDir, $lanePatchDir | Out-Null
        $targets = [string[]]$targetSet.Targets
        $targetText = $targets -join ", "
        $laneQuery = [string]$targetSet.Query
        $laneOutput = Join-Path $laneDir "hypothesis-agent.md"
        $lanePromptPath = Join-Path $laneDir "hypothesis-agent.prompt.md"
        $laneLog = Join-Path $logRoot "$passId-$laneId-hypothesis-agent.log"
        $lanePrompt = @"
You are one lane in an AquaSynth IPA loss-landscape swarm.

Current pass: $passId
Lane: $laneId
Targets: $targetText
Shared pre-pass evidence: $preEvidence
Shared trial store: $storePath
Lane patch directory: $lanePatchDir

Your job is to write exactly five parseable .aqua candidates, one for each target in this lane. Other lane agents are running at the same time; do not touch their directories, the shared .cc store, source files, or existing artifacts.

Use the worker as your semantic search/show organ:
`$env:DOTNET_CLI_HOME="$laneDir/dotnet-home"; `$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE="1"; `$env:DOTNET_CLI_TELEMETRY_OPTOUT="1"; dotnet run --no-build --no-restore --project tools/IpaTrialWorker/IpaTrialWorker.csproj -- search --store "$storePath" --query "<your evidence question>" --limit 20 --output "$laneDir/search-<topic>.md" --skip-index true
`$env:DOTNET_CLI_HOME="$laneDir/dotnet-home"; `$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE="1"; `$env:DOTNET_CLI_TELEMETRY_OPTOUT="1"; dotnet run --no-build --no-restore --project tools/IpaTrialWorker/IpaTrialWorker.csproj -- show --store "$storePath" --trial-id "<trial id from search>" --output "$laneDir/detail-<candidate>.md"

Required receipts before writing candidates:
1. Open $preEvidence and extract three metric-bearing facts relevant to this lane.
2. Run exactly three lane searches:
   - "$laneQuery"
   - one query for a weak same-class failure;
   - one query for a transferable success or useful contrast from any class.
3. Open exactly two detailed trial records with show: one weak same-class record and one promising/contrast record.
4. If a search/show receipt is missing, create it. If the tool fails, report the failure and do not invent evidence.

Candidate contract:
- Write exactly five .aqua files under $lanePatchDir.
- Names must be <targetId>__<family>__<hypothesis-name>.aqua.
- Target ids must be exactly: $targetText.
- Use one to three family ids, lowercase kebab-case.
- Reuse a family id when the same owner/control/timing idea is being tested across multiple targets.
- Prefer values and gesture contours visible in existing candidates before inventing new syntax.
- Patch candidates may use FM, AM, modulators, envelopes, filters, and extra animated voices, but mark that as dressing unless gesture/articulation/timeline evidence moves too.

Report contract:
Write $laneDir/hypotheses.md with:
- PreEvidence Digest: three metric-bearing facts.
- Retrieval Receipts: exactly three search files and two show files.
- Lane Loss Read: strongest and weakest evidence for $laneId.
- Hypothesis Families: family, owner layer, owner sentence `X owns Y so Z remains true`, contrast pair, concrete metric value, primitive timeline excerpt or `missing`, five micro-sweep perturbations, predicted movement for gesture/logmel/articulation/rms/timeline.
- Evidence Quality Ledger: specificity, comparability, falsifiability, reuse value, weakest missing evidence.
- Candidate Matrix: exactly five rows, one per target.
- Acceptance Checklist: five files, target coverage, naming schema, receipt count.

Return a short final summary naming the files you wrote.
"@
        $jobs += Start-CodexAgentJob `
            -RepoRoot $repoRoot `
            -Prompt $lanePrompt `
            -PromptPath $lanePromptPath `
            -OutputPath $laneOutput `
            -LogPath $laneLog `
            -CodexCommand $CodexCommand `
            -CodexModel $CodexModel `
            -JobName "$passId-$laneId"
    }

    Wait-CodexJobs -Jobs $jobs
    Write-Status $statusPath "$passId lane hypothesizers complete"

    foreach ($targetSet in $targetSets) {
        Assert-LaneBatch -PatchDirectory (Join-Path $patchRoot $targetSet.Id) -Targets ([string[]]$targetSet.Targets)
    }
    Assert-PassBatch -PatchDirectory $patchRoot
    Write-Status $statusPath "$passId candidate assertions passed"

    Invoke-LoggedProcess `
        -FilePath "dotnet" `
        -ArgumentList @(
            "run",
            "--project",
            $workerProject,
            "--",
            "score",
            "--patch-root",
            $patchRoot,
            "--artifact-root",
            $passDir,
            "--batch-id",
            $passId,
            "--store",
            $storePath,
            "--hypothesizer",
            "external-codex-swarm-$passId"
        ) `
        -LogPath (Join-Path $logRoot "$passId-score-candidates.log")
    Write-Status $statusPath "$passId scoring complete"

    Invoke-LoggedProcess `
        -FilePath "dotnet" `
        -ArgumentList @(
            "run",
            "--project",
            $workerProject,
            "--",
            "index",
            "--store",
            $storePath,
            "--output",
            (Join-Path $passDir "vector-index-after.md")
        ) `
        -LogPath (Join-Path $logRoot "$passId-index-after.log")

    $postEvidence = Join-Path $passDir "semantic-search-after.md"
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
            "$passId external-codex-swarm latest candidates weak promising transfer generalization vowel nasal fricative stop articulation owner primitive dressing",
            "--limit",
            "80",
            "--output",
            $postEvidence,
            "--skip-index",
            "true"
        ) `
        -LogPath (Join-Path $logRoot "$passId-search-after.log")

    $evalOutput = Join-Path $passDir "evaluator-agent.md"
    $evalPromptPath = Join-Path $passDir "evaluator-agent.prompt.md"
    $evalLog = Join-Path $logRoot "$passId-evaluator-agent.log"
    $evalPrompt = @"
You are the evaluator barrier for AquaSynth IPA swarm pass $passId.

Read:
- post-pass semantic evidence: $postEvidence
- pass directory: $passDir
- patch root: $patchRoot
- trial store: $storePath

Before judging, use the worker as your search/show organ:
`$env:DOTNET_CLI_HOME="$passDir/dotnet-home"; `$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE="1"; `$env:DOTNET_CLI_TELEMETRY_OPTOUT="1"; dotnet run --no-build --no-restore --project tools/IpaTrialWorker/IpaTrialWorker.csproj -- search --store "$storePath" --query "$passId external-codex-swarm current pass candidates" --limit 40 --output "$passDir/eval-search-current-pass.md" --skip-index true
`$env:DOTNET_CLI_HOME="$passDir/dotnet-home"; `$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE="1"; `$env:DOTNET_CLI_TELEMETRY_OPTOUT="1"; dotnet run --no-build --no-restore --project tools/IpaTrialWorker/IpaTrialWorker.csproj -- search --store "$storePath" --query "$passId weak regression failed articulation stop fricative nasal vowel source tract radiation" --limit 40 --output "$passDir/eval-search-weak.md" --skip-index true
`$env:DOTNET_CLI_HOME="$passDir/dotnet-home"; `$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE="1"; `$env:DOTNET_CLI_TELEMETRY_OPTOUT="1"; dotnet run --no-build --no-restore --project tools/IpaTrialWorker/IpaTrialWorker.csproj -- search --store "$storePath" --query "$passId promising transfer generalization gesture articulation primitive timeline" --limit 40 --output "$passDir/eval-search-promising.md" --skip-index true
`$env:DOTNET_CLI_HOME="$passDir/dotnet-home"; `$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE="1"; `$env:DOTNET_CLI_TELEMETRY_OPTOUT="1"; dotnet run --no-build --no-restore --project tools/IpaTrialWorker/IpaTrialWorker.csproj -- show --store "$storePath" --trial-id "<weak current pass trial id>" --output "$passDir/eval-detail-weak.md"
`$env:DOTNET_CLI_HOME="$passDir/dotnet-home"; `$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE="1"; `$env:DOTNET_CLI_TELEMETRY_OPTOUT="1"; dotnet run --no-build --no-restore --project tools/IpaTrialWorker/IpaTrialWorker.csproj -- show --store "$storePath" --trial-id "<promising current pass trial id>" --output "$passDir/eval-detail-promising.md"

Verify from disk:
- exactly 25 .aqua files under $patchRoot;
- one target each for a, i, u, e, o, m, n, ng, l, r, s, z, f, v, th, p, b, t, d, k, mix-a, mix-m, mix-s, mix-p, mix-u;
- filename schema <targetId>__<family>__<hypothesis-name>.aqua;
- each lane wrote hypotheses.md with receipt lists and evidence quality ledger.

Write $passDir/evaluation.md with:
- Retrieval Receipts: exactly three search outputs plus weak/promising show outputs.
- Commands Run.
- Pass Compliance: passed/failed with file count, target coverage, naming validity, lane receipt coverage, lane report coverage.
- Family Verdicts: canonical filename family, improved/flat/regressed/unknown metrics; use |delta| < 0.01 as flat and `unknown` when show receipts lack deltas.
- Evidence Quality Verdicts: specificity, comparability, falsifiability, reuse value, primitive timeline support, weakest missing evidence.
- Seam Audit: owner sentence, contrast pair, timeline excerpt or missing, planned perturbation count, actionable/not actionable.
- Novelty Audit: new owner/control axis/timing contour/primitive relationship or dressing-only.
- Generalization Read.
- Dressing Audit.
- Next Pass Brief: five concrete search questions and five candidate-family recommendations future lane agents should inherit.
- Next Target: exactly one final bullet `<file-or-module> | <invariant> | <expected metric movement>`.

Do not edit source files or trial artifacts. Treat the .cc store as shared memory and the search/show tool as the way future agents should read it.
"@
    $evalJob = Start-CodexAgentJob `
        -RepoRoot $repoRoot `
        -Prompt $evalPrompt `
        -PromptPath $evalPromptPath `
        -OutputPath $evalOutput `
        -LogPath $evalLog `
        -CodexCommand $CodexCommand `
        -CodexModel $CodexModel `
        -JobName "$passId-evaluator"
    Wait-CodexJobs -Jobs @($evalJob)
    Write-Status $statusPath "$passId evaluator complete"
}

Write-Status $statusPath "swarm complete: $loopRoot"
Write-Host "IPA trial swarm complete: $loopRoot"
