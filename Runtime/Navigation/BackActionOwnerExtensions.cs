using System;
using System.Threading.Tasks;

namespace UniMob.UI.Navigation
{
    public static class BackActionOwnerExtensions
    {
        /// <summary>
        ///     Makes the back button ask the topmost route to pop, carrying <paramref name="request"/>. Back
        ///     is chrome: it does not own the route, so it asks rather than pops, and like any other asker
        ///     it says what it is asking with.
        /// </summary>
        public static TBackActionOwner WithPopOnBack<TBackActionOwner>(
            this TBackActionOwner owner,
            NavigatorState navigatorState,
            object request,
            Func<bool> filter = null
        )
            where TBackActionOwner : IBackActionOwner
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            bool HandleBack()
            {
                if (filter == null || filter.Invoke())
                {
                    if (navigatorState.NavigationStack.Count > 0)
                    {
                        ReportIfFaulted(
                            navigatorState.RequestPop(navigatorState.TopmostRoute, request)
                        );
                    }

                    return true;
                }

                return false;
            }

            owner.SetBackAction(HandleBack);

            return owner;
        }

        /// <summary>
        ///     Reports a faulted request to the zone. A back press has no caller to hand the outcome to,
        ///     and the route's decision runs outside the command loop's catch, so a hook that throws on
        ///     back would otherwise fail in silence. Cancellation is not a failure and is not reported.
        /// </summary>
        private static void ReportIfFaulted(Task request)
        {
            request.ContinueWith(
                faulted =>
                    Zone.Current.HandleUncaughtException(faulted.Exception.GetBaseException()),
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously
            );
        }
    }
}
