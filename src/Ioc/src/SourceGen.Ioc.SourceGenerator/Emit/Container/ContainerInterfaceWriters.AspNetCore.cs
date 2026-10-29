using static SourceGen.Ioc.Constants;

namespace SourceGen.Ioc;

partial class IocSourceGenerator
{
    /// <summary>
    /// Writes IControllerActivator implementation using ActivatorUtilities with ObjectFactory caching.
    /// </summary>
    private static void WriteIControllerActivatorImplementation(SourceWriter writer, ContainerModel container)
    {
        writer.WriteLine("#region IControllerActivator");
        writer.WriteLine();

        // Static cache field
        writer.WriteLine("private static readonly global::System.Collections.Concurrent.ConcurrentDictionary<global::System.Type, global::Microsoft.Extensions.DependencyInjection.ObjectFactory> _controllerFactoryCache = new();");
        writer.WriteLine("private static global::Microsoft.Extensions.DependencyInjection.ObjectFactory CreateControllerFactory(");
        writer.WriteLine("    [global::System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(");
        writer.WriteLine("        global::System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicConstructors)] global::System.Type t)");
        writer.WriteLine("    => global::Microsoft.Extensions.DependencyInjection.ActivatorUtilities.CreateFactory(t, global::System.Type.EmptyTypes);");
        writer.WriteLine();

        // Create method
        writer.WriteLine("object global::Microsoft.AspNetCore.Mvc.Controllers.IControllerActivator.Create(global::Microsoft.AspNetCore.Mvc.ControllerContext controllerContext)");
        writer.WriteLine("{");
        writer.Indentation++;
        writer.WriteLine("global::System.ArgumentNullException.ThrowIfNull(controllerContext);");
        writer.WriteLine("var controllerType = controllerContext.ActionDescriptor.ControllerTypeInfo.AsType();");
        writer.WriteLine("var instance = GetService(controllerType);");
        writer.WriteLine("if (instance is not null) return instance;");
        writer.WriteLine();
        writer.WriteLine("if (!_controllerFactoryCache.TryGetValue(controllerType, out var factory))");
        writer.WriteLine("{");
        writer.Indentation++;
        writer.WriteLine("factory = CreateControllerFactory(controllerType);");
        writer.WriteLine("_controllerFactoryCache.TryAdd(controllerType, factory);");
        writer.Indentation--;
        writer.WriteLine("}");
        writer.WriteLine();
        writer.WriteLine("return factory(this, []);");
        writer.Indentation--;
        writer.WriteLine("}");
        writer.WriteLine();

        // Release method
        writer.WriteLine("void global::Microsoft.AspNetCore.Mvc.Controllers.IControllerActivator.Release(global::Microsoft.AspNetCore.Mvc.ControllerContext context, object controller)");
        writer.WriteLine("{");
        writer.Indentation++;
        writer.WriteLine("global::System.ArgumentNullException.ThrowIfNull(context);");
        writer.WriteLine("global::System.ArgumentNullException.ThrowIfNull(controller);");
        writer.WriteLine("if (controller is global::System.IDisposable disposable)");
        writer.WriteLine("{");
        writer.Indentation++;
        writer.WriteLine("disposable.Dispose();");
        writer.Indentation--;
        writer.WriteLine("}");
        writer.Indentation--;
        writer.WriteLine("}");
        writer.WriteLine();

        // ReleaseAsync method
        writer.WriteLine("global::System.Threading.Tasks.ValueTask global::Microsoft.AspNetCore.Mvc.Controllers.IControllerActivator.ReleaseAsync(global::Microsoft.AspNetCore.Mvc.ControllerContext context, object controller)");
        writer.WriteLine("{");
        writer.Indentation++;
        writer.WriteLine("global::System.ArgumentNullException.ThrowIfNull(context);");
        writer.WriteLine("global::System.ArgumentNullException.ThrowIfNull(controller);");
        writer.WriteLine("if (controller is global::System.IAsyncDisposable asyncDisposable)");
        writer.WriteLine("{");
        writer.Indentation++;
        writer.WriteLine("return asyncDisposable.DisposeAsync();");
        writer.Indentation--;
        writer.WriteLine("}");
        writer.WriteLine();
        writer.WriteLine("((global::Microsoft.AspNetCore.Mvc.Controllers.IControllerActivator)this).Release(context, controller);");
        writer.WriteLine("return default;");
        writer.Indentation--;
        writer.WriteLine("}");
        writer.WriteLine();

        writer.WriteLine("#endregion");
        writer.WriteLine();
    }

