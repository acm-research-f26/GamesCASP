using UnityEngine;
using System.Collections.Generic;

public class MapManager : MonoBehaviour
{

    //prefabs
    public GameObject wallPrefab;
    public GameObject windowPrefab;

    //wall positions
    private List<Vector3> positions = new List<Vector3>()
    {
        new Vector3(4.6f, 3.2f, 0),
        new Vector3(4.6f, -1.3f, 0),
        new Vector3(-4.58f, -1.3f, 0),
        new Vector3(-4.58f, 3.2f, 0),
        new Vector3(-2.23f, 5.35f, 0),
        new Vector3(2.28f, 5.35f, 0),
        new Vector3(-2.23f, -3.45f, 0),
        new Vector3(2.28f, -3.45f, 0)
    };

    private List<Vector3> WindowPositions = new List<Vector3>()
    {
        new Vector3(4.6f, 3.2f, 0),
        new Vector3(4.6f, -1.3f, 0),
        new Vector3(-4.58f, -1.3f, 0),
    };

    public void StartGameClicked()
    {
        Debug.Log("Generating map");
        //generate map
        for (int i = 0; i < positions.Count; i++)
        {
            GameObject wall = Instantiate(wallPrefab);
            wall.transform.position = positions[i];
            if (wall.transform.position.y > 3.5 || wall.transform.position.y < -2)
            {
                wall.transform.rotation = Quaternion.Euler(0, 0, 0);
            }
        }
        for (int i = 0; i < WindowPositions.Count; i++)
        {
            GameObject window = Instantiate(windowPrefab);
            window.transform.position = WindowPositions[i];
        }
    }
}
