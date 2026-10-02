using UnityEngine;

public class StartUIScript : MonoBehaviour
{
    public GameObject StartUI;

    public void StartGameClicked()
    {
        StartUI.SetActive(false);
    }
}
