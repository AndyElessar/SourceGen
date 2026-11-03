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
        /// Gets whether the registration has an async injection method, making it an async-init service that is resolved
        /// as <c>Task&lt;T&gt;</c>.
        /// </summary>
        public bool IsAsyncInit
        {
            get
            {
                foreach(var member in registration.InjectionMembers)
                {
                    if(member.MemberType == InjectionMemberType.AsyncMethod)
                        return true;
                }

                return false;
            }
        }

        /// <summary>
        /// Gets whether the registration's decorators wrap its instance. Decorators apply to closed service type registrations;
        /// the implementation type resolves the undecorated instance, and <c>Instance</c> registrations are not decorated.
        /// </summary>
        public bool AppliesDecorators =>
            registration.Decorators.Length > 0
            && !registration.IsOpenGeneric
            && registration.Instance is null
            && registration.IsServiceTypeAlias;
    }
}
