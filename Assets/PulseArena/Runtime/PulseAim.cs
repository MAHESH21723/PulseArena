using UnityEngine;

namespace PulseArena
{
    /// Mobile-style aim assist for the LOCAL human pilot only (bots are driven by BotBrain).
    /// PulseInput calls Tick every frame; the HUD reads OnTarget. Presentation/input only:
    /// the server still decides every hit (see ArenaPlayer bullet magnetism).
    public static class PulseAim
    {
        /// Nearest alive enemy inside the aim cone and range with line of sight, else null.
        public static ArenaPlayer Target;
        public static bool OnTarget => Target != null;
        /// PlayerPrefs "autoFire" (default off): fire while the crosshair is on an enemy.
        public static bool AutoFire;
        /// 0..1 strength, PlayerPrefs "aimAssist" (default 1).
        public static float Assist = 1f;

        public const string AutoFireKey = "autoFire", AssistKey = "aimAssist";
        const float MaxRange = 60f, CenterMassHeight = 1.15f;

        public static float TargetAngle, TargetCone = 8f, TargetDistance;
        static float errYaw, errPitch;
        static int nextLoad;

        public static void LoadSettings()
        {
            AutoFire = PlayerPrefs.GetInt(AutoFireKey, 0) != 0;
            Assist = Mathf.Clamp01(PlayerPrefs.GetFloat(AssistKey, 1f));
        }

        public static void Clear() { Target = null; }

        /// Look-sensitivity multiplier: slows the drag as the crosshair nears the target (friction).
        public static float LookScale =>
            Target != null && Assist > 0f ? Mathf.Lerp(1f, .5f, Assist * (1f - Mathf.Clamp01(TargetAngle / TargetCone))) : 1f;

        /// True when auto-fire should hold the trigger (crosshair tightly on an enemy).
        public static bool AutoFireWanted(WeaponDef def) =>
            AutoFire && Target != null && TargetAngle < 3f && TargetDistance <= def.Range;

        /// Finds the target for the local pilot looking along (yaw, pitch).
        public static void Tick(ArenaPlayer local, float yaw, float pitch)
        {
            if (Time.frameCount >= nextLoad) { nextLoad = Time.frameCount + 90; LoadSettings(); }
            Target = null;
            if (!local || !local.Alive || Assist <= 0f && !AutoFire) return;

            Vector3 eye = local.Eye;
            Vector3 fwd = Quaternion.Euler(pitch, yaw, 0) * Vector3.forward;
            float bestScore = float.MaxValue;
            var all = ArenaPlayer.All;
            for (int i = 0; i < all.Count; i++)
            {
                var p = all[i];
                if (!p || p == local || !p.Alive) continue;
                Vector3 d = p.transform.position + Vector3.up * CenterMassHeight - eye;
                float dist = d.magnitude;
                if (dist < .5f || dist > MaxRange) continue;
                Vector3 dir = d / dist;
                float angle = Vector3.Angle(fwd, dir);
                float cone = Mathf.Lerp(10f, 4.5f, Mathf.Clamp01(dist / 40f));
                if (angle > cone) continue;
                float score = angle + dist * .05f;
                if (score >= bestScore) continue;
                if (!Physics.Raycast(eye, dir, out var hit, dist + .5f, ~0, QueryTriggerInteraction.Ignore)) continue;
                if (hit.collider.GetComponentInParent<ArenaPlayer>() != p) continue;

                bestScore = score;
                Target = p;
                TargetAngle = angle;
                TargetCone = cone;
                TargetDistance = dist;
                errYaw = Mathf.DeltaAngle(yaw, Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg);
                errPitch = -Mathf.Asin(Mathf.Clamp(dir.y, -1f, 1f)) * Mathf.Rad2Deg - pitch;
            }
        }

        /// Gentle magnetism toward the target's centre mass (degrees to add this frame).
        /// Only while moving or firing, weaker at range, weaker while the player is actively dragging.
        public static Vector2 Pull(float dt, bool moving, bool firing, bool dragging)
        {
            if (Target == null || Assist <= 0f || (!moving && !firing)) return Vector2.zero;
            float falloff = 1f - Mathf.Clamp01((TargetDistance - 8f) / 50f) * .6f;
            float centre = Mathf.SmoothStep(0f, 1f, TargetAngle / 1.5f);   // no jitter once centred
            float k = 3f * Assist * falloff * (firing ? 1f : .6f) * (dragging ? .35f : 1f) * centre;
            float t = 1f - Mathf.Exp(-k * dt);
            Vector2 step = new Vector2(errYaw * t, errPitch * t);
            float max = 90f * dt;
            if (step.magnitude > max) step = step.normalized * max;
            return step;
        }
    }
}
