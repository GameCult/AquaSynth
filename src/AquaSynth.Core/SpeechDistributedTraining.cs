using System.Diagnostics;
using GameCult.Caching;
using GameCult.Caching.MessagePack;
using GameCult.Mesh;
using MessagePack;

namespace AquaSynth.Dsl;

[CultDocument("aquasynth.speech_render_request", "aquasynth.speech_render_request.v1")]
[MessagePackObject]
public sealed record SpeechRenderRequest(
    [property: Key(0)]
    [property: CultName]
    string RequestId,
    [property: Key(1)] string BatchId,
    [property: Key(2)] string CreatedAtUtc,
    [property: Key(3)] string CurriculumTier,
    [property: Key(4)] string TargetReferenceId,
    [property: Key(5)] string MorphologyId,
    [property: Key(6)] string RendererProfileId,
    [property: Key(7)] string ScoringRecipeId,
    [property: Key(8)] SpeechUtteranceEmbeddingInputSnapshot UtteranceInput,
    [property: Key(9)] SpeechPhoneticEventSnapshot PhoneticEvent,
    [property: Key(10)] float[] CandidateControlVector,
    [property: Key(11)] float[] ReferenceControlVector,
    [property: Key(12)] SpeechTimingReceipt[] TimingReceipts);

[CultDocument("aquasynth.speech_render_result", "aquasynth.speech_render_result.v1")]
[MessagePackObject]
public sealed record SpeechRenderResult(
    [property: Key(0)]
    [property: CultName]
    string ResultId,
    [property: Key(1)]
    [property: CultIndex("request")]
    string RequestId,
    [property: Key(2)] string WorkerId,
    [property: Key(3)] string RendererToolchain,
    [property: Key(4)] string CompletedAtUtc,
    [property: Key(5)] SpeechRenderStatus Status,
    [property: Key(6)] float Loss,
    [property: Key(7)] float[] OutputGradient,
    [property: Key(8)] SpeechScoreMetric[] Metrics,
    [property: Key(9)] SpeechRenderArtifact[] Artifacts,
    [property: Key(10)] SpeechTimingReceipt[] TimingReceipts,
    [property: Key(11)] string FailureCode = "",
    [property: Key(12)] string FailureMessage = "");

[CultDocument("aquasynth.speech_training_checkpoint", "aquasynth.speech_training_checkpoint.v1")]
[MessagePackObject]
public sealed record SpeechTrainingCheckpoint(
    [property: Key(0)]
    [property: CultName]
    string CheckpointId,
    [property: Key(1)] string BatchId,
    [property: Key(2)] string CreatedAtUtc,
    [property: Key(3)] int AppliedResultCount,
    [property: Key(4)] float MeanAppliedLoss,
    [property: Key(5)] string[] AppliedResultIds,
    [property: Key(6)] string Notes,
    [property: Key(7)] SpeechTimingReceipt[] TimingReceipts);

[MessagePackObject]
public sealed record SpeechTimingReceipt(
    [property: Key(0)] string StageId,
    [property: Key(1)] string DecidedAtUtc,
    [property: Key(2)] double LatencyMilliseconds,
    [property: Key(3)] double BudgetMilliseconds,
    [property: Key(4)] float Confidence,
    [property: Key(5)] string Notes);

[MessagePackObject]
public sealed record SpeechUtteranceEmbeddingInputSnapshot(
    [property: Key(0)] float[] TextEmbedding,
    [property: Key(1)] float[] PhoneticRealizationEmbedding,
    [property: Key(2)] float[] Prosody,
    [property: Key(3)] float[] CharacterState)
{
    public UtteranceEmbeddingInput ToInput() => new(TextEmbedding, PhoneticRealizationEmbedding, Prosody, CharacterState);

    public static SpeechUtteranceEmbeddingInputSnapshot From(UtteranceEmbeddingInput input) =>
        new([.. input.SpeechTextEmbedding], [.. input.PhoneticRealizationEmbedding], [.. input.ProsodyAndEmphasisHints], [.. input.CharacterStateVector]);
}

