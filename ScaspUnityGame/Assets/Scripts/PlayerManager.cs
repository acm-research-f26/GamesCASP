using UnityEngine;
using System.Collections.Generic;

public class PlayerManager : MonoBehaviour
{
    //variables
    int numInnocents = GameSettings.InnocentCount;
    int numWerewolves = GameSettings.WerewolfCount;

    public List<Player> players = new List<Player>();
    //prefabs
    public GameObject playerPrefab;

    // when start button is clicked
    public void StartGameClicked()
    {
        //spawn players
        for (int i = 0; i < numInnocents; i++)
        {
            GameObject player = Instantiate(playerPrefab);
            Player playerScript = player.GetComponent<Player>();
            playerScript.Initialize("innocent", i, 0);
            players.Add(playerScript);
        }
    }
}
