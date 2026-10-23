using System;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using UnityEngine;
using UnityEngine.Events;

// Attach to an active GameObject. Intended for Unity Editor and desktop players.
public class ScaspClient : MonoBehaviour
{
    [Header("s(CASP) Server")]
    [SerializeField] private string serverUrl = "ws://localhost:6767";
    [Header("Original AI Events")]
    [SerializeField] private UnityEvent<string> onAction = new UnityEvent<string>();

    private ClientWebSocket socket;
    private CancellationTokenSource lifetime;
    private readonly SemaphoreSlim sendLock = new SemaphoreSlim(1, 1);

    // Queue responses for processing on Unity's main thread
    private readonly ConcurrentQueue<string> responseQueue = new ConcurrentQueue<string>();

    public bool IsConnected => socket != null && socket.State == WebSocketState.Open;
    public UnityEvent<string> OnAction => onAction;

    // NPC specific action event
    // Parameters are actor, action, target
    public event Action<string, string, string> NpcActionReceived;

    [Serializable]
    private class Request
    {
        public string request_type;
        public string message_type;
        public string culprit;
        // NPC-specific request fields
        public string actor;
        public NpcState state;
    }

    [Serializable]
    private class Response
    {
        public string message_type;
        public string[] possible_actions;
        public string error;
        public string detail;
        // NPC-specific response fields
        public string actor;
        public string action;
        public string target;
    }

    private async void Start()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        Debug.LogError("ScaspClient requires a browser-compatible WebSocket client for WebGL.", this);
        return;
#else
        lifetime = new CancellationTokenSource();
        socket = new ClientWebSocket();
        try
        {
            using (var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token))
            {
                connectTimeout.CancelAfter(TimeSpan.FromSeconds(10));
                await socket.ConnectAsync(new Uri(serverUrl), connectTimeout.Token);
            }
            Debug.Log("s(CASP) connected: " + serverUrl, this);
            //await SendAsync(new Request { request_type = "get_action" });
            await ReceiveLoopAsync(lifetime.Token);
        }
        catch (Exception exception)
        {
            if (!lifetime.IsCancellationRequested)
                Debug.LogError("s(CASP) connection ended: " + exception.Message, this);
        }
        finally
        {
            lifetime.Cancel();
            socket.Dispose();
            socket = null;
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
                Debug.LogError("s(CASP) " + response.error + ": " + response.detail, this);
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
                Debug.Log($"s(CASP) NPC: {response.actor}, " + $"Action: {response.action}, Target: {response.target}", this);

                NpcActionReceived?.Invoke(response.actor, response.action, response.target);
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
                        Debug.LogWarning("s(CASP) server closed the connection. Restart Play Mode to reconnect.", this);
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

                /*
                string json = Encoding.UTF8.GetString(message.ToArray());
                var response = JsonUtility.FromJson<Response>(json);
                if (response == null)
                    throw new InvalidDataException("Empty server response.");
                
                // Queue the response for processing in Update()
                responseQueue.Enqueue(response);
                
                
                if (response.message_type == "error")
                    Debug.LogError("s(CASP) " + response.error + ": " + response.detail, this);
                else if (response.message_type == "possible_actions" && response.possible_actions != null)
                {
                    foreach (string action in response.possible_actions)
                    {
                        Debug.Log("s(CASP) action: " + action, this);
                        onAction.Invoke(action);
                    }
                }
                */
            }
        }
    }

    private void OnDestroy()
    {
        lifetime?.Cancel();
        socket?.Abort();
    }
}
