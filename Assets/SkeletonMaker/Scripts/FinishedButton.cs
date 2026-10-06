using UnityEngine;

namespace SkeletonMaker
{
    /// <summary>
    /// The button for "my skeleton is finished": goes to the Join stage, where
    /// the other performer appears. See PushButton for how it's pressed.
    /// </summary>
    public class FinishedButton : PushButton
    {
        [SerializeField] private Stage goesTo = Stage.Join;

        protected override string Caption => "Finished";

        protected override void Press()
        {
            if (StageController.Instance != null) StageController.Instance.Go(goesTo);
        }
    }
}
