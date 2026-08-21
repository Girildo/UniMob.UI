using UnityEngine;

namespace UniMob
{
    /// <summary>The zone the player loop drives. Adds nothing but a way to run a whole frame.</summary>
    internal sealed class DrivenZone : Zone
    {
        internal void Drive(float deltaTime)
        {
            RunTickers(deltaTime);
            DrainNextFrame();
        }
    }

    /// <summary>
    ///     Connects the zone to Unity's player loop. The only part of the clock that needs a scene.
    /// </summary>
    // Pins the zone ahead of AtomScheduler, which both sat at the default order. The navigator's
    // golden traces encode the resulting latency, so this is not cosmetic. See CONTEXT.md.
    [DefaultExecutionOrder(-1000)]
    internal sealed class ZoneDriver : MonoBehaviour
    {
        private readonly DrivenZone _zone = new DrivenZone();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        internal static void Init()
        {
            var go = new GameObject(nameof(Zone));
            var driver = go.AddComponent<ZoneDriver>();
            DontDestroyOnLoad(go);
            DontDestroyOnLoad(driver);

            Zone.Install(driver._zone);
        }

        // Unscaled, because a UI animation should not stop when the game is paused, and because every
        // ticker read Time.unscaledDeltaTime for itself before the delta was handed to them.
        private void Update() => _zone.Drive(Time.unscaledDeltaTime);
    }
}