[MessagePackObject]
public sealed record SpeechPhoneticEventSnapshot(
    [property: Key(0)] string Id,
    [property: Key(1)] string Ipa,
    [property: Key(2)] PhoneticFeatures Features,
    [property: Key(3)] double StartSeconds,
    [property: Key(4)] double DurationSeconds,
    [property: Key(5)] PhoneticProsody Prosody)
{
    public PhoneticEvent ToEvent() => new(Id, Ipa, Features, StartSeconds, DurationSeconds, Prosody);

    public static SpeechPhoneticEventSnapshot From(PhoneticEvent phoneticEvent) =>
        new(
            phoneticEvent.Id,
            phoneticEvent.Ipa,
            phoneticEvent.Features,
            phoneticEvent.StartSeconds,
            phoneticEvent.DurationSeconds,
            phoneticEvent.Prosody);
}

[MessagePackObject]
public sealed record SpeechScoreMetric(
    [property: Key(0)] string Name,
    [property: Key(1)] float Value,
    [property: Key(2)] float Weight);

[MessagePackObject]
public sealed record SpeechRenderArtifact(
    [property: Key(0)] string Kind,
    [property: Key(1)] string Uri,
    [property: Key(2)] string ContentHash);

public enum SpeechRenderStatus
{
    Succeeded,
    Failed
}

[CultDocument("aquasynth.speech_worker_payload_manifest", "aquasynth.speech_worker_payload_manifest.v1")]
[MessagePackObject]
public sealed record SpeechWorkerPayloadManifest(
    [property: Key(0)]
    [property: CultName]
    string PayloadId,
    [property: Key(1)] string BatchId,
    [property: Key(2)] string CreatedAtUtc,
    [property: Key(3)] string RendererProfileId,
    [property: Key(4)] string ScoringRecipeId,
    [property: Key(5)] string RequiredRuntimeKind,
    [property: Key(6)] string WorkerRole,
    [property: Key(7)] SpeechWorkerPayloadArtifact[] Artifacts,
    [property: Key(8)] string Notes);

[MessagePackObject]
public sealed record SpeechWorkerPayloadArtifact(
    [property: Key(0)] string Kind,
    [property: Key(1)] string Uri,
    [property: Key(2)] string ContentHash,
    [property: Key(3)] string MediaType);

[CultDocument("aquasynth.speech_worker_admission_receipt", "aquasynth.speech_worker_admission_receipt.v1")]
[MessagePackObject]
public sealed record SpeechWorkerAdmissionReceipt(
    [property: Key(0)]
    [property: CultName]
    string ReceiptId,
    [property: Key(1)]
    [property: CultIndex("payload")]
    string PayloadId,
    [property: Key(2)] string PeerId,
    [property: Key(3)] string LeaseId,
    [property: Key(4)] string DecidedAtUtc,
    [property: Key(5)] SpeechWorkerAdmissionStatus Status,
    [property: Key(6)] string[] AssignedRequestIds,
    [property: Key(7)] string Reason,
    [property: Key(8)] SpeechTimingReceipt[] TimingReceipts);

public enum SpeechWorkerAdmissionStatus
{
    Admitted,
    Rejected
}

public sealed record SpeechCultMeshWorkerAssignment(
    CultMeshPeerCard Peer,
    CultMeshAuthorityLease Lease,
    SpeechWorkerAdmissionReceipt Receipt,
    IReadOnlyList<SpeechRenderRequest> Requests);

public sealed record SpeechCultMeshTrainingStepResult(
    SpeechWorkerPayloadManifest PayloadManifest,
    IReadOnlyList<SpeechWorkerAdmissionReceipt> AdmissionReceipts,
    IReadOnlyList<SpeechRenderResult> WorkerResults,
    SpeechDistributedTrainingApplyResult Applied);

public static class SpeechCultMeshRoles
{
    public const string RenderWorker = "speech-render-worker";
}

