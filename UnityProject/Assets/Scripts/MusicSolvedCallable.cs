using UnityEngine;

// ADDED (round 2). For the few "solved" moments that do NOT go through
// ObjectiveManager.CompleteObjective (a puzzle with Objective On Solved left
// empty, FlyingBook.onLandedSafely, ElementRowPuzzle re-solves, door sequences):
// add this to the object and drop it in that script's onSolved/callOnSolved list.
// It runs the same rise -> hold -> release lift on the music's Solved parameter.
public class MusicSolvedCallable : CallScript
{
    public override void CallBehaviour()
    {
        if (EraMusic.Instance != null) EraMusic.Instance.LiftSolved();
    }
}
