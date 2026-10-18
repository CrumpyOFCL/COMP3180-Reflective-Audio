using System.Collections.Generic;
using UnityEngine;

// SHOWCASE: the aim laser. The game drew it with BeamTrajectory, which works out
// mirror bounces through BeamManager and BeamMirror; none of those are in this
// repo, so this draws the showcase beam's actual path instead: straight from the
// spawn point along the aim, stopping at the first wall (ShowcaseBeam has no
// bounces either).
//
// Put it on Player_New next to PlayerAimAndShoot. It makes its own line unless
// you assign one, and it shows and hides with the aim ring.
public class ShowcaseAimLaser : MonoBehaviour
{
    [SerializeField] private PlayerAimAndShoot aim;

    [Tooltip("Left empty, a line is created on a child object.")]
    [SerializeField] private LineRenderer line;

    [Header("Path")]
    [Tooltip("What stops the laser. Left as Nothing, it uses Solid plus the eras.")]
    [SerializeField] private LayerMask hitLayers;
    [Min(1f)][SerializeField] private float maxLength = 40f;

    [Header("Look")]
    [Min(0.001f)][SerializeField] private float width = 0.05f;
    [SerializeField] private Color startColor = new Color(0.45f, 0.9f, 1f, 0.9f);
    [SerializeField] private Color endColor = new Color(0.45f, 0.9f, 1f, 0.25f);
    [SerializeField] private string sortingLayer = "Default";
    [SerializeField] private int sortingOrder = 50;

    private ContactFilter2D filter;
    private readonly List<RaycastHit2D> hits = new List<RaycastHit2D>(4);

    private void Awake()
    {
        if (aim == null) aim = GetComponentInParent<PlayerAimAndShoot>();
        if (aim == null) aim = GetComponentInChildren<PlayerAimAndShoot>();
        if (aim == null)
        {
            Debug.LogError("From ShowcaseAimLaser: no PlayerAimAndShoot found.", this);
            enabled = false;
            return;
        }

        if (hitLayers.value == 0)
            hitLayers = LayerMask.GetMask("Solid", "Past", "Present", "Future");

        filter = new ContactFilter2D { useTriggers = false, useLayerMask = true };

        if (line == null) line = CreateLine();
        line.enabled = false;
    }

    private LineRenderer CreateLine()
    {
        var go = new GameObject("Aim Laser");
        go.transform.SetParent(transform, false);

        var lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = true;
        lr.positionCount = 2;
        lr.numCapVertices = 2;
        lr.startWidth = width;
        lr.endWidth = width;
        lr.startColor = startColor;
        lr.endColor = endColor;
        lr.sortingLayerName = sortingLayer;
        lr.sortingOrder = sortingOrder;

        // Unlit, so it reads the same with or without 2D lights.
        Shader shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
        if (shader == null) shader = Shader.Find("Sprites/Default");
        if (shader != null) lr.sharedMaterial = new Material(shader);

        return lr;
    }

    private void LateUpdate()
    {
        // Same rule PlayerAimAndShoot uses for the ring and cursor.
        bool visible = aim.isActiveAndEnabled && (aim.AllowShootingWhileMoving || !aim.IsPlayerMoving);
        line.enabled = visible;
        if (!visible) return;

        Vector2 origin = aim.BeamOrigin;
        Vector2 direction = aim.BeamDirection;

        filter.layerMask = ActiveLayers();
        hits.Clear();
        float length = maxLength;
        int count = Physics2D.Raycast(origin, direction, filter, hits, maxLength);
        for (int i = 0; i < count; i++)
            length = Mathf.Min(length, hits[i].distance);

        float z = aim.transform.position.z - 0.05f;
        line.SetPosition(0, new Vector3(origin.x, origin.y, z));
        line.SetPosition(1, new Vector3(origin.x + direction.x * length, origin.y + direction.y * length, z));
    }

    // Walls in the eras the player isn't in are passable, as for the ground check.
    private LayerMask ActiveLayers()
    {
        TimeSingleton time = TimeSingleton.Instance;
        return time != null ? (LayerMask)(hitLayers & ~time.InactiveEraMask) : hitLayers;
    }

    private void OnDisable()
    {
        if (line != null) line.enabled = false;
    }
}