public sealed record SpeechDistributedTrainingOptions(
    string CurriculumTier = "tiny-local-proof",
    string TargetReferenceId = "local/reference-control-vector",
    string MorphologyId = "neutral-human-ish",
    string RendererProfileId = "compiled-faust-worker",
    string ScoringRecipeId = "control-vector-mse-proof",
    string RequiredRuntimeKind = "dotnet-csharp",
    string WorkerRole = SpeechCultMeshRoles.RenderWorker,
    float UtteranceLearningRate = 0.04f,
    float SynthDriverLearningRate = 0.04f);

public sealed record SpeechDistributedTrainingApplyResult(
    SpeechTrainingCheckpoint Checkpoint,
    IReadOnlyList<PackedNeuralBackpropagation> Backpropagations);

public static class SpeechDistributedTrainingCultCacheStore
{
    public static Task UpsertRequestsAsync(string filePath, IEnumerable<SpeechRenderRequest> requests) =>
        UpsertAsync(filePath, requests, request => $"speech-render-request:{request.RequestId}");

    public static Task UpsertResultsAsync(string filePath, IEnumerable<SpeechRenderResult> results) =>
        UpsertAsync(filePath, results, result => $"speech-render-result:{result.ResultId}");

    public static Task UpsertCheckpointsAsync(string filePath, IEnumerable<SpeechTrainingCheckpoint> checkpoints) =>
        UpsertAsync(filePath, checkpoints, checkpoint => $"speech-training-checkpoint:{checkpoint.CheckpointId}");

    public static Task UpsertPayloadManifestsAsync(string filePath, IEnumerable<SpeechWorkerPayloadManifest> payloads) =>
        UpsertAsync(filePath, payloads, payload => $"speech-worker-payload:{payload.PayloadId}");

    public static Task UpsertAdmissionReceiptsAsync(string filePath, IEnumerable<SpeechWorkerAdmissionReceipt> receipts) =>
        UpsertAsync(filePath, receipts, receipt => $"speech-worker-admission:{receipt.ReceiptId}");

    public static Task<IReadOnlyList<SpeechRenderRequest>> ReadRequestsAsync(string filePath) =>
        ReadAllAsync<SpeechRenderRequest>(filePath, request => request.RequestId);

    public static Task<IReadOnlyList<SpeechRenderResult>> ReadResultsAsync(string filePath) =>
        ReadAllAsync<SpeechRenderResult>(filePath, result => result.ResultId);

    public static Task<IReadOnlyList<SpeechTrainingCheckpoint>> ReadCheckpointsAsync(string filePath) =>
        ReadAllAsync<SpeechTrainingCheckpoint>(filePath, checkpoint => checkpoint.CheckpointId);

    public static Task<IReadOnlyList<SpeechWorkerPayloadManifest>> ReadPayloadManifestsAsync(string filePath) =>
        ReadAllAsync<SpeechWorkerPayloadManifest>(filePath, payload => payload.PayloadId);

    public static Task<IReadOnlyList<SpeechWorkerAdmissionReceipt>> ReadAdmissionReceiptsAsync(string filePath) =>
        ReadAllAsync<SpeechWorkerAdmissionReceipt>(filePath, receipt => receipt.ReceiptId);

    private static async Task UpsertAsync<T>(
        string filePath,
        IEnumerable<T> documents,
        Func<T, string> key)
        where T : class
    {
        using var cache = await OpenForMutationAsync(filePath).ConfigureAwait(false);
        foreach (var document in documents)
        {
            await cache.UpsertAsync(document, new CultRecordHandle<T>(new CultRecordKey(key(document)))).ConfigureAwait(false);
        }

        await cache.FlushAsync().ConfigureAwait(false);
    }

    private static async Task<IReadOnlyList<T>> ReadAllAsync<T>(string filePath, Func<T, string> orderKey)
        where T : class
    {
        if (!File.Exists(filePath))
        {
            return [];
        }

        using var cache = await CultCacheMessagePack.OpenAsync(filePath).ConfigureAwait(false);
        return cache.GetAll<T>().OrderBy(orderKey, StringComparer.Ordinal).ToArray();
    }

