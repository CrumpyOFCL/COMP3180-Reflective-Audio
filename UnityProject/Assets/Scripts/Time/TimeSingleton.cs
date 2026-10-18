using UnityEngine;

public class TimeSingleton : TimeSwitchSubject 
{
    #region - Singleton Boilerplate - 
    private static TimeSingleton _instance;
    public static TimeSingleton Instance 
    { 
        get
        {
            if (isQuitting)
            {
                return null;
            }
            
            // Prefer one already in the scene. Building a fresh object here
            // while the real one was still waiting on its Awake meant the scene
            // copy destroyed itself a moment later.
            if (_instance == null)
                _instance = FindAnyObjectByType<TimeSingleton>();

            if (_instance == null)
            {
                GameObject timeManager = new GameObject("TimeManager");
                DontDestroyOnLoad(timeManager);
                _instance = timeManager.AddComponent<TimeSingleton>();
            }

            return _instance;
        } 
    }
    #endregion

    public enum GameState { Past = 0, Present = 1, Future = 2 }
    public const int EraCount = 3;

    // Index must match the GameState enum order.
    private static readonly string[] eraLayerNames = { "Past", "Present", "Future" };

    public GameState currentGameState { get; private set; } = GameState.Present;

    private readonly int[] eraLayers = new int[EraCount];
    private int layerPlayer;
    private int layerBullets;

    private static bool isQuitting = false;


    // Statics survive a play session when Reload Domain is off, which is the
    // default fast-enter-playmode setup. Without this, isQuitting stays true
    // from the previous run and Instance returns null forever - silently taking
    // the entire era system with it on the second press of play.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        _instance = null;
        isQuitting = false;
    }

    // Masks are read every frame by ground checks and beam code, so they are
    // built once in Awake and refreshed only when the era actually changes.
    private int allErasMask;
    private int activeEraMask;
    private int inactiveEraMask;

    // Layer index of the era that is currently solid.
    public int ActiveEraLayer => eraLayers[(int)currentGameState];

    // Mask containing only the era layer that is currently solid.
    public LayerMask ActiveEraMask => activeEraMask;

    // Mask containing every era layer except the active one.
    public LayerMask InactiveEraMask => inactiveEraMask;

    public LayerMask AllErasMask => allErasMask;


    private void CacheMasks()
    {
        allErasMask = 0;
        for (int i = 0; i < EraCount; i++)
            allErasMask |= 1 << eraLayers[i];

        activeEraMask = 1 << ActiveEraLayer;
        inactiveEraMask = allErasMask & ~activeEraMask;
    }


    private void Awake()
    {
        // Must survive section unloads. Auto-created into whichever scene is
        // active, this used to die with the first section and take the era and
        // every registered observer with it.
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }

        _instance = this;

        if (transform.parent != null)
        {
            // DontDestroyOnLoad only works on root objects.
            transform.SetParent(null, true);
        }

        DontDestroyOnLoad(gameObject);

        for (int i = 0; i < EraCount; i++)
        {
            eraLayers[i] = LayerMask.NameToLayer(eraLayerNames[i]);

            if (eraLayers[i] < 0)
                Debug.LogError($"From TimeSingleton: layer \"{eraLayerNames[i]}\" does not exist. Add it under Project Settings > Tags and Layers.");
        }

        layerPlayer  = LayerMask.NameToLayer("Player");
        layerBullets = LayerMask.NameToLayer("Bullets");

        CacheMasks();

        SeparateEraLayers();
        ApplyCollisionMatrix(currentGameState);
        NotifyObservers(currentGameState);
    }


    // Observers register in their own Start, which may run after ours, so the
    // notify in Awake reaches nobody. Waiting one frame guarantees everything
    // has registered and gets a correct starting era without needing a swap.
    private System.Collections.IEnumerator Start()
    {
        yield return null;
        NotifyObservers(currentGameState);
    }


    private void OnApplicationQuit()
    {
        isQuitting = true;
    }


    private void OnDestroy()
    {
        if (_instance == this) _instance = null;
    }



    public int EraLayerIndex(GameState era) => eraLayers[(int)era];

    public LayerMask EraMask(GameState era) => 1 << eraLayers[(int)era];


    // True if the given layer index is one of the era layers.
    // Objects on non-era layers (shared geometry, props) return false.
    public bool TryGetStateFromLayer(int layerIndex, out GameState era)
    {
        for (int i = 0; i < EraCount; i++)
        {
            if (eraLayers[i] == layerIndex)
            {
                era = (GameState)i;
                return true;
            }
        }

        era = currentGameState;
        return false;
    }


    // Re-notify observers without changing era. Used after freeze/unfreeze.
    public void UpdateTime()
    {
        NotifyObservers(currentGameState);
    }


    // Move directly to a specific era. No-op if already there.
    public void SetTime(GameState target)
    {
        if (target == currentGameState) return;

        currentGameState = target;
        CacheMasks();
        ApplyCollisionMatrix(currentGameState);
        NotifyObservers(currentGameState);

        Dev.Log("From TimeSingleton: Time = " + currentGameState);
    }


    // Step along the Past <-> Present <-> Future chain. Wraps at both ends.
    public void StepTime(int direction)
    {
        SetTime(PeekStep(direction));
    }


    // The era a step in this direction would land on, without moving.
    public GameState PeekStep(int direction)
    {
        int next = ((int)currentGameState + direction) % EraCount;
        if (next < 0) next += EraCount;
        return (GameState)next;
    }


    // Era layers never collide with each other. Only needs setting once.
    private void SeparateEraLayers()
    {
        for (int a = 0; a < EraCount; a++)
        {
            for (int b = a + 1; b < EraCount; b++)
                Physics2D.IgnoreLayerCollision(eraLayers[a], eraLayers[b], true);
        }
    }


    private void ApplyCollisionMatrix(GameState currentState)
    {
        for (int i = 0; i < EraCount; i++)
        {
            bool isActive = (i == (int)currentState);

            Physics2D.IgnoreLayerCollision(layerPlayer,  eraLayers[i], !isActive);
            Physics2D.IgnoreLayerCollision(layerBullets, eraLayers[i], !isActive);
        }
    }
}