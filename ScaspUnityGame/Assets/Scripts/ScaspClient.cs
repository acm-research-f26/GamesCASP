using System;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

// Attach to an active GameObject. Intended for Unity Editor and desktop players.
public class ScaspClient : MonoBehaviour
{
    [Header("s(CASP) Server")]
    [SerializeField] private string serverUrl = "ws://localhost:6767";
    [SerializeField, Min(0.1f)] private float factAcknowledgmentTimeout = 10f;
    [Tooltip("Seconds between reconnection attempts; doubles after each failure, up to 10 seconds.")]
    [SerializeField, Min(0.5f)] private float reconnectDelay = 1f;
    [Header("Original AI Events")]
    [SerializeField] private UnityEvent<string> onAction = new UnityEvent<string>();

    private ClientWebSocket socket;
    private CancellationTokenSource lifetime;
    private readonly SemaphoreSlim sendLock = new SemaphoreSlim(1, 1);
    // Each pending write finishes with the Python file write time in ms
    private readonly Dictionary<string, TaskCompletionSource<float>> pendingFactWrites =
        new Dictionary<string, TaskCompletionSource<float>>();

    // Queue responses for processing on Unity's main thread
    private readonly ConcurrentQueue<string> responseQueue = new ConcurrentQueue<string>();

    public bool IsConnected => socket != null && socket.State == WebSocketState.Open;
    public UnityEvent<string> OnAction => onAction;

    // NPC specific action event
    // Parameters are actor, action, target
    public event Action<string, string, string> NpcActionReceived;

    // The full decision reply, with its request ID, timings and optional explanation.
    public event Action<NpcDecisionResponse> NpcDecisionReceived;

    // Python rejected or failed a decision request: request ID and detail.
    public event Action<string, string> NpcDecisionError;

    public event Action Connected;
    public event Action Disconnected;

    public string ServerUrl => serverUrl;
    // Python could not produce a decision for this actor (validation or solver error).
    public event Action<string> NpcDecisionFailed;

    [Serializable]
    private class Request
    {
        public string request_type;
        public string message_type;
        public string culprit;
        // NPC-specific request fields
        public string actor;
        public NpcState state;
        public string request_id;
        public ScaspFactPayload[] facts;
        public string policy;
        public bool explain;
    }

    [Serializable]
    private class Response
    {
        public string message_type;
        public string[] possible_actions;
        public string error;
        public string detail;
        public string request_id;
        public string request_type;
        // NPC-specific response fields
        public string actor;
        public string action;
        public string target;
        public string policy;
        public float solver_ms;
        public float server_ms;
        public float explain_ms;
        public string justification;
        public string[] rejected;
        // Python's fact file write time, -1 means the reply didnt have it so it wont be counted
        public float write_ms = -1f;
    }