    private static async Task<CultCache> OpenForMutationAsync(string filePath)
    {
        var options = new CultCacheOpenOptions { PullOnOpen = File.Exists(filePath) };
        return await CultCacheMessagePack.OpenAsync(filePath, options).ConfigureAwait(false);
    }
}

public static class SpeechDistributedTrainingCoordinator
{
    public static IReadOnlyList<SpeechRenderRequest> CreateRenderRequests(
        string batchId,
        SpeechBackpropagationPipeline pipeline,
        IReadOnlyList<SpeechBackpropagationTrainingExample> examples,
        SpeechDistributedTrainingOptions? options = null)
    {
        options ??= new SpeechDistributedTrainingOptions();
        var createdAt = DateTimeOffset.UtcNow;
        var createdAtText = createdAt.ToString("O");
        var requests = new SpeechRenderRequest[examples.Count];
        for (var index = 0; index < examples.Count; index++)
        {
            var example = examples[index];
            var candidate = pipeline.Predict(example).ToVector(pipeline.SynthDriver.MelBandCount);
            requests[index] = new SpeechRenderRequest(
                $"{batchId}:{index:0000}",
                batchId,
                createdAtText,
                options.CurriculumTier,
                options.TargetReferenceId,
                options.MorphologyId,
                options.RendererProfileId,
                options.ScoringRecipeId,
                SpeechUtteranceEmbeddingInputSnapshot.From(example.UtteranceInput),
                SpeechPhoneticEventSnapshot.From(example.Event),
                candidate,
                example.Target.ToVector(pipeline.SynthDriver.MelBandCount),
                [
                    new SpeechTimingReceipt(
                        "utterance-to-candidate-controls",
                        createdAtText,
                        0,
                        0,
                        CandidateConfidence(candidate),
                        "Local request creation captured the current utterance encoder and synth-driver decision.")
                ]);
        }

        return requests;
    }

    public static IReadOnlyList<SpeechRenderResult> ScoreControlVectorRequests(
        IReadOnlyList<SpeechRenderRequest> requests,
        string workerId,
        string rendererToolchain = "local-control-vector-proof")
    {
        var completedAt = DateTimeOffset.UtcNow.ToString("O");
        var results = new SpeechRenderResult[requests.Count];
        for (var index = 0; index < requests.Count; index++)
        {
            var startedAt = DateTimeOffset.UtcNow;
            var startedStamp = Stopwatch.GetTimestamp();
            var request = requests[index];
            if (request.CandidateControlVector.Length != request.ReferenceControlVector.Length)
            {
                var latency = Stopwatch.GetElapsedTime(startedStamp).TotalMilliseconds;
                results[index] = new SpeechRenderResult(
                    $"{request.RequestId}:failed",
                    request.RequestId,
                    workerId,
                    rendererToolchain,
                    completedAt,
                    SpeechRenderStatus.Failed,
                    0,
                    [],
                    [],
                    [],
                    [
                        new SpeechTimingReceipt(
                            "score-render-result",
                            startedAt.ToString("O"),
                            latency,
                            0,
                            0,
                            "Rejected before scoring because the candidate/reference control vector sizes differ.")
                    ],
                    "control_vector_size_mismatch",
                    "Candidate and reference control vectors must have the same length.");
                continue;
            }

            var gradient = new float[request.CandidateControlVector.Length];
            var loss = 0f;
            for (var valueIndex = 0; valueIndex < gradient.Length; valueIndex++)
            {
                var difference = request.CandidateControlVector[valueIndex] - request.ReferenceControlVector[valueIndex];
                gradient[valueIndex] = 2f * difference / gradient.Length;
                loss += difference * difference;
            }

            loss /= gradient.Length;
            var scoreLatency = Stopwatch.GetElapsedTime(startedStamp).TotalMilliseconds;
            results[index] = new SpeechRenderResult(
                $"{request.RequestId}:score",
                request.RequestId,
                workerId,
                rendererToolchain,
                completedAt,
                SpeechRenderStatus.Succeeded,
                loss,
                gradient,
                [new SpeechScoreMetric("control_mse", loss, 1)],
                [new SpeechRenderArtifact("score-report", $"cultcache://speech-render-result/{request.RequestId}", "")],
                [
                    new SpeechTimingReceipt(
                        "score-render-result",
                        startedAt.ToString("O"),
                        scoreLatency,
                        0,
                        LossConfidence(loss),
                        "Local control-vector proof scored candidate output against reference output.")
                ]);
        }

        return results;
    }

