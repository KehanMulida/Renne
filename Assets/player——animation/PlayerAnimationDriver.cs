using UnityEngine;
using System.Collections;

/// <summary>Connects the existing tactical player to the imported Generic animation rig.</summary>
[DisallowMultipleComponent]
public sealed class PlayerAnimationDriver : MonoBehaviour
{
    [SerializeField] private Animator animator;
    private PlayerController player;
    private UnitMovement movement;
    private EquipmentManager equipment;
    private CapsuleCollider capsule;
    private int pendingAction; // 1 = throw, 2 = pickup
    private int actionStance;
    private bool actionPlaying;
    private float actionDeadline;
    private bool gridStepActive;
    private int gridStepStance;
    private float gaitPhase;
    private readonly RaycastHit[] groundHits = new RaycastHit[12];
    public float GaitPhase => gaitPhase;
    public bool UseGridCadence => animator && animator.runtimeAnimatorController &&
        System.Array.Exists(animator.parameters, p => p.name == "GaitPhase");
    public float StanceSpeedMultiplier => player && player.IsProne ? .6f : player && player.IsCrouching ? .75f : 1.2f;
    private static readonly int StanceId = Animator.StringToHash("Stance");
    private static readonly int MovingId = Animator.StringToHash("IsMoving");
    public Animator Animator => animator;
    public bool IsPerformingAction => pendingAction != 0 || actionPlaying;

    public void Configure(Animator value) { animator = value; }

    public bool IsGridLocomotionReady
    {
        get
        {
            if (!animator || !isActiveAndEnabled || IsPerformingAction || animator.IsInTransition(0)) return false;
            int stance = player ? (int)player.CurrentStance : 0;
            return animator.GetCurrentAnimatorStateInfo(0).IsName(stance == 2 ? "ProneCrawl" : stance == 1 ? "CrouchWalk" : "Walk");
        }
    }

    public IEnumerator WaitForGridLocomotion()
    {
        while (animator && isActiveAndEnabled)
        {
            if (IsGridLocomotionReady) yield break;
            yield return null;
        }
    }

    public void BeginGridStep()
    {
        gridStepActive = true; gridStepStance = player ? (int)player.CurrentStance : 0;
        SetGridProgress(0);
    }
    public void SetGridProgress(float progress)
    {
        gaitPhase = Mathf.Clamp01(progress);
        if (animator) animator.SetFloat("GaitPhase", gaitPhase);
    }
    public void EndGridStep() { SetGridProgress(1); gridStepActive = false; }
    public void CancelGridStep() { gridStepActive = false; SetGridProgress(0); }

    private void Awake()
    {
        player = GetComponent<PlayerController>();
        movement = GetComponent<UnitMovement>();
        equipment = GetComponent<EquipmentManager>();
        capsule = GetComponent<CapsuleCollider>();
        if (!animator) animator = GetComponentInChildren<Animator>();
        if (animator) { animator.applyRootMotion = false; animator.cullingMode = AnimatorCullingMode.AlwaysAnimate; }
    }

    private void OnEnable()
    {
        if (!equipment) equipment = GetComponent<EquipmentManager>();
        if (equipment) equipment.OnItemThrown += PlayThrow;
    }

    private void OnDisable()
    {
        if (equipment) equipment.OnItemThrown -= PlayThrow;
        pendingAction = 0; actionPlaying = false; gridStepActive = false;
    }

    public void PlayThrow(Vector3 target)
    {
        if (!animator) return;
        FaceTarget(target);
        pendingAction = 1;
        actionStance = 0; // The authored throw is standing; return to gameplay stance afterwards.
    }

    public void PlayPickup(Vector3 target)
    {
        if (!animator || IsPerformingAction) return;
        FaceTarget(target);
        pendingAction = 2;
        actionStance = player && player.CurrentStance != Stance.Stand ? 1 : 0;
    }

    private void FaceTarget(Vector3 target)
    {
        Vector3 direction = target - transform.position; direction.y = 0;
        if ((!movement || !movement.IsMoving) && direction.sqrMagnitude > .001f)
            transform.rotation = Quaternion.LookRotation(direction);
    }

    private void Update()
    {
        if (!animator || !animator.runtimeAnimatorController) return;
        int stance = player ? (int)player.CurrentStance : 0;
        var state = animator.GetCurrentAnimatorStateInfo(0);
        if (actionPlaying && !animator.IsInTransition(0))
        {
            bool inAction = state.IsTag("Action");
            if ((inAction && state.normalizedTime >= .98f) || Time.time > actionDeadline)
                actionPlaying = false;
        }
        int visualStance = gridStepActive ? gridStepStance : IsPerformingAction ? actionStance : stance;
        animator.SetInteger(StanceId, visualStance);
        animator.SetBool(MovingId, movement && movement.IsMoving && (gridStepActive || !IsPerformingAction));

        if (pendingAction != 0 && !gridStepActive && !actionPlaying && !animator.IsInTransition(0))
        {
            bool ready = visualStance == 0
                ? state.IsName("StandIdle") || state.IsName("Walk")
                : state.IsName("CrouchIdle") || state.IsName("CrouchWalk");
            if (ready)
            {
                animator.ResetTrigger("Throw"); animator.ResetTrigger("Pickup");
                animator.SetTrigger(pendingAction == 1 ? "Throw" : "Pickup");
                pendingAction = 0; actionPlaying = true; actionDeadline = Time.time + 4f;
            }
        }
        UpdateCapsule(stance);
    }

    private void UpdateCapsule(int stance)
    {
        if (!capsule) return;
        // Player root remains on the grid floor; the visual skeleton supplies its own posture height.
        if (stance == 2)
        {
            capsule.direction = 2; capsule.radius = .22f; capsule.height = 1.8f;
            capsule.center = new Vector3(0, .23f, 0);
        }
        else
        {
            float height = stance == 1 ? 1.12f : 1.92f;
            capsule.direction = 1; capsule.radius = .3f; capsule.height = height;
            capsule.center = Vector3.up * (height * .5f);
        }
    }

    private void LateUpdate()
    {
        if (!animator || animator.transform == transform) return;
        float ground = transform.position.y;
        float highest = float.NegativeInfinity;
        int count = Physics.RaycastNonAlloc(transform.position + Vector3.up * .4f, Vector3.down,
            groundHits, 1.4f, 1 << 6, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
            if (!groundHits[i].transform.IsChildOf(transform) && groundHits[i].point.y <= transform.position.y + .3f)
                highest = Mathf.Max(highest, groundHits[i].point.y);
        if (!float.IsNegativeInfinity(highest)) ground = highest;
        Vector3 position = animator.transform.position;
        position.y = ground + .002f;
        animator.transform.position = position;
    }
}
