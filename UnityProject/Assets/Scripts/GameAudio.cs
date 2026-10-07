using System;
using FMOD.Studio;
using FMODUnity;
using UnityEngine;

// Small shared helpers so gameplay scripts only need one line per sound.
// Every call is a no-op when the EventReference is left empty in the Inspector,
// so adding a slot never changes behaviour until someone assigns an event.
public static class GameAudio
{
    /// <summary>Fire-and-forget one-shot at a world position (3D events) or anywhere (2D events).</summary>
    public static void PlayOneShot(EventReference sound, Vector3 position)
    {
        if (sound.IsNull) return;
        RuntimeManager.PlayOneShot(sound, position);
    }

    /// <summary>2D one-shot (UI, stingers, HUD). Position is ignored by 2D events.</summary>
    public static void PlayOneShot2D(EventReference sound)
    {
        if (sound.IsNull) return;
        RuntimeManager.PlayOneShot(sound);
    }

    /// <summary>One-shot that follows a moving object until it finishes.</summary>
    public static void PlayOneShotAttached(EventReference sound, GameObject owner)
    {
        if (sound.IsNull || owner == null) return;
        RuntimeManager.PlayOneShotAttached(sound, owner);
    }

    /// <summary>One-shot with one local parameter set before it starts (create, set, start, release).</summary>
    public static void PlayOneShot(EventReference sound, Vector3 position, string parameter, float value)
    {
        if (sound.IsNull) return;

        EventInstance instance = RuntimeManager.CreateInstance(sound);
        instance.set3DAttributes(RuntimeUtils.To3DAttributes(position));
        if (!string.IsNullOrEmpty(parameter)) instance.setParameterByName(parameter, value);
        instance.start();
        instance.release();
    }

    /// <summary>One-shot with one labeled parameter set before it starts.</summary>
    public static void PlayOneShotWithLabel(EventReference sound, Vector3 position, string parameter, string label)
    {
        if (sound.IsNull) return;

        EventInstance instance = RuntimeManager.CreateInstance(sound);
        instance.set3DAttributes(RuntimeUtils.To3DAttributes(position));
        if (!string.IsNullOrEmpty(parameter) && !string.IsNullOrEmpty(label))
            instance.setParameterByNameWithLabel(parameter, label);
        instance.start();
        instance.release();
    }

    /// <summary>
    /// For OnDestroy / OnDisable paths: true only while FMOD is running, so a
    /// sound requested during scene teardown or app quit can't spin up a new
    /// RuntimeManager.
    /// </summary>
    public static bool IsAlive => RuntimeManager.IsInitialized;
}

// A looping event owned by one object: started once, stopped and released once.
// Serializable so it shows in the Inspector as a single "Sound" slot.
[Serializable]
public class LoopingSound
{
    [SerializeField] private EventReference sound;

    private EventInstance instance;

    public bool IsAssigned => !sound.IsNull;
    public bool IsPlaying => instance.isValid();

    /// <summary>Starts the loop attached to the owner (3D follows it). Does nothing if already playing.</summary>
    public void Play(GameObject owner)
    {
        if (sound.IsNull || instance.isValid() || owner == null) return;

        instance = RuntimeManager.CreateInstance(sound);

        Rigidbody2D body = owner.GetComponent<Rigidbody2D>();
        if (body != null)
            RuntimeManager.AttachInstanceToGameObject(instance, owner, body);
        else
            RuntimeManager.AttachInstanceToGameObject(instance, owner);

        instance.start();
    }

    /// <summary>Stops and releases. Safe to call any number of times, and during teardown.</summary>
    public void Stop(bool allowFadeout = true)
    {
        if (!instance.isValid()) return;

        if (RuntimeManager.IsInitialized)
        {
            instance.stop(allowFadeout
                ? FMOD.Studio.STOP_MODE.ALLOWFADEOUT
                : FMOD.Studio.STOP_MODE.IMMEDIATE);
            instance.release();
        }

        instance.clearHandle();
    }

    public void SetParameter(string name, float value)
    {
        if (instance.isValid() && !string.IsNullOrEmpty(name))
            instance.setParameterByName(name, value);
    }

    public void SetPaused(bool paused)
    {
        if (instance.isValid()) instance.setPaused(paused);
    }
}