    public static SpeechWorkerPayloadManifest CreateCultMeshPayloadManifest(
        string payloadId,
        IReadOnlyList<SpeechRenderRequest> requests,
        IReadOnlyList<SpeechWorkerPayloadArtifact>? artifacts = null,
        SpeechDistributedTrainingOptions? options = null)
    {
        if (requests.Count == 0)
        {
            throw new ArgumentException("A CultMesh speech payload needs at least one render request.", nameof(requests));
        }

        options ??= new SpeechDistributedTrainingOptions();
        return new SpeechWorkerPayloadManifest(
            payloadId,
            requests[0].BatchId,
            DateTimeOffset.UtcNow.ToString("O"),
            options.RendererProfileId,
            options.ScoringRecipeId,
            options.RequiredRuntimeKind,
            options.WorkerRole,
            [.. artifacts ?? DefaultPayloadArtifacts(options)],
            "Payload manifest authorizes transport/admission only; workers return gradients and receipts, not checkpoints.");
    }

    public static IReadOnlyList<SpeechCultMeshWorkerAssignment> AdmitCultMeshWorkers(
        SpeechWorkerPayloadManifest payload,
        IReadOnlyList<SpeechRenderRequest> requests,
        IReadOnlyList<CultMeshPeerCard> peers,
        CultMeshAuthorityLeaseCatalog leases,
        string? shardId = null,
        DateTimeOffset? at = null)
    {
        if (requests.Count == 0)
        {
            return [];
        }

        var admittedPeers = new List<(CultMeshPeerCard Peer, CultMeshAuthorityLease Lease)>();
        foreach (var peer in peers.OrderBy(peer => peer.PeerId, StringComparer.Ordinal))
        {
            if (!peer.HasRole(payload.WorkerRole))
            {
                continue;
            }

            var leaseId = peer.AuthorityLeaseId;
            if (string.IsNullOrWhiteSpace(leaseId))
            {
                continue;
            }

            var lease = leases.Get(leaseId);
            if (lease is null || !leases.IsAuthorized(peer, payload.WorkerRole, shardId, at))
            {
                continue;
            }

            admittedPeers.Add((peer, lease));
        }

        if (admittedPeers.Count == 0)
        {
            throw new InvalidOperationException("no CultMesh speech render workers were admitted for the payload");
        }

        var requestBuckets = admittedPeers
            .Select(_ => new List<SpeechRenderRequest>())
            .ToArray();
        for (var index = 0; index < requests.Count; index++)
        {
            requestBuckets[index % requestBuckets.Length].Add(requests[index]);
        }

        var decidedAt = DateTimeOffset.UtcNow.ToString("O");
        var assignments = new List<SpeechCultMeshWorkerAssignment>();
        for (var index = 0; index < admittedPeers.Count; index++)
        {
            var bucket = requestBuckets[index];
            if (bucket.Count == 0)
            {
                continue;
            }

            var (peer, lease) = admittedPeers[index];
            var requestIds = bucket.Select(request => request.RequestId).ToArray();
            var receipt = new SpeechWorkerAdmissionReceipt(
                $"{payload.PayloadId}:{peer.PeerId}:admitted",
                payload.PayloadId,
                peer.PeerId,
                lease.LeaseId,
                decidedAt,
                SpeechWorkerAdmissionStatus.Admitted,
                requestIds,
                "Peer advertised the speech render role and held a valid CultMesh worker lease.",
                [
                    new SpeechTimingReceipt(
                        "cultmesh-worker-admission",
                        decidedAt,
                        0,
                        0,
                        1,
                        $"Admitted {requestIds.Length} request(s) for score-only worker execution.")
                ]);
            assignments.Add(new SpeechCultMeshWorkerAssignment(peer, lease, receipt, bucket));
        }

        return assignments;
    }

