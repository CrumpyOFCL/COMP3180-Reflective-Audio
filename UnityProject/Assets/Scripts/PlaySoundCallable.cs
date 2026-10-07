using FMODUnity;
using UnityEngine;

// ADDED (round 2). Plays an FMOD event from any CallScript list - no code change
// needed in the calling script. Tyler's puzzles already expose these lists:
//   SequenceLever.callOnAccepted / callOnRejected / callOnReset,
//   PuzzleSequenceLock.callOnSolved / callOnWrong, ObjectiveGate.callWhenMet,
//   ElementLift / ElementFerry onDeparted / onArrived / onBlocked,
//   ElementalReceptor onCharged / onRejected / onEmptied,
//   RiverCrossingPuzzle onFailed / onStaged / onSolved, ElementalPuzzle.onSolved,
//   FlyingBook.onLandedSafely, AreaTrigger.callScript, PressurePlate targets...
//
// One Shot mode: CallBehaviour plays the event once (PlayOneShot - detached,
//                ends on its own).
// Loop mode:     CallBehaviour starts a looping event attached to this object,
//                CallEnd (continuous triggers such as pressure plates) stops it.
//                Calling again while playing does nothing.
public class PlaySoundCallable : CallScript
{
    private enum Mode { OneShot, Loop }

    [SerializeField] private Mode mode = Mode.OneShot;

    [Tooltip("One Shot mode: event to play.")]
    [SerializeField] private EventReference oneShot;

    [Tooltip("Loop mode: looping event, started by CallBehaviour, stopped by CallEnd.")]
    [SerializeField] private LoopingSound loop = new LoopingSound();

    [Tooltip("Where the sound plays. Empty = this object.")]
    [SerializeField] private Transform emitter;

    [Tooltip("Optional local parameter set on One Shot events (e.g. a labeled 'State').")]
    [SerializeField] private string parameterName = "";
    [SerializeField] private string parameterLabel = "";

    private Transform Emitter => emitter != null ? emitter : transform;

    public override void CallBehaviour()
    {
        if (mode == Mode.Loop)
        {
            loop.Play(Emitter.gameObject);
            return;
        }

        if (!string.IsNullOrEmpty(parameterName) && !string.IsNullOrEmpty(parameterLabel))
            GameAudio.PlayOneShotWithLabel(oneShot, Emitter.position, parameterName, parameterLabel);
        else
            GameAudio.PlayOneShot(oneShot, Emitter.position);
    }

    public override void CallEnd()
    {
        if (mode == Mode.Loop) loop.Stop();
    }

    private void OnDisable()
    {
        loop.Stop();
    }
}
