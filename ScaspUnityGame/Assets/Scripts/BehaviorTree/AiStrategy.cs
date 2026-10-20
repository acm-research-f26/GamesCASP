using UnityEngine;

public abstract class AiStrategy : MonoBehaviour
{
    // behavior tree objects will override this to store facts in their blackboards
    public abstract void storeFact(Fact theFact);

    // all aistrategy objects will perform actions in their own ways by calling ExecuteAction
    // based on whatever this returns
    // aiaction class
    public abstract AiAction getNextAction();
}
