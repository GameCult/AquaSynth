param(
    [int]$Passes = 1,
    [int]$AgentsPerPass = 5,
    [int]$SongsPerAgent = 2,
    [string]$Source = "D:\Music\Reinier\Heyoka\Gate Code\Heyoka - Alien Gibberish.mp3",
    [string]$SourceFolder = "D:\Music\Reinier\Heyoka",
    [float]$DurationSeconds = 30,
    [switch]$FullSongs,
    [int]$IterationsPerTarget = 5,
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
        "--add-dir", (Join-Path (Split-Path -Parent $RepoRoot) "CultLib")
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
            Where-Object { $_.Extension -in @(".mp3", ".wav", ".flac", ".aiff", ".aif", ".ogg", ".m4a", ".mp4") } |
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
            $motionCoverage = if ([string]::IsNullOrWhiteSpace($row.candidate_motion_coverage)) { 0 } else { [double]$row.candidate_motion_coverage }
            $firstSecondEnergyShare = if ([string]::IsNullOrWhiteSpace($row.candidate_first_second_energy_share)) { 1 } else { [double]$row.candidate_first_second_energy_share }
            $modeCollapseRisk = if ([string]::IsNullOrWhiteSpace($row.mode_collapse_risk)) { 1 } else { [double]$row.mode_collapse_risk }
            $rows += [pscustomobject]@{
                CandidateId = $candidateId
                TrialId = $row.trial_id
                ReferenceId = $row.reference_id
                Verdict = $row.verdict
                LogMelCosine = [double]$row.log_mel_cosine
                AudioScore = [double]$row.audio_score
                RmsRatio = [double]$row.rms_ratio
                CentroidRatio = [double]$row.centroid_ratio
                MotionCoverage = $motionCoverage
                FirstSecondEnergyShare = $firstSecondEnergyShare
                ModeCollapseRisk = $modeCollapseRisk
                CandidateWav = $candidateWav
                ReferenceWav = $referenceWav
                SummaryPath = $summaryFile.FullName
            }
        }
    }

    $playable = @($rows | Where-Object { Test-Path -LiteralPath $_.CandidateWav })
    $ranked = @($playable | Sort-Object -Property @{ Expression = "ModeCollapseRisk"; Descending = $false }, @{ Expression = "MotionCoverage"; Descending = $true }, @{ Expression = "LogMelCosine"; Descending = $true }, @{ Expression = "AudioScore"; Descending = $true } | Select-Object -First $TopCount)
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
        $title = "#{0} {1} collapse={2:0.######} motion={3:0.######} cosine={4:0.######} score={5:0.######} verdict={6}" -f $rank, $row.CandidateId, $row.ModeCollapseRisk, $row.MotionCoverage, $row.LogMelCosine, $row.AudioScore, $row.Verdict
        $playlist.Add("#EXTINF:-1,$title")
        $playlist.Add($row.CandidateWav)

        $report.Add(('- {0}. `{1}` / verdict `{2}` / collapse `{3:0.######}` / motion `{4:0.######}` / first-second `{5:0.######}` / cosine `{6:0.######}` / score `{7:0.######}` / rms `{8:0.######}` / centroid `{9:0.######}`' -f $rank, $row.CandidateId, $row.Verdict, $row.ModeCollapseRisk, $row.MotionCoverage, $row.FirstSecondEnergyShare, $row.LogMelCosine, $row.AudioScore, $row.RmsRatio, $row.CentroidRatio))
        $report.Add(('  - candidate: `{0}`' -f $row.CandidateWav))
        $report.Add(('  - reference: `{0}`' -f $row.ReferenceWav))
        $report.Add(('  - summary: `{0}`' -f $row.SummaryPath))
        $rank++
    }

    if ($skipped.Count -gt 0) {
        $report.Add("")
        $report.Add("## Skipped Non-Playable Candidates")
        foreach ($row in $skipped | Sort-Object -Property @{ Expression = "ModeCollapseRisk"; Descending = $false }, @{ Expression = "MotionCoverage"; Descending = $true }, @{ Expression = "LogMelCosine"; Descending = $true }, @{ Expression = "AudioScore"; Descending = $true }) {
            $report.Add(('- `{0}` / verdict `{1}` / collapse `{2:0.######}` / motion `{3:0.######}` / cosine `{4:0.######}` / score `{5:0.######}` / expected candidate: `{6}`' -f $row.CandidateId, $row.Verdict, $row.ModeCollapseRisk, $row.MotionCoverage, $row.LogMelCosine, $row.AudioScore, $row.CandidateWav))
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
- iterations_per_target: $IterationsPerTarget
- playlist_top_count: $PlaylistTopCount
- seed: $seedValue
- duration_seconds: $DurationSeconds
- full_songs: $FullSongs
- new_segment_per_pass: $NewSegmentPerPass
- random_source_per_agent: $RandomSourcePerAgent
- store: $storePath
- worker: $workerProject

"@

$latestDistilledStore = ""
$latestMusicKnowledgeStore = ""
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

    $preMusicEvidence = Join-Path $passDir "music-knowledge-before.md"
    if ($latestMusicKnowledgeStore.Length -gt 0 -and (Test-Path -LiteralPath $latestMusicKnowledgeStore)) {
        Invoke-LoggedProcess `
            -FilePath "dotnet" `
            -ArgumentList @(
                "run",
                "--project",
                $workerProject,
                "--",
                "music-search",
                "--store",
                $latestMusicKnowledgeStore,
                "--query",
                "syrinx voice subtractive drums additive pad texture reusable abstraction music production quality standard",
                "--limit",
                "40",
                "--output",
                $preMusicEvidence
            ) `
        -LogPath (Join-Path $logRoot "$passId-music-search-before.log")
    }
    else {
        Set-Content -LiteralPath $preMusicEvidence -Value "# No prior music-production knowledge store yet"
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
            $durationArg = if ($FullSongs) { "0" } else { [string]$DurationSeconds }
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
                    $durationArg,
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
        $durationLabel = if ($FullSongs) { "the full decoded song duration from each challenge report" } else { "$DurationSeconds seconds" }
        $targetKind = if ($FullSongs) { "full-song" } else { "$DurationSeconds second" }
        $iterationRoot = Join-Path $agentDir "iterations"
        $outputPath = Join-Path $agentDir "hypothesis-agent.md"
        $promptPath = Join-Path $agentDir "hypothesis-agent.prompt.md"
        $logPath = Join-Path $logRoot "$passId-$agentId-hypothesis-agent.log"
        $prompt = @"
You are an AquaSynth producer-apprenticeship worker.

The dataset is on trial. Your patches, producer briefs, listening journals, failures, and evaluator scores become curriculum evidence in CultCache and the vector database. Write useful studio knowledge and reusable reasoning, not just a one-off trick that happens to flatter one metric.

Challenge:
$challengeList
$challengeReportList
- shared prior evidence: $preEvidence
- shared music-production knowledge: $preMusicEvidence
- patch output directory: $agentPatchDir
$targetPatchList
- candidate filename prefix: $agentId
- phase id: $passId
- phase seed: $phaseSeed
- private iteration root: $iterationRoot
- required self-iteration attempts per target: $IterationsPerTarget
$agentSourceList
$agentSeedList
- required duration: $durationLabel

Goal:
Write exactly one parseable AquaSynth `.aqua` patch for each frozen $targetKind reference assigned to you. This is scene-audio parity and producer apprenticeship, not IPA articulation. You may use ordinary voices, FM, AM/tremolo, filters, formants, noise layers, acoustic vocal/syrinx primitives, additive/PAD layers, curves, and helper voices.

Composition objective:
- The previous corpus learned useful sound-production roles but collapsed into short phrases plus texture. Your job is now composition parity: declare meter, tonal center or progression, instrument lanes, section events, automation, and mix motion before polishing timbre.
- Every final patch must include a composition spine using today's implemented sugar: `meter`, `sequence`, `chords` or `scale`, and `mix` where appropriate. These lower to ordinary `param` and `curve` owners; they are not hidden magic.
- Treat the target as arranged music across time. There should be distinct musical events or motif mutations after the first second, distributed across the full assigned duration. For 10-second clips, seconds 2, 4, 6, and 8 are useful checkpoints; for 30-second or full-song targets, use section entrances, drops, fills, swells, or mix moves across the beginning, middle, and ending.
- Do not submit the stock attractor: a voiced burst at the start, a copied four-lane kick/snare/hat loop, then textured noise. If the reference really demands that shape, cite target artifacts and listening evidence proving it.

Generalization:
- You own $SongsPerAgent target patches, and your report must compare all assigned targets and state what reusable scene/instrument knowledge transfers between them.
- The patches may share instrument roles, texture roles, control idioms, and DSL conventions, but each patch should fit its own target's tempo/register/spectral evidence.
- Future agents will receive distilled evidence from this trial when asked to zero-shot an audio production request. Make your studio reports useful retrieval context: name what transferred, what did not, what sounded alive, what sounded fake, and what evidence would change your next patch.

Producer evidence:
- Write $agentDir/producer-brief.md before patching. For each target, include the artistic reading, likely genre/tempo feel, emotional/energy contour, section map, instrument role map, mix priorities, and the exact challenge artifacts you are trusting.
- Write $agentDir/listening-journal.md during self-iteration. For every attempt, record what you expected, what the evaluator/audio facts said, what sounded alive, what sounded fake or static, and the exact revision you made.
- Write $agentDir/aqua-gap-ledger.md. List missing primitives, syntax sugar, control surfaces, analysis views, or renderer features that made the production harder. Each gap needs current workaround, evidence path, and whether it blocks composition or only polish.
- Write $agentDir/studio-lesson.md at the end. This is the compact lesson for the next musician: keep/cut verdicts, transferable production ideas, rejected tricks, and which AquaSynth abstractions should be mined next.
- The evaluator records `producer_musicianship_score`, `required_studio_docs_present`, `template_loop_risk`, `noise_percussion_risk`, `composition_section_score`, and `aqua_gap_count`. Candidates with missing studio evidence, stock-loop risk, or noise-percussion risk are failure pressure, not curriculum exemplars.

Self-iteration loop:
- For each target, run exactly $IterationsPerTarget local attempts before publishing the final `.aqua` file.
- Each attempt must write a candidate under `$iterationRoot/attempt-NN/<target-id>/patch`, run the scoring worker against that one target, inspect the rendered candidate evidence, and revise the next attempt.
- Scoring command pattern:
  `dotnet run --project "$workerProject" -- song-score --patch-root "<attempt-patch-dir>" --challenge "<challenge-json>" --artifact-root "<attempt-dir>" --batch-id "<agent-id>-<target-id>-attempt-NN" --store "<attempt-dir>/iteration-results.cc" --hypothesizer "$agentId-iteration"`
- After each score, read the attempt's `evaluator-report.md` and `summary.csv`. If the attempt rendered, also read `audio/comparison.txt`, `audio/candidate-analysis.md`, `audio/candidate-logmel-band-stats.csv`, `audio/candidate-rms-envelope-autocorr.csv`, and `audio/candidate-whitened-spectral-autocorr.csv`. If the attempt is `render-failed`, do not try to read missing candidate audio analysis files; treat the parse/lowering/render failure as negative syntax evidence and fix that before changing the composition.
- Use those candidate-side facts the same way you use target facts: compare spectral bands, envelope autocorrelation, whitened spectral autocorrelation, centroid/RMS, instrument-role metrics, and chip-distress risk. Move timings, filters, gains, sources, roles, and patterns between attempts.
- The evaluator records song-continuity metrics: `candidate_motion_coverage`, `motion_coverage_ratio`, `candidate_first_second_energy_share`, `first_second_energy_excess`, `candidate_tail_energy_share`, and `mode_collapse_risk`. A patch that plays a short phrase in the first second and then coasts on low-motion texture/noise is failed song evidence even if its RMS or log-mel score looks tolerable.
- If an attempt has `mode_collapse_risk >= .45`, the next attempt must add later-section motifs or eventful motion across the target's full duration. Do not merely raise the noise bed or pad gain; that is hiding, not composing.
- If an attempt has high `template_loop_risk` or `noise_percussion_risk`, rebuild the role ownership before touching level: drums need pitched body plus filtered skin and target-specific gates; texture needs band limits and musical motion.
- Keep intermediate candidates inside `$iterationRoot`; publish only one final `.aqua` per target to the official target patch output directory.

Reusable abstraction mining:
- Your second job is to act like a musician designing the future patch language. Every attempt should name at least one reusable abstraction that made the patch easier to think about.
- Write $agentDir/abstraction-ledger.md as the report of record for syntax-sugar mining. For each proposed abstraction include:
  - `name`: a lowercase slug such as `syrinx-hook`, `sub-kick-grid`, `spectral-dust-hat`, `additive-wash`, `call-response-bass`;
  - `role`: what musical job it owns;
  - `owner sentence`: `X owns Y so Z remains true`;
  - `controls`: the parameters a musician would expect to tweak;
  - `current lowering`: exact AquaSynth syntax using today's `voice`, `texture`, `pattern`, `scale`, `layer`, `harmonics`, `spectrum`, `param`, and `curve` commands;
  - `attempt evidence`: which attempt used it, score movement, candidate analysis facts, and what changed after listening to the rendered output;
  - `sugar sketch`: the shortest future syntax you wish existed;
  - `keep/cut verdict`: whether this abstraction should be mined into DSL sugar, kept as a stock patch idiom, or discarded.
- Prefer abstractions that transfer across multiple assigned songs. A one-off trick is allowed only if you label it as target-specific and say why it should not become syntax.
- Do not propose sugar that hides ownership. The future shorthand must still lower into visible syrinx, subtractive, additive/PAD, texture, pattern, scale, or curve owners.
- Composition abstractions are now first-class sugar candidates. Prefer reusable names like `metered_progression`, `hook_degrees`, `lane_sequence`, `section_rise`, `drop_fill`, `mix_scene`, or better target-grounded names. Each one must say which `meter`, `sequence`, `chords`, `scale`, `mix`, `param`, and `curve` lines it lowers into.

Noise and texture:
- Do not model background or recording color as a full-duration raw `voice wave=noise` bed. That produces static white-noise wash and will be treated as broken subtractive synthesis, not scene modeling.
- Use the shaped texture owner for noise roles, but make it target-specific: `texture name=dust_hat role=dust pattern=<target-derived-pattern> step=<beat_seconds/4> gain=.08 sustain=<duration_seconds>`, `texture name=air_wash role=air gain=.035 sustain=<duration_seconds>`, `texture name=codec_bed role=codec gain=.04 sustain=<duration_seconds>`.
- `texture` lowers to a gated noise voice with role-specific bandpass/highpass/lowpass/tremolo defaults and a control curve or pattern gate. Raw `voice wave=noise` is only acceptable for short transients with explicit gates and narrow filters.

Instrument ownership:
- This harness is feeding the music-generator curriculum, so do not solve the target with three naked oscillators and a prayer. The scorer now records instrument-role metrics and chip-distress risk in CultCache.
- If the patch has a singing, creature, or alien lead role, use the syrinx/acoustic owner: `source_port kind=syrinx`, pressure/opening curves, `acoustic_network`, and `acoustic_voice`, then shape it with filters/modulation.
- If the patch has drums or rhythmic transients, use subtractive ownership: pitched sine/triangle body, filtered noise skin, short envelope, and pattern gate.
- If the patch has a pad, bed, drone, or harmonic wash, use additive/PAD ownership: `layer engine=add` or `engine=pad`, `harmonics`, `spectrum`, and slow modulation.
- Ordinary simple voices are still legal as helpers, but a candidate built mostly from square/sine/saw blips with no syrinx, subtractive drum, additive/PAD, or shaped texture role will be treated as chip-distress-risk evidence.

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

Composition sugar available today:
- Set the meter before sequencing: `meter bpm=<tempo_bpm> beats=4`.
- Sequence any instrument lane without hand-declaring the gate parameter: `sequence name=kick pattern=x..x step=<beat_seconds> high=.8 low=0`, then bind `gain=@/seq/kick`.
- Declare chord/progression control lanes for pads or bass: `chords name=prog root=<root_hz> scale=minor progression=0,5,3,4 voicing=0,2,4 paths=/chords/prog/root,/chords/prog/third,/chords/prog/fifth step=bar`, then bind voices to those paths.
- Use `scale` for sampled note degrees inside the register: `scale name=hook path=/seq/pitch root=<root_hz> scale=minor degrees=0,2,4,2,5,4 step=<beat_seconds/2>`.
- Use `mix` for composition-scale lane motion: `mix name=pad points=0:.12,2:.18,4:.08,6:.24,8:.16`, then bind `gain=@/mix/pad/gain`.
- These are Strudel-ish authoring conveniences, but the lowered truth remains inspectable parameters and curves.

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
- Invent at least three reusable instrument or pattern roles across your assigned targets, such as `syrinx-hook`, `dust-hat-grid`, `codec-bed`, `rubber-bass`, `additive-wash`, `vowel-drone`, or another precise name.
- Write $agentDir/instrument-conventions.md as a concise index that points into `abstraction-ledger.md` and summarizes:
  - the proposed instrument role names;
  - the control surfaces each role would want;
  - any Strudel-like sugar that would make the patch shorter;
  - the exact lowering into today's AquaSynth syntax using explicit `voice`, `texture`, `pattern`, `scale`, `param`, and `curve` lines.
- The `.aqua` candidate must use today's implemented syntax only: `pattern`, `scale`, `param`, `curve`, and ordinary patch/voice syntax are legal. Proposed future sugar is evidence for the next DSL cut, not magic accepted by the parser.
- Today's implemented composition syntax also includes `meter`, `sequence`, `chords`, and `mix`. Prefer those over raw `param`/`curve` boilerplate when writing composition-scale structure.
- Legal oscillator `wave=` values are `sine`, `square`, `saw`, `triangle`, and `noise` only. Use `square` plus filters/envelopes for pulse-like tone; `pulse` is not accepted syntax.

Evidence contract:
- Read every assigned challenge report and $preEvidence.
- Write $agentDir/hypotheses.md with: per-target reference features, shared cross-target invariants, cited analysis artifacts, tempo/rhythm plan, register/scale plan, scene voices, synthesis owners, invented instrument roles, expected metric movement on assigned targets, known risks.
- Include a `Composition Map` section in $agentDir/hypotheses.md: meter/time signature, progression or tonal center, instrument lanes, section events after the opening, automation/mix moves, and which evaluator metrics should prove the arrangement did not collapse.
- Also write $agentDir/producer-brief.md, $agentDir/listening-journal.md, $agentDir/aqua-gap-ledger.md, and $agentDir/studio-lesson.md. These reports are now curriculum admission evidence, not decorative paperwork.
- Write exactly one `.aqua` file under each target patch output directory named `<agent-id>__<family>__<hypothesis>.aqua`.
- Include at least three scene voices or layers in the patch: a primary voice/body, a rhythmic drum/transient/noise role, and a pad/bed/recording-color role. Prefer syrinx for the primary voice when it is voice-like, subtractive for the drum/transient, and additive/PAD or shaped texture for the bed.
- The target must have full-form continuity: distinct musical events or motif mutations after the first second, with audible activity distributed across the beginning, middle, and ending. A one-second riff followed by gently textured pink noise is mode collapse and should not be submitted.
- Background/recording-color layers must use `texture` or an equivalently gated and band-shaped explicit voice; a static full-duration white-noise bed is failed evidence.
- Make each patch duration/gates cover its matching target duration from the challenge report.

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

    $musicKnowledgeStore = Join-Path $passDir "music-production-knowledge.cc"
    Invoke-LoggedProcess `
        -FilePath "dotnet" `
        -ArgumentList @(
            "run",
            "--project",
            $workerProject,
            "--",
            "music-distill",
            "--artifact-root",
            $loopRoot,
            "--output-store",
            $musicKnowledgeStore,
            "--output",
            (Join-Path $passDir "music-production-knowledge-report.md"),
            "--max-candidates",
            "40"
        ) `
        -LogPath (Join-Path $logRoot "$passId-music-distill.log")
    $latestMusicKnowledgeStore = $musicKnowledgeStore

    Invoke-LoggedProcess `
        -FilePath "dotnet" `
        -ArgumentList @(
            "run",
            "--project",
            $workerProject,
            "--",
            "music-show",
            "--store",
            $musicKnowledgeStore,
            "--knowledge-id",
            "music-production-quality-standard-v1",
            "--output",
            (Join-Path $passDir "music-production-quality-standard.md")
        ) `
        -LogPath (Join-Path $logRoot "$passId-music-show-standard.log")
}

Write-Host "Song snippet trial swarm complete: $loopRoot"
