param(
    [int]$Passes = 1,
    [int]$AgentsPerPass = 5,
    [int]$SongsPerAgent = 2,
    [string]$Source = "D:\Music\Reinier\Heyoka\Gate Code\Heyoka - Alien Gibberish.mp3",
    [string]$SourceFolder = "D:\Music\Reinier\Heyoka",
    [float]$DurationSeconds = 30,
    [int]$Seed = 0,
    [switch]$NewSegmentPerPass,
    [switch]$RandomSourcePerAgent,
    [int]$PlaylistTopCount = 10,
    [string]$CodexCommand = "codex",
    [string]$CodexModel = "gpt-5.3-codex"
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
        [string]$LogPath
    )

    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $LogPath) | Out-Null
    $display = "$FilePath $($ArgumentList -join ' ')"
    Add-Content -LiteralPath $LogPath -Value "[$([DateTimeOffset]::UtcNow.ToString("O"))] $display"
    & $FilePath @ArgumentList *>&1 | Tee-Object -FilePath $LogPath -Append
    if ($LASTEXITCODE -ne 0) {
        throw "Command failed with exit code $LASTEXITCODE`: $display"
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
        }
    }
}

function Wait-CodexJobs {
    param([System.Management.Automation.Job[]]$Jobs)

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

function Assert-SongCandidates {
    param(
        [string]$PatchDirectory,
        [int]$ExpectedCount
    )

    $files = @(Get-ChildItem -LiteralPath $PatchDirectory -Filter "*.aqua" -File -Recurse)
    if ($files.Count -ne $ExpectedCount) {
        throw "Song challenge wrote $($files.Count) .aqua candidates; expected $ExpectedCount."
    }

    foreach ($file in $files) {
        $stem = [IO.Path]::GetFileNameWithoutExtension($file.Name)
        if ($stem -cnotmatch '^[a-z0-9]+([-_][a-z0-9]+)*__[a-z0-9]+([-_][a-z0-9]+)*__[a-z0-9]+([-_][a-z0-9]+)*$') {
            throw "Candidate '$stem' must be <agent-id>__<family>__<hypothesis>.aqua with lowercase slug segments."
        }
    }
}

function Get-SongSources {
    param(
        [string]$Source,
        [string]$SourceFolder
    )

    if ($SourceFolder.Length -gt 0) {
        $files = @(Get-ChildItem -LiteralPath $SourceFolder -File -Recurse |
            Where-Object { $_.Extension -in @(".mp3", ".wav", ".flac", ".aiff", ".aif", ".ogg") } |
            Sort-Object FullName)
        if ($files.Count -eq 0) {
            throw "No supported song files found in '$SourceFolder'."
        }

        return @($files | ForEach-Object { $_.FullName })
    }

    return @($Source)
}

function Write-PassPlaylist {
    param(
        [string]$PassDirectory,
        [int]$TopCount
    )

    $summaryFiles = @(Get-ChildItem -LiteralPath $PassDirectory -Filter "summary.csv" -File -Recurse)
    $rows = @()
    foreach ($summaryFile in $summaryFiles) {
        $batchDirectory = Split-Path -Parent $summaryFile.FullName
        foreach ($row in (Import-Csv -LiteralPath $summaryFile.FullName)) {
            $candidateId = $row.candidate_id
            $candidateWav = Join-Path $batchDirectory (Join-Path "song-candidates" (Join-Path $candidateId "audio/candidate.wav"))
            $referenceWav = Join-Path (Split-Path -Parent $batchDirectory) "reference.wav"
            $rows += [pscustomobject]@{
                CandidateId = $candidateId
                TrialId = $row.trial_id
                ReferenceId = $row.reference_id
                Verdict = $row.verdict
                LogMelCosine = [double]$row.log_mel_cosine
                AudioScore = [double]$row.audio_score
                RmsRatio = [double]$row.rms_ratio
                CentroidRatio = [double]$row.centroid_ratio
                CandidateWav = $candidateWav
                ReferenceWav = $referenceWav
                SummaryPath = $summaryFile.FullName
            }
        }
    }

    $playable = @($rows | Where-Object { Test-Path -LiteralPath $_.CandidateWav })
    $ranked = @($playable | Sort-Object -Property @{ Expression = "LogMelCosine"; Descending = $true }, @{ Expression = "AudioScore"; Descending = $true } | Select-Object -First $TopCount)
    $skipped = @($rows | Where-Object { -not (Test-Path -LiteralPath $_.CandidateWav) })
    $playlistPath = Join-Path $PassDirectory "top-scoring-candidates.m3u"
    $reportPath = Join-Path $PassDirectory "top-scoring-candidates.md"
    $playlist = New-Object System.Collections.Generic.List[string]
    $playlist.Add("#EXTM3U")
    $report = New-Object System.Collections.Generic.List[string]
    $report.Add("# Top Scoring Playable Song Candidates")
    $report.Add("")
    $report.Add(('playlist: `{0}`' -f $playlistPath))
    $report.Add("")

    $rank = 1
    foreach ($row in $ranked) {
        $title = "#{0} {1} cosine={2:0.######} score={3:0.######} verdict={4}" -f $rank, $row.CandidateId, $row.LogMelCosine, $row.AudioScore, $row.Verdict
        $playlist.Add("#EXTINF:-1,$title")
        $playlist.Add($row.CandidateWav)

        $report.Add(('- {0}. `{1}` / verdict `{2}` / cosine `{3:0.######}` / score `{4:0.######}` / rms `{5:0.######}` / centroid `{6:0.######}`' -f $rank, $row.CandidateId, $row.Verdict, $row.LogMelCosine, $row.AudioScore, $row.RmsRatio, $row.CentroidRatio))
        $report.Add(('  - candidate: `{0}`' -f $row.CandidateWav))
        $report.Add(('  - reference: `{0}`' -f $row.ReferenceWav))
        $report.Add(('  - summary: `{0}`' -f $row.SummaryPath))
        $rank++
    }

    if ($skipped.Count -gt 0) {
        $report.Add("")
        $report.Add("## Skipped Non-Playable Candidates")
        foreach ($row in $skipped | Sort-Object -Property @{ Expression = "LogMelCosine"; Descending = $true }, @{ Expression = "AudioScore"; Descending = $true }) {
            $report.Add(('- `{0}` / verdict `{1}` / cosine `{2:0.######}` / score `{3:0.######}` / expected candidate: `{4}`' -f $row.CandidateId, $row.Verdict, $row.LogMelCosine, $row.AudioScore, $row.CandidateWav))
        }
    }

    Set-Content -LiteralPath $playlistPath -Value $playlist
    Set-Content -LiteralPath $reportPath -Value $report
}

$repoRoot = Resolve-RepoRoot
$codex = Get-Command $CodexCommand -ErrorAction Stop
$loopId = New-Timestamp
$loopRoot = Join-Path $repoRoot "artifacts/parity/song-snippet-swarms/$loopId"
$logRoot = Join-Path $loopRoot "logs"
$workerProject = Join-Path $repoRoot "tools/IpaTrialWorker/IpaTrialWorker.csproj"
$storePath = Join-Path $loopRoot "song-trial-results.cc"
$seedValue = if ($Seed -eq 0) { Get-Random -Minimum 1 -Maximum ([int]::MaxValue) } else { $Seed }
$songSources = @(Get-SongSources -Source $Source -SourceFolder $SourceFolder)
New-Item -ItemType Directory -Force -Path $loopRoot, $logRoot | Out-Null

Set-Content -LiteralPath (Join-Path $loopRoot "loop-index.md") -Value @"
# Song Snippet Trial Swarm $loopId

- repo: $repoRoot
- source: $Source
- source_folder: $SourceFolder
- song_sources: $($songSources -join '; ')
- codex: $($codex.Source)
- passes: $Passes
- agents_per_pass: $AgentsPerPass
- songs_per_agent: $SongsPerAgent
- playlist_top_count: $PlaylistTopCount
- seed: $seedValue
- duration_seconds: $DurationSeconds
- new_segment_per_pass: $NewSegmentPerPass
- random_source_per_agent: $RandomSourcePerAgent
- store: $storePath
- worker: $workerProject

"@

$latestDistilledStore = ""
for ($pass = 1; $pass -le $Passes; $pass++) {
    $passId = "pass-{0:000}" -f $pass
    $passDir = Join-Path $loopRoot $passId
    $patchRoot = Join-Path $passDir "proposed-patches"
    $phaseSeed = if ($NewSegmentPerPass) { $seedValue + (($pass - 1) * 7919) } else { $seedValue }
    New-Item -ItemType Directory -Force -Path $passDir, $patchRoot | Out-Null

    $preEvidence = Join-Path $passDir "semantic-search-before.md"
    $evidenceStore = if ($latestDistilledStore.Length -gt 0 -and (Test-Path -LiteralPath $latestDistilledStore)) { $latestDistilledStore } else { $storePath }
    if (Test-Path -LiteralPath $evidenceStore) {
        Invoke-LoggedProcess `
            -FilePath "dotnet" `
            -ArgumentList @(
                "run",
                "--project",
                $workerProject,
                "--",
                "search",
                "--store",
                $evidenceStore,
                "--query",
                "song snippet alien gibberish rhythm tempo timbre spectral envelope modulation scene voices weak promising",
                "--limit",
                "40",
                "--output",
                $preEvidence,
                "--skip-index",
                "true"
            ) `
        -LogPath (Join-Path $logRoot "$passId-search-before.log")
    }
    else {
        Set-Content -LiteralPath $preEvidence -Value "# No prior song-snippet evidence yet"
    }

    $jobs = @()
    for ($agent = 1; $agent -le $AgentsPerPass; $agent++) {
        $agentId = "agent-{0:00}" -f $agent
        $agentDir = Join-Path $passDir $agentId
        $agentPatchDir = Join-Path $patchRoot $agentId
        New-Item -ItemType Directory -Force -Path $agentDir, $agentPatchDir | Out-Null
        $challengePaths = @()
        $challengeReports = @()
        $targetPatchDirs = @()
        $agentSources = @()
        $agentSeeds = @()
        for ($song = 1; $song -le $SongsPerAgent; $song++) {
            $songId = "target-{0:00}" -f $song
            $agentChallengeRoot = Join-Path $agentDir $songId
            $agentChallengePath = Join-Path $agentChallengeRoot "challenge.json"
            $targetPatchDir = Join-Path $agentPatchDir $songId
            New-Item -ItemType Directory -Force -Path $agentChallengeRoot, $targetPatchDir | Out-Null
            $sourceIndex = if ($RandomSourcePerAgent) {
                (($phaseSeed + ($agent * 1543) + ($song * 7919)) % $songSources.Count)
            }
            else {
                (($phaseSeed - $seedValue) / 7919 + $song - 1) % $songSources.Count
            }
            $agentSource = $songSources[[int]$sourceIndex]
            $agentSeed = $phaseSeed + ($agent * 104729) + ($song * 8191)
            Invoke-LoggedProcess `
                -FilePath "dotnet" `
                -ArgumentList @(
                    "run",
                    "--project",
                    $workerProject,
                    "--",
                    "song-prepare",
                    "--source",
                    $agentSource,
                    "--artifact-root",
                    $agentChallengeRoot,
                    "--duration-seconds",
                    ([string]$DurationSeconds),
                    "--seed",
                    ([string]$agentSeed),
                    "--challenge-id",
                    "song-snippet-$loopId-$passId-$agentId-$songId",
                    "--output",
                    $agentChallengePath
                ) `
                -LogPath (Join-Path $logRoot "$passId-$agentId-$songId-challenge-prepare.log")
            $challengePaths += $agentChallengePath
            $challengeReports += (Join-Path $agentChallengeRoot "challenge.md")
            $targetPatchDirs += $targetPatchDir
            $agentSources += $agentSource
            $agentSeeds += $agentSeed
        }
        $challengeList = ($challengePaths | ForEach-Object { "- frozen challenge JSON: $_" }) -join "`n"
        $challengeReportList = ($challengeReports | ForEach-Object { "- human-readable challenge report: $_" }) -join "`n"
        $targetPatchList = ($targetPatchDirs | ForEach-Object { "- target patch output directory: $_" }) -join "`n"
        $agentSourceList = ($agentSources | ForEach-Object { "- assigned source: $_" }) -join "`n"
        $agentSeedList = ($agentSeeds | ForEach-Object { "- target seed: $_" }) -join "`n"
        $outputPath = Join-Path $agentDir "hypothesis-agent.md"
        $promptPath = Join-Path $agentDir "hypothesis-agent.prompt.md"
        $logPath = Join-Path $logRoot "$passId-$agentId-hypothesis-agent.log"
        $prompt = @"
You are an AquaSynth song-snippet parity worker.

The dataset is on trial. Your patches, hypotheses, failures, and evaluator scores become curriculum evidence in CultCache and the vector database. Write useful sample data and reusable reasoning, not just a one-off trick that happens to flatter one metric.

Challenge:
$challengeList
$challengeReportList
- shared prior evidence: $preEvidence
- patch output directory: $agentPatchDir
$targetPatchList
- candidate filename prefix: $agentId
- phase id: $passId
- phase seed: $phaseSeed
$agentSourceList
$agentSeedList
- required duration: $DurationSeconds seconds

Goal:
Write exactly one parseable AquaSynth `.aqua` patch for each frozen $DurationSeconds second reference clip assigned to you. This is scene-audio parity, not IPA articulation. You may use ordinary voices, FM, AM/tremolo, filters, formants, noise layers, acoustic vocal/syrinx primitives, curves, and helper voices.

Generalization:
- You own two target patches, but your report must compare both targets and state what reusable scene/instrument knowledge transfers between them.
- The two patches may share instrument roles, texture roles, control idioms, and DSL conventions, but each patch should fit its own target's tempo/register/spectral evidence.
- Future agents will receive distilled evidence from this trial when asked to zero-shot an audio production request. Make your `hypotheses.md` useful retrieval context: name what transferred, what did not, and what evidence would change your next patch.

Noise and texture:
- Do not model background or recording color as a full-duration raw `voice wave=noise` bed. That produces static white-noise wash and will be treated as broken subtractive synthesis, not scene modeling.
- Use the shaped texture owner for noise roles: `texture name=dust_hat role=dust pattern=x..x step=<beat_seconds/4> gain=.08 sustain=$DurationSeconds`, `texture name=air_wash role=air gain=.035 sustain=$DurationSeconds`, `texture name=codec_bed role=codec gain=.04 sustain=$DurationSeconds`.
- `texture` lowers to a gated noise voice with role-specific bandpass/highpass/lowpass/tremolo defaults and a control curve or pattern gate. Raw `voice wave=noise` is only acceptable for short transients with explicit gates and narrow filters.

Rhythm and tempo:
- The challenge report includes `tempo_bpm`, `beat_seconds`, and `tempo_confidence`, estimated from spectral/RMS onset autocorrelation.
- The challenge report also points to `analysis_report`, `log_mel_spectrogram_csv`, `log_mel_band_stats_csv`, `rms_envelope_csv`, and `rms_envelope_autocorr_csv`; read them before writing the patch.
- Use spectrogram band means, first derivatives, second derivatives, and envelope autocorrelation peaks as evidence for which voices should be steady, pulsed, noisy, bright, or accelerating.
- Use those timing facts to build rhythmic controls.
- Current sequencing syntax is Strudel-ish through ordinary AquaSynth parameter curves:
  - declare a parameter: `param path=/seq/kick default=0 min=0 max=1 step=.001`
  - step a pattern with hold interpolation: `curve name=kick_seq path=/seq/kick points=0:1,0.12:0,0.48:1,0.60:0 interp=hold loop=true`
  - new sugar now exists: `pattern name=kick_seq path=/seq/kick pattern=x..x step=<beat_seconds/4> high=1 low=0`
  - use beat-derived times from `beat_seconds`; eighth notes are `beat_seconds/2`, sixteenths are `beat_seconds/4`.
  - bind parameters into voices or primitives, for example `gain=@/seq/kick`, `tremolo_hz=<tempo_bpm/60>`, `noise=@/seq/hiss`, `freq=@/seq/pitch`.
- Do not invent unimplemented syntax like `s("bd sn")` inside the `.aqua` candidate. You may propose it in `instrument-conventions.md`.

Register and scale:
- The challenge report includes `dominant_hz`, `register_low_hz`, `register_high_hz`, `root_note`, `suggested_scale`, and `scale_frequencies_hz`.
- Songs tend to pick a register and stay there. Treat those bounds as the default melodic playground unless your hypothesis explicitly tests octave spread.
- Current scale sugar is explicit frequency sequencing:
  - declare pitch: `param path=/seq/pitch default=<dominant_hz> min=<register_low_hz> max=<register_high_hz> step=.01 unit=Hz`
  - step the scale: `curve name=lead_scale path=/seq/pitch points=0:<scale0>,0.25:<scale1>,0.5:<scale2>,0.75:<scale3> interp=hold loop=true`
  - new sugar now exists: `scale name=lead_scale path=/seq/pitch freqs=<scale_frequencies_hz> degrees=0,1,3,2 step=<beat_seconds/4>`
  - bind it: `freq=@/seq/pitch`
- Candidate patches may use implemented `pattern` and `scale` sugar or the explicit lowered `param`/`curve` form. Proposed future sugar goes in the report.

Instrument and DSL convention invention:
- Invent at least one reusable instrument role for this segment, such as `alien-lead`, `dust-hat`, `codec-bed`, `rubber-bass`, `vowel-drone`, or another precise name.
- Write $agentDir/instrument-conventions.md with:
  - the proposed instrument role names;
  - the control surfaces each role would want;
  - any Strudel-like sugar that would make the patch shorter;
  - the exact lowering into today's AquaSynth syntax using explicit `voice`, `param`, and `curve` lines.
- The `.aqua` candidate must use today's implemented syntax only: `pattern`, `scale`, `param`, `curve`, and ordinary patch/voice syntax are legal. Proposed future sugar is evidence for the next DSL cut, not magic accepted by the parser.
- Legal oscillator `wave=` values are `sine`, `square`, `saw`, `triangle`, and `noise` only. Use `square` plus filters/envelopes for pulse-like tone; `pulse` is not accepted syntax.

Evidence contract:
- Read every assigned challenge report and $preEvidence.
- Write $agentDir/hypotheses.md with: per-target reference features, shared cross-target invariants, cited analysis artifacts, tempo/rhythm plan, register/scale plan, scene voices, synthesis owners, invented instrument roles, expected metric movement on both targets, known risks.
- Write exactly one `.aqua` file under each target patch output directory named `<agent-id>__<family>__<hypothesis>.aqua`.
- Include at least three scene voices or layers in the patch, such as primary alien-voice/body, rhythmic transient/noise, and background/recording color.
- Background/recording-color layers must use `texture` or an equivalently gated and band-shaped explicit voice; a static full-duration white-noise bed is failed evidence.
- Make each patch duration/gates cover its matching $DurationSeconds second target.

Return a short final summary naming the patch and report.
"@
        $jobs += Start-CodexAgentJob `
            -RepoRoot $repoRoot `
            -Prompt $prompt `
            -PromptPath $promptPath `
            -OutputPath $outputPath `
            -LogPath $logPath `
            -CodexCommand $CodexCommand `
            -CodexModel $CodexModel `
            -JobName "$passId-$agentId"
    }

    Wait-CodexJobs -Jobs $jobs
    Assert-SongCandidates -PatchDirectory $patchRoot -ExpectedCount ($AgentsPerPass * $SongsPerAgent)

    for ($agent = 1; $agent -le $AgentsPerPass; $agent++) {
        $agentId = "agent-{0:00}" -f $agent
        $agentDir = Join-Path $passDir $agentId
        for ($song = 1; $song -le $SongsPerAgent; $song++) {
            $songId = "target-{0:00}" -f $song
            Invoke-LoggedProcess `
                -FilePath "dotnet" `
                -ArgumentList @(
                    "run",
                    "--project",
                    $workerProject,
                    "--",
                    "song-score",
                    "--patch-root",
                    (Join-Path (Join-Path $patchRoot $agentId) $songId),
                    "--challenge",
                    (Join-Path $agentDir "$songId/challenge.json"),
                    "--artifact-root",
                    (Join-Path $agentDir $songId),
                    "--batch-id",
                    "$passId-$agentId-$songId",
                    "--store",
                    $storePath,
                    "--hypothesizer",
                    "external-codex-song-swarm-$passId-$agentId-$songId"
                ) `
                -LogPath (Join-Path $logRoot "$passId-$agentId-$songId-score-candidate.log")
        }
    }

    Write-PassPlaylist -PassDirectory $passDir -TopCount $PlaylistTopCount

    $distilledStore = Join-Path $passDir "song-trial-results.distilled.cc"
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
            (Join-Path $passDir "vector-index-after.md"),
            "--timeout-seconds",
            "300"
        ) `
        -LogPath (Join-Path $logRoot "$passId-index-after.log")

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
            "$passId song snippet current candidates promising weak rhythm tempo timbre spectral scene voices",
            "--limit",
            "40",
            "--output",
            (Join-Path $passDir "semantic-search-after.md"),
            "--skip-index",
            "true"
        ) `
        -LogPath (Join-Path $logRoot "$passId-search-after.log")

    Invoke-LoggedProcess `
        -FilePath "dotnet" `
        -ArgumentList @(
            "run",
            "--project",
            $workerProject,
            "--",
            "distill",
            "--store",
            $storePath,
            "--output-store",
            $distilledStore,
            "--output",
            (Join-Path $passDir "distillation-report.md"),
            "--min-cosine",
            "0.35",
            "--max-results",
            "40"
        ) `
        -LogPath (Join-Path $logRoot "$passId-distill.log")
    $latestDistilledStore = $distilledStore
}

Write-Host "Song snippet trial swarm complete: $loopRoot"
