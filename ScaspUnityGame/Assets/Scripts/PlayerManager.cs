using UnityEngine;

public class PlayerManager : MonoBehaviour
{
    int numInnocents = GameSettings.InnocentCount;

    //prefabs
    public GameObject playerPrefab;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    public void StartGameClicked ()
    {
        //spawn players
        for (int i = 0; i < numInnocents; i++)
        {
            GameObject player = Instantiate(playerPrefab);

            Player playerScript = player.GetComponent<Player>();
            playerScript.Initialize("innocent");
        }
    }
}
