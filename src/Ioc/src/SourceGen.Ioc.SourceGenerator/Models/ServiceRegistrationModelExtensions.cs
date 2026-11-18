namespace SourceGen.Ioc;

/// <summary>
/// Registration semantics shared by the Register and Container outputs, so that both interpret a registration the same way.
/// </summary>
internal static class ServiceRegistrationModelExtensions
{
    extension(ServiceRegistrationModel registration)
    {
        /// <summary>
        /// Gets whether the registration registers a service type other than its implementation type, which resolves the
        /// implementation type's instance.
        /// </summary>
        public bool IsServiceTypeAlias => registration.ServiceType.Name != registration.ImplementationType.Name;

        /// <summary>
        /// Gets whether the registration is an async-init service that is resolved as <c>Task&lt;T&gt;</c>: its implementation
        /// type or a decorator applied to its service type has an async injection method.
        /// </summary>
        public bool IsAsyncInit => registration.HasAsyncInjection || registration.HasAsyncDecorators;

        /// <summary>
        /// Gets whether the implementation type has an async injection method, so the implementation type itself is
        /// resolved as <c>Task&lt;T&gt;</c>. An <c>Instance</c> is registered as is, without calling its inject methods, so it
        /// is never async-init.
        /// </summary>
        public bool HasAsyncInjection => registration.Instance is null && HasAsyncMethod(registration.InjectionMembers);

        /// <summary>
        /// Gets whether a decorator applied to the service type has an async injection method, so the decorated service type
        /// is resolved as <c>Task&lt;T&gt;</c> even when the implementation type is not.
        /// </summary>
        public bool HasAsyncDecorators =>
            registration.AppliesDecorators && HasAsyncDecoratorMethod(registration.Decorators);

        /// <summary>
        /// Gets whether the instance that a <c>Func</c> with input parameters constructs for the service type needs async
        /// initialization, so such a Func is not generated: the registration is async-init, or it is an <c>Instance</c>
        /// registration (never async-init, since its instance is not decorated) and a decorator that the Func applies to the
        /// new instance has an async injection method.
        /// </summary>
        public bool IsAsyncInitWhenConstructedByFunc =>
            registration.IsAsyncInit
            || (registration.Instance is not null && HasAsyncDecoratorMethod(registration.Decorators));

        /// <summary>
        /// Gets whether a service type alias calls its own factory instead of resolving the implementation type's instance.
        /// A generic factory is closed per service type, so its result is the service type's instance: it is typed as the
        /// service type, and the implementation type keeps resolving its own registration.
        /// </summary>
        public bool HasOwnFactoryInstance =>
            registration.Factory is { TypeParameterCount: > 0 }
            && !registration.IsOpenGeneric
            && registration.IsServiceTypeAlias;

        /// <summary>
        /// Gets whether the registration's decorators wrap its instance. Decorators apply to closed service type registrations;
        /// the implementation type resolves the undecorated instance, and <c>Instance</c> registrations are not decorated.
        /// </summary>
        public bool AppliesDecorators =>
            registration.Decorators.Length > 0
            && !registration.IsOpenGeneric
            && registration.Instance is null
            && registration.IsServiceTypeAlias;

        /// <summary>
        /// Gets whether the service type resolves an instance of its own instead of the implementation type's instance:
        /// a decorated instance, or the result of a generic factory. The instance is typed as the service type.
        /// </summary>
        public bool HasOwnServiceInstance => registration.AppliesDecorators || registration.HasOwnFactoryInstance;
    }

    private static bool HasAsyncDecoratorMethod(ImmutableEquatableArray<TypeData> decorators)
    {
        foreach(var decorator in decorators)
        {
            if(decorator.InjectionMembers is { } members && HasAsyncMethod(members))
                return true;
        }

        return false;
    }

    private static bool HasAsyncMethod(ImmutableEquatableArray<InjectionMemberData> members)
    {
        foreach(var member in members)
        {
            if(member.MemberType == InjectionMemberType.AsyncMethod)
                return true;
        }

        return false;
    }
}
