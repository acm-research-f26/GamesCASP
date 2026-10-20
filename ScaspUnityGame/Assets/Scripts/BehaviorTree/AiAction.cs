using UnityEngine;

// send AiAction objects to ExecuteAction()
public class AiAction
{
    public string action;
    public string target;

    public AiAction(string action, string target)
    {
        this.action = action;
        this.target = target;
    }
}
