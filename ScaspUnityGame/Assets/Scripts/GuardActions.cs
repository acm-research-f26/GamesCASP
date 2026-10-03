using UnityEngine;

public class GuardActions : MonoBehaviour
{
    // Connect ScaspClient's On Action event to this method (Dynamic string).
    public void HandleAction(string action)
    {
        switch (action)
        {
            case "raise_alarm":
                RaiseAlarm();
                break;
            case "find_player_last":
                FindPlayerLast();
                break;
            case "investigate_noise":
                InvestigateNoise();
                break;
            case "wander_randomly":
                WanderRandomly();
                break;
            default:
                Debug.LogWarning("No guard behavior configured for: " + action, this);
                break;
        }
    }

    private void RaiseAlarm()
    {
        Debug.Log("Guard is raising the alarm!", this);
        // TODO: Trigger your alarm sound, lights, or animation here.
    }

    private void FindPlayerLast()
    {
        Debug.Log("Guard is searching the player's last known location!", this);
        // TODO: Move the guard to your stored last known player position.
    }

    private void InvestigateNoise()
    {
        Debug.Log("Guard is investigating a noise!", this);
        // TODO: Move the guard to your stored noise position.
    }

    private void WanderRandomly()
    {
        Debug.Log("Guard is wandering!", this);
        // TODO: Choose a reachable destination and move the guard there.
    }
}
