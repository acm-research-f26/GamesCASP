using UnityEngine;

public class LeverManager : MonoBehaviour
{
    public int totalLevers = 5;
    private int activatedLevers = 0;

    public GameObject winScreen;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        winScreen.SetActive(false);
    }

    public void LeverActivated()
    {
        activatedLevers++;

        if (activatedLevers >= totalLevers)
        {
            winScreen.SetActive(true);
        }
    }
}
