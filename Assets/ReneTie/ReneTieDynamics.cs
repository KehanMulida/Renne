using System;
using UnityEngine;

namespace Renne.CharacterEffects
{
    /// <summary>Five-bone tie simulation. Runs after Animator; requires exposed FBX transforms.</summary>
    [DisallowMultipleComponent, DefaultExecutionOrder(10000)]
    public sealed class ReneTieDynamics : MonoBehaviour
    {
        public Transform[] bones = new Transform[5];
        public Transform end;
        public Transform chest;
        [Min(0)] public float stiffness = 65f;
        [Min(0)] public float damping = 8f;
        [Range(0, 1)] public float gravityScale = .35f;
        [Range(0, 80)] public float maxSwing = 50f;
        [Min(0)] public float inwardAllowance = .008f;
        [Min(.01f)] public float teleportDistance = 1f;
        [Tooltip("Optional capsule/sphere colliders on the body or arms; assigned explicitly.")]
        public Collider[] bodyColliders = Array.Empty<Collider>();

        Quaternion[] rest;
        Vector3[] offsets, tips, previous, restTips;
        float[] chestDepth;
        Vector3 outwardLocal, lastAnchor;
        bool ready;
        float previousStep = 1f / 120f;

        [ContextMenu("Find tie bones")]
        public void FindBones()
        {
            var all = GetComponentsInChildren<Transform>(true);
            bones = new Transform[5];
            foreach (var t in all)
            {
                for (int i = 0; i < 5; i++)
                    if (t.name == "tie_" + (i + 1).ToString("00")) bones[i] = t;
                if (t.name == "tie_end") end = t;
                if (t.name == "spine_03.x") chest = t;
            }
        }

        void OnEnable() { ready = false; }
        void Start() { Initialize(); }
        void OnDisable()
        {
            if (rest != null && bones != null)
                for (int i = 0; i < rest.Length; i++)
                    if (bones[i]) bones[i].localRotation = rest[i];
            ready = false;
        }

        public bool Initialize()
        {
            if (ready) return true;
            if (bones == null || bones.Length != 5 || !bones[0] || !end || !chest) FindBones();
            if (bones == null || bones.Length != 5 || !end || !chest) return false;
            for (int i = 0; i < 5; i++)
                if (!bones[i] || (i > 0 && bones[i].parent != bones[i - 1])) return false;
            if (end.parent != bones[4]) return false;
            rest = new Quaternion[5]; offsets = new Vector3[5];
            tips = new Vector3[5]; previous = new Vector3[5];
            restTips = new Vector3[5]; chestDepth = new float[5];
            var normal = Vector3.ProjectOnPlane(bones[0].position - chest.position, transform.up).normalized;
            if (normal.sqrMagnitude < .5f) normal = transform.forward;
            outwardLocal = chest.InverseTransformDirection(normal);
            for (int i = 0; i < 5; i++)
            {
                rest[i] = bones[i].localRotation;
                var child = i < 4 ? bones[i + 1] : end;
                offsets[i] = child.localPosition;
                tips[i] = previous[i] = child.position;
                chestDepth[i] = Vector3.Dot(child.position - chest.position, normal);
            }
            lastAnchor = bones[0].position;
            ready = true;
            return true;
        }

        public void ResetSimulation()
        {
            if (!ready) return;
            for (int i = 0; i < 5; i++) bones[i].localRotation = rest[i];
            for (int i = 0; i < 5; i++) tips[i] = previous[i] = (i < 4 ? bones[i + 1] : end).position;
            lastAnchor = bones[0].position;
        }

        void LateUpdate() { Simulate(Time.deltaTime); }

        // Public for deterministic editor validation and custom update loops.
        public void Simulate(float deltaTime)
        {
            if (!Initialize() || deltaTime <= 0) return;
            if (deltaTime > .15f || Vector3.Distance(lastAnchor, bones[0].position) > teleportDistance)
            { ResetSimulation(); return; }
            lastAnchor = bones[0].position;
            // Restore only the dedicated tie chain. Chest/body animation remains untouched.
            for (int i = 0; i < 5; i++) bones[i].localRotation = rest[i];
            for (int i = 0; i < 5; i++) restTips[i] = (i < 4 ? bones[i + 1] : end).position;
            int steps = Mathf.CeilToInt(deltaTime / (1f / 120f));
            float dt = deltaTime / steps;
            float decay = Mathf.Exp(-Mathf.Max(0, damping) * dt);
            Vector3 normal = chest.TransformDirection(outwardLocal).normalized;
            for (int s = 0; s < steps; s++)
            {
                for (int i = 0; i < 5; i++) bones[i].localRotation = rest[i];
                for (int i = 0; i < 5; i++)
                {
                    var bone = bones[i];
                    Vector3 pivot = bone.position;
                    Vector3 baseline = bone.TransformVector(offsets[i]);
                    float length = baseline.magnitude;
                    if (length < .00001f) continue;
                    Vector3 velocity = (tips[i] - previous[i]) * (dt / previousStep) * decay;
                    Vector3 next = tips[i] + velocity + (Physics.gravity * gravityScale +
                        (restTips[i] - tips[i]) * stiffness) * (dt * dt);
                    previous[i] = tips[i];
                    Vector3 direction = Vector3.RotateTowards(baseline.normalized,
                        (next - pivot).normalized, maxSwing * Mathf.Deg2Rad, 0);
                    next = pivot + direction * length;
                    // A chest-relative envelope stops the strip crossing the shirt's rest surface.
                    float depth = chestDepth[i] - inwardAllowance * Mathf.Abs(transform.lossyScale.y);
                    for (int iteration = 0; iteration < 3; iteration++)
                    {
                        foreach (var collider in bodyColliders)
                        {
                            if (!collider || !collider.enabled || !collider.gameObject.activeInHierarchy) continue;
                            Vector3 surface = collider.ClosestPoint(next);
                            if ((surface - next).sqrMagnitude < 1e-10f)
                            {
                                // ClosestPoint returns the input for an interior point. Probe from outside.
                                Vector3 outside = next + normal * (collider.bounds.extents.magnitude * 3 + 1);
                                next = collider.ClosestPoint(outside) + normal * .003f;
                            }
                        }
                        direction = (next - pivot).normalized;
                        float required = (depth - Vector3.Dot(pivot - chest.position, normal)) / length;
                        if (Vector3.Dot(direction, normal) < required)
                        {
                            float d = Mathf.Clamp(required, -1, 1);
                            Vector3 tangent = Vector3.ProjectOnPlane(direction, normal).normalized;
                            if (tangent.sqrMagnitude < .1f) tangent = Vector3.ProjectOnPlane(baseline, normal).normalized;
                            direction = normal * d + tangent * Mathf.Sqrt(Mathf.Max(0, 1 - d * d));
                        }
                        next = pivot + direction * length;
                    }
                    tips[i] = next;
                    bone.rotation = Quaternion.FromToRotation(baseline, next - pivot) * bone.rotation;
                }
                previousStep = dt;
            }
        }
    }
}

