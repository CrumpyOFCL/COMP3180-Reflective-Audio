using System.Collections.Generic;
using UnityEngine;

public abstract class TimeSwitchSubject : MonoBehaviour
{
    // List of all observers of this subject
    private List<ITimeSwitchObserver> _observers = new List<ITimeSwitchObserver>();

    // Reused, so notifying doesn't allocate a fresh copy on every era change.
    private readonly List<ITimeSwitchObserver> _notifyBuffer = new List<ITimeSwitchObserver>();


    // Add observer from this subject's collection.
    public void AddObserver(ITimeSwitchObserver newObserver)
    {
        // Registering twice would notify twice. Anything that registers in
        // OnEnable rather than Start hits this on every re-enable.
        if (newObserver == null || _observers.Contains(newObserver)) return;

        _observers.Add(newObserver);
    }


    // Remove observer from this subject's collection.
    public void RemoveObserver(ITimeSwitchObserver newObserver)
    {
        _observers.Remove(newObserver);
    }


    // Notify all observers that the event has occured.
    protected void NotifyObservers(TimeSingleton.GameState currentTime)
    {
        // Drop dead entries first, so one stale observer can't stop the rest
        // being told.
        for (int i = _observers.Count - 1; i >= 0; i--)
        {
            ITimeSwitchObserver observer = _observers[i];

            if (observer == null || (observer is UnityEngine.Object dead && dead == null))
                _observers.RemoveAt(i);
        }

        // Notify from a copy. Observers unregister and spawn others while being
        // notified, and removing an entry below the cursor mid-loop used to
        // shift the indices and notify something a second time.
        _notifyBuffer.Clear();
        _notifyBuffer.AddRange(_observers);

        for (int i = 0; i < _notifyBuffer.Count; i++)
        {
            ITimeSwitchObserver observer = _notifyBuffer[i];

            if (observer is UnityEngine.Object unityObject && unityObject == null) continue;

            observer.OnNotify(currentTime);
        }
    }
}