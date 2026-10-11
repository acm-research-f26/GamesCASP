
using UnityEngine;
using System.Collections.Generic;

public class MapManager : MonoBehaviour
{
    // Original prefabs
    public GameObject wallPrefab;
    public GameObject windowPrefab;

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
    };

    // Window is 32 pixels wide at 32 PPU.
    private const float windowOpening = 1f;

    // Wall segments are 4.5 Unity units long.
    private const float wallLength = 4.5f;

    public void StartGameClicked()
    {
        Debug.Log("Generating map");

        for (int i = 0; i < positions.Count; i++)
        {
            Vector3 wallPosition = positions[i];

            bool horizontal =
                wallPosition.y > 3.5f ||
                wallPosition.y < -2f;

            bool hasWindow = WindowPositions.Contains(wallPosition);

            if (hasWindow)
            {
                CreateWallWithOpening(wallPosition, horizontal);
            }
            else
            {
                CreateWall(wallPosition, horizontal, wallLength);
            }
        }

        // Preserve Sam's window spawning.
        for (int i = 0; i < WindowPositions.Count; i++)
        {
            GameObject window = Instantiate(windowPrefab);
            window.transform.position = WindowPositions[i];
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
