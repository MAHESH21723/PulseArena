using UnityEngine;

namespace PulseArena
{
    /// Drives a pilot's character Animator and raises footsteps. Lives on the ArenaPlayer object
    /// (not on the model) so it keeps running while the model is hidden, which is the case for
    /// the local first-person pilot. Footsteps use distance travelled, so they work with the
    /// Animator culled or inactive.
    [DisallowMultipleComponent]
    public sealed class PilotAnimator : MonoBehaviour
    {
        static readonly int SpeedId = Animator.StringToHash("Speed");
        static readonly int AliveId = Animator.StringToHash("Alive");
        static readonly int FireId = Animator.StringToHash("Fire");
        static readonly int HitId = Animator.StringToHash("Hit");

        const float WalkStride = 1.6f, RunStride = 2.1f, WalkSpeedLimit = 3.5f;
        const float TeleportDistance = 3f;   // bigger jumps are respawns, not movement

        ArenaPlayer pilot;
        Animator animator;
        Vector3 last;
        float speed, stepDistance;
        bool rightFoot, subscribed, hasLast;

        public Animator Animator => animator;

        public void Init(ArenaPlayer owner, Animator model)
        {
            Unsubscribe();
            pilot = owner; animator = model;
            hasLast = false; stepDistance = 0; speed = 0;
            if (isActiveAndEnabled) Subscribe();
        }

        void OnEnable() { Subscribe(); }
        void OnDisable() { Unsubscribe(); }

        void Subscribe()
        {
            if (subscribed || !pilot) return;
            subscribed = true;
            PulseEvents.Shot += OnShot;
            PulseEvents.Damaged += OnDamaged;
            PulseEvents.Eliminated += OnEliminated;
            PulseEvents.Respawned += OnRespawned;
        }

        void Unsubscribe()
        {
            if (!subscribed) return;
            subscribed = false;
            PulseEvents.Shot -= OnShot;
            PulseEvents.Damaged -= OnDamaged;
            PulseEvents.Eliminated -= OnEliminated;
            PulseEvents.Respawned -= OnRespawned;
        }

        bool Ready => animator && animator.isActiveAndEnabled && animator.runtimeAnimatorController;

        void OnShot(ArenaPlayer shooter, WeaponId weapon, Vector3 from, Vector3 to, bool hit)
        {
            if (shooter == pilot && Ready) animator.SetTrigger(FireId);
        }

        void OnDamaged(ArenaPlayer victim, ArenaPlayer attacker, int amount, Vector3 from)
        {
            if (victim == pilot && Ready && pilot.Alive) animator.SetTrigger(HitId);
        }

        void OnEliminated(ArenaPlayer victim, ArenaPlayer attacker, WeaponId weapon)
        {
            if (victim != pilot || !Ready) return;
            animator.ResetTrigger(FireId); animator.ResetTrigger(HitId);
            animator.SetBool(AliveId, false);
        }

        void OnRespawned(ArenaPlayer who)
        {
            if (who != pilot) return;
            hasLast = false; stepDistance = 0; speed = 0;
            if (!Ready) return;
            animator.ResetTrigger(FireId); animator.ResetTrigger(HitId);
            animator.SetBool(AliveId, true); animator.SetFloat(SpeedId, 0);
            animator.Play("Locomotion", 0, 0);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0) return;
            Vector3 pos = transform.position;
            if (!hasLast) { last = pos; hasLast = true; }
            Vector3 delta = pos - last; last = pos;
            float dy = delta.y; delta.y = 0;
            float dist = delta.magnitude;
            bool alive = !pilot || pilot.Alive;

            if (dist > TeleportDistance) { dist = 0; stepDistance = 0; }
            float instant = dist / dt;
            speed = Mathf.Lerp(speed, alive ? instant : 0, 1 - Mathf.Exp(-14f * dt));
            if (speed < .05f) speed = 0;

            // Footsteps: distance based, grounded pilots only (a jump is mostly vertical motion).
            bool grounded = Mathf.Abs(dy / dt) < 2.5f;
            if (alive && grounded && instant > .6f)
            {
                stepDistance += dist;
                float stride = speed < WalkSpeedLimit ? WalkStride : RunStride;
                if (stepDistance >= stride)
                {
                    stepDistance -= stride;
                    rightFoot = !rightFoot;
                    Vector3 side = transform.right * (rightFoot ? .16f : -.16f);
                    if (pilot) PulseEvents.RaiseFootstep(pilot, pos + side);
                }
            }
            else if (instant <= .6f) stepDistance = Mathf.Min(stepDistance, RunStride * .5f);

            if (Ready)
            {
                animator.SetFloat(SpeedId, speed);
                animator.SetBool(AliveId, alive);
            }
        }
    }
}
