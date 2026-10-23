using UnityEditor;
using UnityEngine;

public class AgentActionTester : EditorWindow
{
    private static readonly string[] Actions =
    {
        "hunt_lever", "pull_lever", "move_to_lever", "run_from",
        "move_to_barricade", "hold_barricade", "wait", "hunt_barricade", "leave_barricade"
    };

    [SerializeField] private Agent_Parent_Script agent;
    [SerializeField] private GameObject target;
    [SerializeField] private int actionIndex;

    [MenuItem("Tools/AI/Action Tester")]
    private static void Open()
    {
        GetWindow<AgentActionTester>("Action Tester");
    }

    private void OnGUI()
    {
        EditorGUILayout.HelpBox(
            "Open Main, enter Play Mode, then click Start. Select a spawned " +
            "playerPrefab(Clone) and click Use Selected Agent. Manual actions do not need Python.",
            MessageType.Info);

        agent = (Agent_Parent_Script)EditorGUILayout.ObjectField(
            "Agent", agent, typeof(Agent_Parent_Script), true);

        if (GUILayout.Button("Use Selected Agent"))
        {
            agent = Selection.activeGameObject != null
                ? Selection.activeGameObject.GetComponentInParent<Agent_Parent_Script>()
                : null;
        }

        actionIndex = Mathf.Clamp(actionIndex, 0, Actions.Length - 1);
        actionIndex = EditorGUILayout.Popup("Action", actionIndex, Actions);
        target = (GameObject)EditorGUILayout.ObjectField("Target (optional)", target, typeof(GameObject), true);
        EditorGUILayout.LabelField("Target name sent", target != null ? target.name : "(nearest matching object)");

        EditorGUILayout.HelpBox(
            "Use a scene target such as lever_1. Leave Target empty to use the nearest detected match. " +
            "hunt_lever, hunt_barricade, and wait ignore Target. Each command replaces the current action.",
            MessageType.None);

        bool liveAgent = Application.isPlaying && agent != null && agent.gameObject.scene.IsValid()
            && agent.gameObject.activeInHierarchy;
        AiAction executor = agent != null ? agent.GetComponent<AiAction>() : null;

        if (agent != null && executor == null)
            EditorGUILayout.HelpBox("This agent is missing its AiAction component.", MessageType.Error);

        if (liveAgent && executor != null && !executor.isActiveAndEnabled)
            EditorGUILayout.HelpBox("Enable AiAction so detection and movement can update.", MessageType.Warning);

        if (liveAgent && target != null && executor != null)
        {
            float distance = Vector2.Distance(agent.transform.position, target.transform.position);
            EditorGUILayout.LabelField("Target distance / detection range", $"{distance:F2} / {executor.detectionRange:F2}");
        }

        if (liveAgent && agent.TryGetComponent<ScaspStrategy>(out var strategy) && strategy.isActiveAndEnabled)
            EditorGUILayout.HelpBox(
                "For an isolated manual test, uncheck ScaspStrategy on this agent first. " +
                "An outstanding server response could otherwise replace your command. " +
                "Re-enable it before requesting an AI decision.", MessageType.Warning);

        using (new EditorGUI.DisabledScope(!liveAgent || executor == null || !executor.isActiveAndEnabled))
        {
            if (GUILayout.Button("Move To Nearest Window"))
            {
                target = null;
                actionIndex = System.Array.IndexOf(Actions, "move_to_barricade");
                agent.ExecuteAction("move_to_barricade");
            }

            if (GUILayout.Button("Execute Action"))
            {
                agent.ExecuteAction(Actions[actionIndex], target != null ? target.name : "");
            }

            if (GUILayout.Button("Stop Agent (wait)"))
                agent.ExecuteAction("wait");
        }

        using (new EditorGUI.DisabledScope(!liveAgent))
        {
            if (GUILayout.Button("Request AI Decision"))
                agent.RequestDecision();
        }

        if (!Application.isPlaying)
            EditorGUILayout.HelpBox("Commands are available in Play Mode.", MessageType.Info);
        else if (EditorApplication.isPaused)
            EditorGUILayout.HelpBox("Resume Play Mode to see movement and physics update.", MessageType.Info);
    }

    private void OnInspectorUpdate()
    {
        Repaint();
    }
}
