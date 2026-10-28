using UnityEngine;
using System.Collections.Generic;

public class MapManager : MonoBehaviour
{
    // Original prefabs
    public GameObject wallPrefab;
    public GameObject windowPrefab;
    public GameObject leverPrefab;

    public LeverManager leverManager;

    public int numWindows = 3;
    public int numLevers = 2;


    // Original wall positions
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

    // Original window positions
    private List<Vector3> WindowPositions = new List<Vector3>()
    {
        new Vector3(4.6f, 3.2f, 0),
        new Vector3(4.6f, -1.3f, 0),
        new Vector3(-4.58f, -1.3f, 0),
        new Vector3(-2.3f, -3.4f, 0),
        new Vector3(2.3f, -3.4f, 0),
        new Vector3(-2.3f, 5.3f, 0),
        new Vector3(2.3f, 5.3f, 0),
    };

    //lever spawn directions
    private List<Vector3> LeverPositions = new List<Vector3>()
    {

        new Vector3(-1f, -1f, 0f),
        new Vector3(-1f,  0f, 0f),
        new Vector3(-1f,  1f, 0f),
        new Vector3( 0f, -1f, 0f),
        new Vector3( 0f,  0f, 0f),
        new Vector3( 0f,  1f, 0f),
        new Vector3( 1f, -1f, 0f),
        new Vector3( 1f,  0f, 0f),
        new Vector3( 1f,  1f, 0f)

    };

    List<Vector3> spawnedWindowPositions = new List<Vector3>();


    // Window is 32 pixels wide at 32 PPU.
    private const float windowOpening = 1f;

    // Wall segments are 4.5 Unity units long.
    private const float wallLength = 4.5f;

    public void StartGameClicked()
    {
        Debug.Log("Generating map");

        // Preserve Sam's window spawning.
        for (int i = 0; i < numWindows; i++)
        {

            int index = Random.Range(0, WindowPositions.Count);
            Vector3 position = WindowPositions[index];
            GameObject window = Instantiate(windowPrefab);
            window.transform.position = WindowPositions[index];
            WindowPositions.RemoveAt(index);

            spawnedWindowPositions.Add(position);
        }

        for (int i = 0; i < positions.Count; i++)
        {
            Vector3 wallPosition = positions[i];

            bool horizontal =
                wallPosition.y > 3.5f ||
                wallPosition.y < -2f;

            bool hasWindow = spawnedWindowPositions.Exists(
                windowPosition => Vector3.Distance(windowPosition, wallPosition) < 0.15f
            );

            if (hasWindow)
            {
                CreateWallWithOpening(wallPosition, horizontal);
            }
            else
            {
                CreateWall(wallPosition, horizontal, wallLength);
            }
        }


        //generate levers
        leverManager.totalLevers = numLevers;

        for (int i = 0; i < numLevers; i++)
        {
            int index = Random.Range(0, LeverPositions.Count);
            Vector3 position = LeverPositions[index];
            position.x *= Random.Range(1, 4);
            position.y *= Random.Range(1, 4);

            GameObject lever = Instantiate(leverPrefab, position, Quaternion.identity);
            lever.name = $"lever_{i + 1}";
            LeverScript leverScript = lever.GetComponent<LeverScript>();
            leverScript.leverManager = leverManager;
            if (lever.TryGetComponent<LeverFactEmitter>(out var emitter))
                emitter.SetLeverId(lever.name);
            LeverPositions.RemoveAt(index);
        }

    }

    private void CreateWallWithOpening(
        Vector3 center,
        bool horizontal)
    {
        float halfLength =
            (wallLength - windowOpening) / 2f;

        float offset =
            (windowOpening + halfLength) / 2f;

        Vector3 direction = horizontal
            ? Vector3.right
            : Vector3.up;

        CreateWall(
            center - direction * offset,
            horizontal,
            halfLength
        );

        CreateWall(
            center + direction * offset,
            horizontal,
            halfLength
        );
    }

    private void CreateWall(
        Vector3 position,
        bool horizontal,
        float length)
    {
        GameObject wall = Instantiate(wallPrefab);

        wall.transform.position = position;

        wall.transform.rotation = Quaternion.Euler(
            0,
            0,
            horizontal ? 0 : 90
        );

        Vector3 scale = wall.transform.localScale;

        // Preserve the wall's thickness.
        scale.x = length;

        wall.transform.localScale = scale;
    }
}
