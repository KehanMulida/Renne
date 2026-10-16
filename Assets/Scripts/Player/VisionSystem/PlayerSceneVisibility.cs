using System.Collections.Generic;
using UnityEngine;

/// <summary>玩家视觉信息边界。只控制呈现和玩家交互，不影响 AI、碰撞或声音。</summary>
[DefaultExecutionOrder(900)]
public sealed class PlayerSceneVisibility : MonoBehaviour
{
    public static PlayerSceneVisibility Instance { get; private set; }
    [SerializeField] private Transform player;
    [SerializeField] private PlayerLightFog fog;
    [SerializeField] private LayerMask walls = 129;
    [SerializeField] private float eyeHeight = 0.9f;
    [SerializeField] private float floorTolerance = 1.5f;
    [SerializeField] private float refreshInterval = 0.1f;
    private float nextRefresh, nextScan;
    private MeshRenderer[] architecture;
    private readonly List<Transform> targets = new List<Transform>();
    private readonly Dictionary<Renderer, bool> owned = new Dictionary<Renderer, bool>();
    private readonly Dictionary<Transform, Renderer[]> renderers = new Dictionary<Transform, Renderer[]>();
    private readonly RaycastHit[] hits = new RaycastHit[128];

    public static bool CanReveal(Component target) => target != null && CanReveal(target.transform);
    public static bool CanReveal(Transform target) => target != null &&
        (Instance == null || !Instance.isActiveAndEnabled || Instance.IsVisible(target));

    private void OnEnable()
    {
        Instance = this;
        if (fog == null) fog = GetComponent<PlayerLightFog>();
        if (player == null)
        {
            var found = GameObject.FindGameObjectWithTag("Player");
            if (found != null) player = found.transform;
        }
        Rescan();
        Refresh();
    }

    private void Rescan()
    {
        architecture = FindObjectsOfType<MeshRenderer>();
        foreach (var enemy in FindObjectsOfType<EnemyAIController>()) Register(enemy.transform);
        foreach (var item in FindObjectsOfType<WorldItem>()) Register(item.transform);
        // Structural scene objects (doors, large shelves) stay intact. Their UI/interactions
        // use CanReveal instead, so hiding a door cannot open a visual hole in a room.
        nextScan = Time.unscaledTime + 1f;
    }

    private void Register(Transform target)
    {
        if (target == player || targets.Contains(target)) return;
        targets.Add(target);
        var found = target.GetComponentsInChildren<Renderer>(true);
        renderers[target] = found;
        foreach (var renderer in found)
            if (!owned.ContainsKey(renderer)) owned.Add(renderer, renderer.forceRenderingOff);
    }

    private void LateUpdate()
    {
        if (Time.unscaledTime >= nextScan) Rescan();
        if (Time.unscaledTime >= nextRefresh) Refresh();
    }

    public void Refresh()
    {
        for (int i = targets.Count - 1; i >= 0; i--)
        {
            var target = targets[i];
            if (target == null) { targets.RemoveAt(i); continue; }
            bool visible = IsVisible(target);
            foreach (var renderer in renderers[target])
                if (renderer != null) renderer.forceRenderingOff = owned[renderer] || !visible;
        }
        nextRefresh = Time.unscaledTime + refreshInterval;
    }

    public bool IsVisible(Transform target)
    {
        if (player == null || target == player || target.IsChildOf(player)) return true;
        if (!target.gameObject.activeInHierarchy) return false;
        float radius = fog != null ? fog.VisionRadius : 9f;
        if ((new Vector2(target.position.x-player.position.x,target.position.z-player.position.z)).sqrMagnitude > radius*radius ||
            Mathf.Abs(target.position.y-player.position.y) > floorTolerance) return false;
        Bounds bounds = new Bounds(target.position + Vector3.up * .35f, Vector3.one * .2f);
        var first = target.GetComponentInChildren<Renderer>();
        if (first != null) bounds = first.bounds;
        Vector3 eye = player.position + Vector3.up * eyeHeight;
        // Samples stay below eye level: a head peeking over a divider must not reveal an entire room.
        Vector3 sample = bounds.center;
        sample.y = Mathf.Clamp(sample.y, player.position.y + .1f, eye.y);
        if (Clear(eye, sample, target)) return true;
        sample.y = Mathf.Clamp(bounds.min.y + .15f, player.position.y + .05f, eye.y);
        return Clear(eye, sample, target);
    }

    private bool Clear(Vector3 eye, Vector3 point, Transform target)
    {
        Vector3 delta = point-eye;
        float distance = delta.magnitude;
        if (distance < .05f) return true;
        var ray = new Ray(eye,delta/distance);
        int count = Physics.RaycastNonAlloc(ray,hits,distance-.03f,walls,QueryTriggerInteraction.Ignore);
        if (count == hits.Length) return false; // Dense geometry fails closed instead of leaking a hidden room.
        for (int i=0;i<count;i++)
        {
            var hit=hits[i];
            var t=hit.collider.transform;
            if(t==player || t.IsChildOf(player) || t==target || t.IsChildOf(target) || target.IsChildOf(t)) continue;
            if(t.GetComponentInParent<EnemyAIController>() != null || t.GetComponentInParent<WorldItem>() != null) continue;
            if(hit.collider.bounds.size.y < .4f) continue;
            return false;
        }
        if (architecture == null) return true;
        foreach(var wall in architecture)
        {
            if(wall==null || !wall.enabled || !wall.gameObject.activeInHierarchy ||
                (walls.value & (1<<wall.gameObject.layer))==0 || wall.GetComponent<Collider>()!=null) continue;
            var t=wall.transform;
            if(t==player || t.IsChildOf(player) || t==target || t.IsChildOf(target) || target.IsChildOf(t)) continue;
            if(t.GetComponentInParent<EnemyAIController>() != null || t.GetComponentInParent<WorldItem>() != null) continue;
            var b=wall.bounds;
            if(b.size.y < .7f || Mathf.Max(b.size.x,b.size.z)<.8f || !b.IntersectRay(ray,out float along)) continue;
            if(along>.03f && along<distance-.03f) return false;
        }
        return true;
    }

    private void OnDisable()
    {
        foreach(var pair in owned) if(pair.Key != null) pair.Key.forceRenderingOff=pair.Value;
        owned.Clear(); targets.Clear(); renderers.Clear();
        if(Instance==this) Instance=null;
    }
}
