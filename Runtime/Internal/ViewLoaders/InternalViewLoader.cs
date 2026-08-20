using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UniMob.UI.Internal.ViewLoaders
{
    internal class InternalViewLoader : IViewLoader
    {
        private readonly Dictionary<string, IViewFactory> _factories =
            new Dictionary<string, IViewFactory>();
        private readonly Dictionary<string, string> _registrars = new Dictionary<string, string>();
        private readonly Dictionary<string, IView> _cache = new Dictionary<string, IView>();

        private GameObject templatesRootObject;

        public InternalViewLoader()
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var attributes = assembly.GetCustomAttributes(
                    typeof(RegisterViewFactoryAttribute),
                    false
                );
                foreach (RegisterViewFactoryAttribute attribute in attributes)
                {
                    var factory = attribute.CreateFactory();
                    if (factory == null)
                    {
                        continue;
                    }

                    var name = factory.Name;
                    var registrar = assembly.GetName().Name;

                    // A throw rather than a log: the loser of a name collision is decided by assembly
                    // load order, so logging it leaves half the app rendering the wrong view and the
                    // other half working. Both registrars are named because neither one alone can be
                    // assumed to be the mistake.
                    if (_registrars.TryGetValue(name, out var owner))
                    {
                        throw new InvalidOperationException(
                            $"Two assemblies register a view named '{name}': {owner} and "
                                + $"{registrar}. Registered view names are global, so give one of "
                                + "them a name of its own."
                        );
                    }

                    _factories.Add(name, factory);
                    _registrars.Add(name, registrar);
                }
            }
        }

        public IView LoadViewPrefab(WidgetViewReference viewReference)
        {
            if (viewReference.Type != WidgetViewReferenceType.Registered)
            {
                return null;
            }

            var name = viewReference.Path;

            if (_cache.TryGetValue(name, out var view))
            {
                return view;
            }

            if (!_factories.TryGetValue(name, out var factory))
            {
                throw new InvalidOperationException(
                    $"No view is registered under the name '{name}'. Register one with "
                        + $"[assembly: {nameof(RegisterComponentViewFactoryAttribute)}] or "
                        + $"[assembly: {nameof(RegisterCustomViewFactoryAttribute)}]."
                );
            }

            if (templatesRootObject == null)
            {
                templatesRootObject = new GameObject("UniMob Runtime View Templates");
                Object.DontDestroyOnLoad(templatesRootObject);
            }

            var template = factory.Create();
            template.transform.SetParent(templatesRootObject.transform, worldPositionStays: true);

            view = template.GetComponent<IView>();

            _cache.Add(name, view);

            return view;
        }
    }
}