    /// <summary>
    /// Writes IComponentActivator implementation using ActivatorUtilities with ObjectFactory caching.
    /// </summary>
    private static void WriteIComponentActivatorImplementation(SourceWriter writer, ContainerModel container)
    {
        writer.WriteLine("#region IComponentActivator");
        writer.WriteLine();

        // Static cache field
        writer.WriteLine("private static readonly global::System.Collections.Concurrent.ConcurrentDictionary<global::System.Type, global::Microsoft.Extensions.DependencyInjection.ObjectFactory> _componentFactoryCache = new();");
        writer.WriteLine("private static global::Microsoft.Extensions.DependencyInjection.ObjectFactory CreateComponentFactory(");
        writer.WriteLine("    [global::System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(");
        writer.WriteLine("        global::System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicConstructors)] global::System.Type t)");
        writer.WriteLine("    => global::Microsoft.Extensions.DependencyInjection.ActivatorUtilities.CreateFactory(t, global::System.Type.EmptyTypes);");
        writer.WriteLine();

        // CreateInstance method
        writer.WriteLine("global::Microsoft.AspNetCore.Components.IComponent global::Microsoft.AspNetCore.Components.IComponentActivator.CreateInstance([global::System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(global::System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicConstructors)] global::System.Type componentType)");
        writer.WriteLine("{");
        writer.Indentation++;
        writer.WriteLine("if (!typeof(global::Microsoft.AspNetCore.Components.IComponent).IsAssignableFrom(componentType))");
        writer.WriteLine("{");
        writer.Indentation++;
        writer.WriteLine("throw new global::System.ArgumentException($\"The type {componentType.FullName} does not implement {nameof(global::Microsoft.AspNetCore.Components.IComponent)}.\", nameof(componentType));");
        writer.Indentation--;
        writer.WriteLine("}");
        writer.WriteLine();
        writer.WriteLine("var instance = GetService(componentType);");
        writer.WriteLine("if (instance is global::Microsoft.AspNetCore.Components.IComponent component) return component;");
        writer.WriteLine();
        writer.WriteLine("if (!_componentFactoryCache.TryGetValue(componentType, out var factory))");
        writer.WriteLine("{");
        writer.Indentation++;
        writer.WriteLine("factory = CreateComponentFactory(componentType);");
        writer.WriteLine("_componentFactoryCache.TryAdd(componentType, factory);");
        writer.Indentation--;
        writer.WriteLine("}");
        writer.WriteLine();
        writer.WriteLine("return (global::Microsoft.AspNetCore.Components.IComponent)factory(this, []);");
        writer.Indentation--;
        writer.WriteLine("}");
        writer.WriteLine();

        writer.WriteLine("#endregion");
        writer.WriteLine();
    }

    private static void WriteIComponentPropertyActivatorImplementation(SourceWriter writer, ContainerModel container, bool effectiveUseSwitchStatement)
    {
        writer.WriteLine("#region IComponentPropertyActivator");
        writer.WriteLine();

        // Static cache field
        writer.WriteLine("private static readonly global::System.Collections.Concurrent.ConcurrentDictionary<global::System.Type, global::System.Action<global::System.IServiceProvider, global::Microsoft.AspNetCore.Components.IComponent>> _propertyActivatorCache = new();");
        writer.WriteLine();

        // GetActivator method - explicit interface implementation
        writer.WriteLine("global::System.Action<global::System.IServiceProvider, global::Microsoft.AspNetCore.Components.IComponent> global::Microsoft.AspNetCore.Components.IComponentPropertyActivator.GetActivator(");
        writer.WriteLine("    [global::System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(global::System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.All)] global::System.Type componentType)");
        writer.WriteLine("{");
        writer.Indentation++;

        writer.WriteLine("if (!_propertyActivatorCache.TryGetValue(componentType, out var activator))");
        writer.WriteLine("{");
        writer.Indentation++;

        // No-op optimization: only when also implementing IComponentActivator
        if(container.ImplementComponentActivator)
        {
            if(effectiveUseSwitchStatement)
            {
                writer.WriteLine("activator = global::System.Array.Exists(_localResolvers, e => e.Key.Equals(new global::SourceGen.Ioc.ServiceIdentifier(componentType, global::Microsoft.Extensions.DependencyInjection.KeyedService.AnyKey)))");
            }
            else
            {
                writer.WriteLine("activator = _serviceResolvers.ContainsKey(new global::SourceGen.Ioc.ServiceIdentifier(componentType, global::Microsoft.Extensions.DependencyInjection.KeyedService.AnyKey))");
            }
            writer.WriteLine("    ? static (_, _) => { }");
            writer.WriteLine("    : CreateComponentPropertyInjector(componentType);");
        }
        else
        {
            writer.WriteLine("activator = CreateComponentPropertyInjector(componentType);");
        }

        writer.WriteLine("_propertyActivatorCache.TryAdd(componentType, activator);");
        writer.Indentation--;
        writer.WriteLine("}");

        writer.WriteLine("return activator;");
        writer.Indentation--;
        writer.WriteLine("}");
        writer.WriteLine();

        // CreateComponentPropertyInjector - reflection fallback method
        WriteCreateComponentPropertyInjectorMethod(writer);

        writer.WriteLine("#endregion");
        writer.WriteLine();
    }

