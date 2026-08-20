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
    /// <remarks>
    ///     The execution order pins a sequence the drivers already ran in: the zone ticks and drains,
    ///     <c>AtomScheduler</c> then actualizes what those invalidated, and the geometry ticker runs in
    ///     <c>LateUpdate</c> after both. That order was incidental -- both Update-phase components sat at
    ///     the default order, and AtomScheduler's GameObject is created lazily on first actualize, so a
    ///     component registered mid-frame could reorder them. Golden traces encode the latency it
    ///     produces, so it is pinned rather than left to registration order.
    /// </remarks>
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