    public static IReadOnlyList<SpeechWorkerAdmissionReceipt> RejectCultMeshWorkers(
        SpeechWorkerPayloadManifest payload,
        IReadOnlyList<CultMeshPeerCard> peers,
        CultMeshAuthorityLeaseCatalog leases,
        string? shardId = null,
        DateTimeOffset? at = null)
    {
        var decidedAt = DateTimeOffset.UtcNow.ToString("O");
        var rejected = new List<SpeechWorkerAdmissionReceipt>();
        foreach (var peer in peers.OrderBy(peer => peer.PeerId, StringComparer.Ordinal))
        {
            var reason = RejectionReason(payload, peer, leases, shardId, at);
            if (reason.Length == 0)
            {
                continue;
            }

            rejected.Add(new SpeechWorkerAdmissionReceipt(
                $"{payload.PayloadId}:{peer.PeerId}:rejected",
                payload.PayloadId,
                peer.PeerId,
                peer.AuthorityLeaseId ?? "",
                decidedAt,
                SpeechWorkerAdmissionStatus.Rejected,
                [],
                reason,
                [
                    new SpeechTimingReceipt(
                        "cultmesh-worker-admission",
                        decidedAt,
                        0,
                        0,
                        0,
                        reason)
                ]));
        }

        return rejected;
    }

    public static IReadOnlyList<SpeechRenderResult> RunCultMeshRenderWorkers(
        IReadOnlyList<SpeechCultMeshWorkerAssignment> assignments,
        SpeechWorkerPayloadManifest payload)
    {
        var results = new List<SpeechRenderResult>();
        foreach (var assignment in assignments)
        {
            if (assignment.Receipt.Status != SpeechWorkerAdmissionStatus.Admitted)
            {
                continue;
            }

            results.AddRange(ScoreControlVectorRequests(
                assignment.Requests,
                assignment.Peer.PeerId,
                $"{payload.RendererProfileId}+{payload.ScoringRecipeId}+cultmesh-lease:{assignment.Lease.LeaseId}"));
        }

        return results;
    }

    public static SpeechCultMeshTrainingStepResult RunCultMeshTrainingStep(
        string batchId,
        string payloadId,
        string checkpointId,
        SpeechBackpropagationPipeline pipeline,
        IReadOnlyList<SpeechBackpropagationTrainingExample> examples,
        IReadOnlyList<CultMeshPeerCard> peers,
        CultMeshAuthorityLeaseCatalog leases,
        SpeechDistributedTrainingOptions? options = null,
        string? shardId = null,
        DateTimeOffset? at = null)
    {
        options ??= new SpeechDistributedTrainingOptions();
        var requests = CreateRenderRequests(batchId, pipeline, examples, options);
        var payload = CreateCultMeshPayloadManifest(payloadId, requests, options: options);
        var assignments = AdmitCultMeshWorkers(payload, requests, peers, leases, shardId, at);
        var rejected = RejectCultMeshWorkers(payload, peers, leases, shardId, at);
        var results = RunCultMeshRenderWorkers(assignments, payload);
        var applied = ApplyResults(pipeline, requests, results, checkpointId, options);
        return new SpeechCultMeshTrainingStepResult(
            payload,
            [.. assignments.Select(assignment => assignment.Receipt), .. rejected],
            results,
            applied);
    }