    // Reconnects with backoff until the component is destroyed, so a server that
    // starts late (or restarts) is picked up without restarting Play Mode.
    private async void Start()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        Debug.LogError("ScaspClient requires a browser-compatible WebSocket client for WebGL.", this);
        return;
#else
        lifetime = new CancellationTokenSource();
        CancellationToken token = lifetime.Token;
        int failures = 0;
        while (!token.IsCancellationRequested)
        {
            socket = new ClientWebSocket();
            bool connected = false;
            try
            {
                using (var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(token))
                {
                    connectTimeout.CancelAfter(TimeSpan.FromSeconds(10));
                    await socket.ConnectAsync(new Uri(serverUrl), connectTimeout.Token);
                }
                connected = true;
                failures = 0;
                AiDiagnostics.Log($"{name}: s(CASP) connected to {serverUrl}", this);
                Connected?.Invoke();
                await ReceiveLoopAsync(token);
            }
            catch (Exception exception)
            {
                if (!token.IsCancellationRequested && (connected || failures++ == 0))
                    Debug.LogError($"{name}: s(CASP) {(connected ? "connection ended" : "server unreachable at " + serverUrl)}: {exception.Message}. Retrying; is the Docker server running?", this);
            }
            finally
            {
                FailPendingFactWrites();
                socket.Dispose();
                socket = null;
                if (connected)
                    Disconnected?.Invoke();
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(Mathf.Min(10f, reconnectDelay * (1 << Mathf.Min(failures, 4)))), token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
#endif
    }

    [ContextMenu("Request Action (Play Mode)")]
    public void RequestAction()
    {
        SendRequest(new Request { request_type = "get_action" });
    }

    // Request a specific NPC's next action
    public void RequestNpcDecision(string actor, NpcState state)
    {
        if (string.IsNullOrWhiteSpace(actor) || state == null)
        {
            Debug.LogError("NPC decision requires an actor and state.", this);
            return;
        }

        SendRequest(new Request {request_type = "npc_decision", actor = actor, state = state});
    }

    // Correlated request: the reply (or error) carries requestId back.
    public void RequestNpcDecision(string actor, NpcState state, string requestId, string policy, bool explain)
    {
        if (string.IsNullOrWhiteSpace(actor) || state == null || string.IsNullOrEmpty(requestId))
        {
            Debug.LogError("NPC decision requires an actor, state and request ID.", this);
            return;
        }

        SendRequest(new Request { request_type = "npc_decision", actor = actor, state = state, request_id = requestId, policy = policy, explain = explain });
    }

    // Completion means Python acknowledged the file write, not just a socket send.
    // Returns Python's write time in ms, or a negative number if Python didnt send one
    public async Task<float> StoreFactAsync(string actor, Fact fact)
    {
        ScaspFactPayload.ValidateAtom(actor, nameof(actor));
        ScaspFactPayload payload = ScaspFactPayload.FromFact(fact);
        if (!IsConnected || lifetime == null || lifetime.IsCancellationRequested)
            throw new InvalidOperationException("s(CASP) is not connected.");

        string requestId = Guid.NewGuid().ToString("N");
        var completion = new TaskCompletionSource<float>(TaskCreationOptions.RunContinuationsAsynchronously);
        pendingFactWrites.Add(requestId, completion);
        try
        {
            await SendAsync(new Request
            {
                request_type = "store_facts",
                request_id = requestId,
                actor = actor,
                facts = new[] { payload }
            });

            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token))
            {
                Task delay = Task.Delay(TimeSpan.FromSeconds(factAcknowledgmentTimeout), timeout.Token);
                Task finished = await Task.WhenAny(completion.Task, delay);
                timeout.Cancel();
                if (finished != completion.Task)
                    throw new TimeoutException("Python did not acknowledge fact storage before the timeout.");
                return await completion.Task;
            }
        }
        finally
        {
            pendingFactWrites.Remove(requestId);
        }
    }
    // Sends a fact, then requests the action for the updated world state.
    public void ReportFact(string eventName, string culprit = null)
    {
        SendRequest(new Request {request_type = "fact", message_type = eventName, culprit = culprit}, true);
    }

    [ContextMenu("Report Heard Noise (Play Mode)")]
    public void ReportHeardNoise()
    {
        ReportFact("heard_noise");
    }

    private async void SendRequest(Request request, bool queryAfter = false)
    {
        if (!IsConnected || lifetime == null || lifetime.IsCancellationRequested)
        {
            Debug.LogWarning("s(CASP) is not connected. Enter Play Mode and check the server URL.", this);
            return;
        }
        try
        {
            await SendAsync(request, queryAfter);
        }
        catch (Exception exception)
        {
            if (!lifetime.IsCancellationRequested)
                Debug.LogError("s(CASP) send failed: " + exception.Message, this);
        }
    }

    private async Task SendAsync(Request request, bool queryAfter = false)
    {
        var token = lifetime.Token;
        await sendLock.WaitAsync(token);
        try
        {
            await SendJsonAsync(request, token);
            if (queryAfter)
                await SendJsonAsync(new Request { request_type = "get_action" }, token);
        }
        finally
        {
            sendLock.Release();
        }
    }

    private Task SendJsonAsync(Request request, CancellationToken token)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(JsonUtility.ToJson(request));
        return socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, token);
    }

    // Process server responses on Unity's main thread
    private void Update()
    {
        while (responseQueue.TryDequeue(out string json))
        {
            Response response;

            try
            {
                response = JsonUtility.FromJson<Response>(json);
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    "Invalid s(CASP) response: " + exception.Message, this);
                continue;
            }

            if (response == null)
                continue;

            if (response.message_type == "error")
            {
                if (!string.IsNullOrEmpty(response.request_id) &&
                    pendingFactWrites.TryGetValue(response.request_id, out var failedWrite))
                    failedWrite.TrySetException(new InvalidOperationException(response.error + ": " + response.detail));
                else if (!string.IsNullOrEmpty(response.request_id) && NpcDecisionError != null)
                    NpcDecisionError.Invoke(response.request_id, response.error + ": " + response.detail);
                else
                {
                    Debug.LogError("s(CASP) " + response.error + ": " + response.detail, this);
                    if (response.request_type == "npc_decision" && !string.IsNullOrEmpty(response.actor))
                        NpcDecisionFailed?.Invoke(response.actor);
                }
            }
            else if (response.message_type == "facts_stored")
            {
                if (!string.IsNullOrEmpty(response.request_id) &&
                    pendingFactWrites.TryGetValue(response.request_id, out var completedWrite))
                    completedWrite.TrySetResult(response.write_ms);
            }
            else if (response.message_type == "possible_actions" && response.possible_actions != null)
            {
                foreach (string action in response.possible_actions)
                {
                    Debug.Log("s(CASP) action: " + action, this);
                    onAction.Invoke(action);
                }
            }
            else if (response.message_type == "npc_action")
            {
                AiDiagnostics.Trace($"s(CASP) reply for {response.actor} (request {response.request_id}): {response.action}({response.target}), solver {response.solver_ms:F0} ms", this);

                NpcActionReceived?.Invoke(response.actor, response.action, response.target);
                NpcDecisionReceived?.Invoke(new NpcDecisionResponse
                {
                    Actor = response.actor,
                    Action = response.action,
                    Target = response.target ?? "",
                    RequestId = response.request_id ?? "",
                    Policy = response.policy ?? "",
                    SolverMs = response.solver_ms,
                    ServerMs = response.server_ms,
                    ExplainMs = response.explain_ms,
                    Justification = response.justification ?? "",
                    Rejected = response.rejected ?? Array.Empty<string>()
                });
            }
        }
    }


    private async Task ReceiveLoopAsync(CancellationToken token)
    {
        var buffer = new byte[4096];
        while (!token.IsCancellationRequested && IsConnected)
        {
            using (var message = new MemoryStream())
            {
                WebSocketReceiveResult result;
                do
                {
                    result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), token);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        Debug.LogWarning("s(CASP) server closed the connection. Attempting to reconnect.", this);
                        return;
                    }
                    if (result.MessageType != WebSocketMessageType.Text)
                        throw new InvalidDataException("Expected a text WebSocket message.");
                    message.Write(buffer, 0, result.Count);
                    if (message.Length > 1024 * 1024)
                        throw new InvalidDataException("Server message exceeds 1 MiB.");
                } while (!result.EndOfMessage);


                string json = Encoding.UTF8.GetString(message.ToArray());

                // Queue raw JSON for deserialization on Unity's main thread.
                responseQueue.Enqueue(json);

            }
        }
    }

    private void OnDestroy()
    {
        FailPendingFactWrites();
        lifetime?.Cancel();
        socket?.Abort();
    }

    private void FailPendingFactWrites()
    {
        foreach (var completion in pendingFactWrites.Values)
            completion.TrySetCanceled();
    }
}

// One s(CASP) decision reply as Python sent it.
public sealed class NpcDecisionResponse
{
    public string Actor;
    public string Action;
    public string Target;
    public string RequestId;
    public string Policy;
    public float SolverMs;
    public float ServerMs;
    public float ExplainMs;
    public string Justification;
    public string[] Rejected;
}
