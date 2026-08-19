using System;
using System.Threading.Tasks;
using JetBrains.Annotations;

namespace UniMob.UI.Widgets
{
    public static class BackActionOwnerExtensions
    {
        /// <summary>
        ///     Makes the back button ask the topmost route to pop, carrying <see cref="PopRequest.Back"/>.
        ///     Back is chrome: it does not own the route, so it asks rather than pops.
        /// </summary>
        [PublicAPI]
        public static TBackActionOwner WithPopOnBack<TBackActionOwner>(this TBackActionOwner owner,
            NavigatorState navigatorState, Func<bool> filter = null)
            where TBackActionOwner : IBackActionOwner
        {
            bool HandleBack()
            {
                if (filter == null || filter.Invoke())
                {
                    if (navigatorState.NavigationStack.Count > 0)
                    {
                        ReportIfFaulted(navigatorState.RequestPop(navigatorState.TopmostRoute, PopRequest.Back));
                    }

                    return true;
                }

                return false;
            }

            owner.SetBackAction(HandleBack);

            return owner;
        }

        /// <summary>
        ///     Observes a request nobody awaits, so that a failure in it is reported rather than lost.
        /// </summary>
        /// <remarks>
        ///     A back press has no caller to hand the outcome to, and the route's decision runs outside the
        ///     navigator's command loop, so the loop's own catch never sees it fail. Unity does not report
        ///     a faulted task that nobody observes, which would leave a hook that throws on back failing in
        ///     silence. Routed to the zone, which is where the package reports everything else it runs on
        ///     nobody's behalf. A cancelled request is not a failure and is not reported.
        /// </remarks>
        private static void ReportIfFaulted(Task request)
        {
            request.ContinueWith(
                faulted => Zone.Current.HandleUncaughtException(faulted.Exception.GetBaseException()),
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
        }
    }
}
