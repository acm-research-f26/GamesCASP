# Connect Unity to the Python server

The server uses text JSON over WebSockets on port 6767. `Assets/Scripts/ScaspClient.cs`
is a reusable component for the Unity Editor and desktop players; no extra package is required.

## This repository's Unity project

1. In Unity Hub, add/open `ScaspUnityGame` (the project specifies Unity 6000.6.0f1).
2. Open your scene, create an empty GameObject named `ScaspConnection`, and add the
   `ScaspClient` component. Save the scene.
3. Leave **Server Url** as `ws://localhost:6767` when Python/Docker and Unity run on
   the same computer.
4. With the server running, press Play. The Console should show `s(CASP) connected`
   followed by `s(CASP) action: ...` or a solver error from the server.
5. In the component's context menu, select **Report Heard Noise (Play Mode)** to
   send a fact and request a new action. **Request Action (Play Mode)** queries
   without adding a fact.

## Your own Unity project

Copy `Assets/Scripts/ScaspClient.cs` into your project's `Assets` folder, then follow
steps 2–5 above. Use one client component per game session. This implementation
targets Editor/desktop, not WebGL; a browser build needs a browser-compatible
WebSocket implementation.

From another MonoBehaviour, reference the component in the Inspector:

```csharp
[SerializeField] private ScaspClient scasp;

public void OnNoiseHeard() => scasp.ReportFact("heard_noise");
public void OnVaseBroken() => scasp.ReportFact("vase_broken", "player");
public void CheckNextAction() => scasp.RequestAction();
```

Call these after the connection log appears (`scasp.IsConnected` is available).
`ReportFact` sends the event followed by an action query. Successful fact messages
have no acknowledgment. Invalid facts produce an error; the following query still
runs against the unchanged facts.

To react to actions, add a listener under **On Action** in the component Inspector:
connect a public `void HandleAction(string action)` method using the **dynamic
string** option. Alternatively, use `scasp.OnAction.AddListener(HandleAction)`
and remove that listener when your subscribing component is destroyed.
Callbacks run on Unity's main thread. The client logs actions; your listener must
translate names into actual gameplay behavior.

Supported events: `heard_noise`, `vase_broken`, `suspicious_sighting`,
`diamond_broken`, `alarm_raised`, and `player_seen`. `vase_broken` requires a culprit
matching `[a-z][A-Za-z0-9_]*`, such as `player` or `guard_1`.

## Connection troubleshooting

- On another computer/device, use `ws://<server-computer-LAN-IP>:6767` and allow
  inbound TCP port 6767 through that computer's firewall. `localhost` always
  means the computer/device running the client. Do not use `0.0.0.0` as the URL.
- With Docker, port 6767 must be published. `docker compose ... up` uses the
  repository's port mapping; `docker compose ... run` needs `--service-ports`
  as shown in the server README.
- A connection error means check the address, listening server, port publishing,
  and firewall. A `processing_failed` response means the connection worked but
  the server's solver failed; inspect the error detail and Python logs.
- Restart Play Mode to reconnect after starting/restarting the server. Automatic
  reconnection is not implemented.
- All clients share the server's facts. Reconnecting Unity preserves them;
  restarting the Python server resets them. Facts accumulate during a session.

Wire protocol examples:

```json
{"request_type":"get_action"}
{"request_type":"fact","message_type":"heard_noise"}
{"request_type":"fact","message_type":"vase_broken","culprit":"player"}
```

Action replies have the form
`{"message_type":"possible_actions","possible_actions":["action_name"]}`.
The current server returns one selected action, despite the plural field name.
