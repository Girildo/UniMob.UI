using System;
using UnityEngine;

namespace UniMob.UI
{
    /// <summary>
    /// Builds the template GameObject for a view registered in source rather than authored as a
    /// prefab. Reached through <see cref="WidgetViewReference.Registered"/>.
    /// </summary>
    /// <remarks>
    /// The factory runs once per name: the object it returns is kept as a template and cloned for
    /// every instance, so it must be a fresh object with no scene dependencies.
    /// </remarks>
    public interface IViewFactory
    {
        /// <summary>The name a <see cref="WidgetViewReference.Registered"/> resolves against.</summary>
        string Name { get; }

        GameObject Create();
    }

    /// <summary>
    /// Registers a view factory with the assembly it is applied to. Every loaded assembly is
    /// scanned, so a consumer registers its own views the same way this package does.
    /// </summary>
    public abstract class RegisterViewFactoryAttribute : Attribute
    {
        public abstract IViewFactory CreateFactory();
    }

    /// <summary>
    /// Registers a view whose template needs more than one GameObject, or components configured
    /// beyond their defaults.
    /// </summary>
    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
    public class RegisterCustomViewFactoryAttribute : RegisterViewFactoryAttribute
    {
        public Type FactoryType { get; }

        public RegisterCustomViewFactoryAttribute(Type factoryType)
        {
            FactoryType = factoryType ?? throw new ArgumentNullException(nameof(factoryType));
        }

        public override IViewFactory CreateFactory()
        {
            return Activator.CreateInstance(FactoryType) as IViewFactory;
        }
    }

    /// <summary>
    /// Registers a view whose template is a single GameObject carrying the listed components at
    /// their default values. Anything more elaborate wants
    /// <see cref="RegisterCustomViewFactoryAttribute"/>.
    /// </summary>
    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
    public class RegisterComponentViewFactoryAttribute : RegisterViewFactoryAttribute
    {
        public string Name { get; }
        public Type[] Components { get; }

        public RegisterComponentViewFactoryAttribute(string name, params Type[] components)
        {
            Name = name;
            Components = components;
        }

        public override IViewFactory CreateFactory()
        {
            return new ComponentViewFactory(Name, Components);
        }
    }

    internal sealed class ComponentViewFactory : IViewFactory
    {
        public ComponentViewFactory(string name, Type[] components)
        {
            Name = name;
            Components = components;
        }

        public string Name { get; }
        public Type[] Components { get; }

        public GameObject Create()
        {
            var go = new GameObject(Name);
            go.SetActive(false);

            foreach (var component in Components)
            {
                var added = go.AddComponent(component);

                if (component == typeof(RectTransform))
                {
                    ((RectTransform)added).sizeDelta = Vector2.zero;
                }
            }

            return go;
        }
    }
}