    public static SpeechDistributedTrainingApplyResult ApplyResults(
        SpeechBackpropagationPipeline pipeline,
        IReadOnlyList<SpeechRenderRequest> requests,
        IReadOnlyList<SpeechRenderResult> results,
        string checkpointId,
        SpeechDistributedTrainingOptions? options = null)
    {
        options ??= new SpeechDistributedTrainingOptions();
        var requestById = requests.ToDictionary(request => request.RequestId, StringComparer.Ordinal);
        var applied = new List<PackedNeuralBackpropagation>();
        var appliedIds = new List<string>();
        var loss = 0f;

        foreach (var result in results.Where(result => result.Status == SpeechRenderStatus.Succeeded))
        {
            if (!requestById.TryGetValue(result.RequestId, out var request))
            {
                continue;
            }

            applied.Add(pipeline.TrainSingleFromSynthOutputGradient(
                request.UtteranceInput.ToInput(),
                request.PhoneticEvent.ToEvent(),
                result.OutputGradient,
                options.UtteranceLearningRate,
                options.SynthDriverLearningRate,
                result.Loss));
            appliedIds.Add(result.ResultId);
            loss += result.Loss;
        }

        if (applied.Count == 0)
        {
            throw new InvalidOperationException("no successful speech render results matched the request batch");
        }

        var checkpointCreatedAt = DateTimeOffset.UtcNow.ToString("O");
        var checkpoint = new SpeechTrainingCheckpoint(
            checkpointId,
            requests.FirstOrDefault()?.BatchId ?? "",
            checkpointCreatedAt,
            applied.Count,
            loss / applied.Count,
            [.. appliedIds],
            "Applied remote-render score gradients to the utterance encoder and synth driver.",
            [
                new SpeechTimingReceipt(
                    "apply-render-gradients",
                    checkpointCreatedAt,
                    0,
                    0,
                    LossConfidence(loss / applied.Count),
                    "Checkpoint records the training step that consumed successful render results.")
            ]);
        return new SpeechDistributedTrainingApplyResult(checkpoint, applied);
    }

    private static float CandidateConfidence(IReadOnlyList<float> candidate)
    {
        if (candidate.Count == 0)
        {
            return 0;
        }

        var sum = 0f;
        for (var index = 0; index < candidate.Count; index++)
        {
            sum += Math.Abs(candidate[index] - 0.5f) * 2f;
        }

        return Math.Clamp(sum / candidate.Count, 0f, 1f);
    }

    private static float LossConfidence(float loss)
    {
        if (!float.IsFinite(loss))
        {
            return 0;
        }

        return Math.Clamp(1f / (1f + loss), 0f, 1f);
    }

    private static IReadOnlyList<SpeechWorkerPayloadArtifact> DefaultPayloadArtifacts(SpeechDistributedTrainingOptions options) =>
    [
        new("compiled-faust-renderer", $"cultcache://speech-renderer/{options.RendererProfileId}", "", "application/wasm-or-native-faust"),
        new("scoring-code", $"cultcache://speech-scoring/{options.ScoringRecipeId}", "", "application/dotnet-assembly"),
        new("model-encoder-assembly", "cultcache://speech-model/aquasynth-core", "", "application/dotnet-assembly")
    ];

    private static string RejectionReason(
        SpeechWorkerPayloadManifest payload,
        CultMeshPeerCard peer,
        CultMeshAuthorityLeaseCatalog leases,
        string? shardId,
        DateTimeOffset? at)
    {
        if (!peer.HasRole(payload.WorkerRole))
        {
            return $"Peer does not advertise required worker role `{payload.WorkerRole}`.";
        }

        if (string.IsNullOrWhiteSpace(peer.AuthorityLeaseId))
        {
            return "Peer has no CultMesh authority lease id for worker admission.";
        }

        var lease = leases.Get(peer.AuthorityLeaseId!);
        if (lease is null)
        {
            return "Peer references a worker lease that is not present in the local lease catalog.";
        }

        if (!leases.IsAuthorized(peer, payload.WorkerRole, shardId, at))
        {
            return "Peer worker lease does not authorize the requested role, shard, or time window.";
        }

        return "";
    }
}