    private static void WriteCreateComponentPropertyInjectorMethod(SourceWriter writer)
    {
        writer.WriteLine("private static global::System.Action<global::System.IServiceProvider, global::Microsoft.AspNetCore.Components.IComponent> CreateComponentPropertyInjector(");
        writer.WriteLine("    [global::System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(global::System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.All)] global::System.Type componentType)");
        writer.WriteLine("{");
        writer.Indentation++;

        writer.WriteLine("const global::System.Reflection.BindingFlags flags = global::System.Reflection.BindingFlags.Instance | global::System.Reflection.BindingFlags.Public | global::System.Reflection.BindingFlags.NonPublic;");
        writer.WriteLine("global::System.Collections.Generic.List<(global::System.Reflection.PropertyInfo Property, object? Key)>? injectables = null;");
        writer.WriteLine();

        // Walk inheritance chain
        writer.WriteLine("for (var type = componentType; type is not null; type = type.BaseType)");
        writer.WriteLine("{");
        writer.Indentation++;

        writer.WriteLine("foreach (var property in type.GetProperties(flags))");
        writer.WriteLine("{");
        writer.Indentation++;

        writer.WriteLine("if (property.DeclaringType != type) continue;");
        writer.WriteLine("var injectAttr = property.GetCustomAttributes(true)");
        writer.WriteLine("    .FirstOrDefault(a => a.GetType().Name is \"InjectAttribute\" or \"IocInjectAttribute\");");
        writer.WriteLine("if (injectAttr is null) continue;");
        writer.WriteLine();
        writer.WriteLine("var keyProp = injectAttr.GetType().GetProperty(\"Key\");");
        writer.WriteLine("var key = keyProp?.GetValue(injectAttr);");
        writer.WriteLine("injectables ??= new();");
        writer.WriteLine("injectables.Add((property, key));");

        writer.Indentation--;
        writer.WriteLine("}");

        writer.Indentation--;
        writer.WriteLine("}");
        writer.WriteLine();

        // Return no-op if no injectables found
        writer.WriteLine("if (injectables is null) return static (_, _) => { };");
        writer.WriteLine();

        // Return injection delegate
        writer.WriteLine("return (serviceProvider, component) =>");
        writer.WriteLine("{");
        writer.Indentation++;
        writer.WriteLine("foreach (var (property, serviceKey) in injectables)");
        writer.WriteLine("{");
        writer.Indentation++;

        writer.WriteLine("object? value;");
        writer.WriteLine("if (serviceKey is not null)");
        writer.WriteLine("{");
        writer.Indentation++;
        writer.WriteLine("if (serviceProvider is not global::Microsoft.Extensions.DependencyInjection.IKeyedServiceProvider keyedProvider)");
        writer.WriteLine("{");
        writer.Indentation++;
        writer.WriteLine("throw new global::System.InvalidOperationException($\"Cannot provide a value for property '{property.Name}' on type '{componentType.FullName}'. The service provider does not implement 'IKeyedServiceProvider'.\");");
        writer.Indentation--;
        writer.WriteLine("}");
        writer.WriteLine("value = keyedProvider.GetRequiredKeyedService(property.PropertyType, serviceKey);");
        writer.Indentation--;
        writer.WriteLine("}");
        writer.WriteLine("else");
        writer.WriteLine("{");
        writer.Indentation++;
        writer.WriteLine("value = serviceProvider.GetService(property.PropertyType) ?? throw new global::System.InvalidOperationException($\"Cannot provide a value for property '{property.Name}' on type '{componentType.FullName}'. There is no registered service of type '{property.PropertyType}'.\");");
        writer.Indentation--;
        writer.WriteLine("}");
        writer.WriteLine("property.SetValue(component, value);");

        writer.Indentation--;
        writer.WriteLine("}");
        writer.Indentation--;
        writer.WriteLine("};");

        writer.Indentation--;
        writer.WriteLine("}");
        writer.WriteLine();
    }

    /// <summary>
    /// Writes the __HotReloadHandler nested class for hot reload cache invalidation.
    /// </summary>
    private static void WriteHotReloadHandler(SourceWriter writer, ContainerModel container)
    {
        writer.WriteLine("[global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]");
        writer.WriteLine(GeneratedCodeAttribute);
        writer.WriteLine("internal static class __HotReloadHandler");
        writer.WriteLine("{");
        writer.Indentation++;
        writer.WriteLine("public static void ClearCache(global::System.Type[]? _)");
        writer.WriteLine("{");
        writer.Indentation++;
        if(container.ImplementComponentActivator)
        {
            writer.WriteLine("_componentFactoryCache.Clear();");
        }
        if(container.ImplementComponentPropertyActivator)
        {
            writer.WriteLine("_propertyActivatorCache.Clear();");
        }
        writer.Indentation--;
        writer.WriteLine("}");
        writer.Indentation--;
        writer.WriteLine("}");
    }
}
