using UnityEngine;

namespace MatchRogue
{
    public enum HapticType
    {
        Light,
        Medium,
        Heavy,
        Success,
        Fail
    }

    public static class HapticManager
    {
        public static bool enableHaptics = true;

        private static HapticType? pendingType;
        private static int pendingFrame = -1;
        private static float nextAllowedTime;

        public static void Play(HapticType type)
        {
            if (!enableHaptics)
            {
                return;
            }

            if (pendingType.HasValue && pendingFrame == Time.frameCount)
            {
                if (GetPriority(type) > GetPriority(pendingType.Value))
                {
                    pendingType = type;
                }

                return;
            }

            Flush();
            pendingType = type;
            pendingFrame = Time.frameCount;
        }

        public static void Flush()
        {
            if (!pendingType.HasValue)
            {
                return;
            }

            var type = pendingType.Value;
            pendingType = null;
            pendingFrame = -1;

            if (!enableHaptics || Time.unscaledTime < nextAllowedTime)
            {
                return;
            }

            nextAllowedTime = Time.unscaledTime + GetCooldown(type);
            Execute(type);
        }

        public static void Light()
        {
            Play(HapticType.Light);
        }

        public static void Medium()
        {
            Play(HapticType.Medium);
        }

        public static void Heavy()
        {
            Play(HapticType.Heavy);
        }

        public static void Success()
        {
            Play(HapticType.Success);
        }

        public static void Fail()
        {
            Play(HapticType.Fail);
        }

        private static int GetPriority(HapticType type)
        {
            switch (type)
            {
                case HapticType.Success:
                case HapticType.Fail:
                    return 4;
                case HapticType.Heavy:
                    return 3;
                case HapticType.Medium:
                    return 2;
                default:
                    return 1;
            }
        }

        private static float GetCooldown(HapticType type)
        {
            switch (type)
            {
                case HapticType.Success:
                case HapticType.Fail:
                    return 0.24f;
                case HapticType.Heavy:
                    return 0.20f;
                case HapticType.Medium:
                    return 0.12f;
                default:
                    return 0.08f;
            }
        }

        private static void Execute(HapticType type)
        {
#if UNITY_EDITOR
            Debug.Log($"[Haptic] {type}");
#elif UNITY_ANDROID
            Handheld.Vibrate();
#else
            Debug.Log($"[Haptic] {type}");
#endif
        }
    }
}
