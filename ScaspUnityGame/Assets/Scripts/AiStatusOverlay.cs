using UnityEngine;
using UnityEngine.InputSystem;

// Shows which AI is in control. Before Start, B switches between the Behavior Tree
// and s(CASP), and P between the s(CASP) policies. During play, it lists each NPC's
// connection, request/response counts, latency and last executed action. F1 hides it.
public class AiStatusOverlay : MonoBehaviour
{
    private PlayerManager players;
    private bool visible = true;
    private GUIStyle style;

    public void Bind(PlayerManager manager) => players = manager;

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        if (players == null || keyboard == null)
            return;
        if (keyboard.f1Key.wasPressedThisFrame)
            visible = !visible;
        if (players.GameStarted)
            return;
        if (keyboard.bKey.wasPressedThisFrame)
            players.DecisionBackend = players.DecisionBackend == NpcDecisionBackend.Scasp ? 
                NpcDecisionBackend.BehaviorTree : players.DecisionBackend == NpcDecisionBackend.BehaviorTree ?
                NpcDecisionBackend.ChatGPT :
                NpcDecisionBackend.Scasp;
        if (keyboard.pKey.wasPressedThisFrame)
            players.ScaspDecisionPolicy = players.ScaspDecisionPolicy == ScaspPolicy.Reasoned ? ScaspPolicy.Baseline : ScaspPolicy.Reasoned;
    }

    private void OnGUI()
    {
        if (!visible || players == null)
            return;
        style ??= new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, fontSize = 14, richText = true };

        string backend = players.DecisionBackend == NpcDecisionBackend.Scasp
            ? $"<b>s(CASP)</b> · {players.ScaspDecisionPolicy} policy"
            : players.DecisionBackend == NpcDecisionBackend.BehaviorTree ? 
            "<b>Behavior Tree</b>" 
            : "<b>ChatGPT</b>";
        string text;
        if (!players.GameStarted)
        {
            text = $"AI: {backend}\n[B] switch AI   [P] s(CASP) policy   [F1] hide";
        }
        else
        {
            text = $"AI: {backend}";
            foreach (Agent agent in players.players)
            {
                if (agent == null)
                    continue;
                var stats = AiDiagnostics.For(agent.name);
                string detail;
                if (players.DecisionBackend == NpcDecisionBackend.Scasp)
                {
                    var strategy = agent.GetComponent<ScaspStrategy>();
                    string link = strategy != null && strategy.IsConnected ? "<color=#7f7>connected</color>" : "<color=#f77>NOT CONNECTED</color>";
                    detail = $"{link} · {stats.Responses}/{stats.Requests} answered · {AiDiagnostics.Percentile(stats.RoundTripMs, 0.5f):F0} ms";
                }
                else if(players.DecisionBackend == NpcDecisionBackend.BehaviorTree)
                {
                    detail = $"{stats.DecisionMs.Count} decisions · {AiDiagnostics.Percentile(stats.DecisionMs, 0.5f):F2} ms";
                }
                else
                {
                    detail = $"{stats.Responses}/{stats.Requests} answered · {AiDiagnostics.Percentile(stats.RoundTripMs, 0.5f):F0} ms";
                }
                text += $"\n{agent.name} ({agent.Health} hp): {detail} · {stats.LastAction}";
            }
        }

        var content = new GUIContent(text);
        Vector2 size = style.CalcSize(content);
        GUI.Box(new Rect(10, 10, size.x + 10, size.y + 6), content, style);
    }
}
