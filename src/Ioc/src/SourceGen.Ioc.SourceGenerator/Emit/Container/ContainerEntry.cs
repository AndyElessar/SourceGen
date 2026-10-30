using static SourceGen.Ioc.Constants;

namespace SourceGen.Ioc;

partial class IocSourceGenerator
{
    /// <summary>
    /// Gets the type returned by a registration's resolver method: the service type when decorators
    /// wrap the instance, otherwise the implementation type.
    /// </summary>
    private static string GetResolvedTypeName(ServiceRegistrationModel registration) =>
        registration.Decorators.Length > 0 ? registration.ServiceType.Name : registration.ImplementationType.Name;

    private closed record class ContainerEntry
    {
        public virtual void WriteField(SourceWriter writer)
        {
        }

        public abstract void WriteResolver(SourceWriter writer);

        public virtual void WriteEagerInit(SourceWriter writer)
        {
        }

        public virtual void WriteDisposal(SourceWriter writer, bool isAsync)
        {
        }

        public virtual void WriteInit(SourceWriter writer)
        {
        }

        public virtual void WriteCollectionResolver(SourceWriter writer)
        {
        }

        public virtual void WriteLocalResolverEntries(SourceWriter writer)
        {
        }

        /// <summary>
        /// Writes the scope constructor statement copying this entry's cached singleton instance from the parent.
        /// </summary>
        public virtual void WriteCopyFromParent(SourceWriter writer)
        {
        }

        /// <summary>
        /// Gets the type a resolver entry for <paramref name="serviceType"/> is registered under.
        /// </summary>
        public virtual string GetExposedServiceType(string serviceType) => serviceType;

        /// <summary>
        /// Gets whether the entry is resolved in the container (or scope) constructor.
        /// </summary>
        public virtual bool IsEagerlyInitialized => false;

        /// <summary>
        /// Gets whether a scope delegates creation of this entry to the root container (lazily created singletons).
        /// </summary>
        public virtual bool DelegatesToRoot => false;
    }

    private closed record class ServiceContainerEntry(
        ServiceRegistrationModel Registration,
        string ResolverMethodName,
        ImmutableEquatableArray<ResolvedConstructorParameter> ConstructorParameters,
        ImmutableEquatableArray<ResolvedInjectionMember> InjectionMembers,
        ImmutableEquatableArray<ResolvedDecorator> Decorators) : ContainerEntry
    {
        /// <summary>
        /// Gets the type returned by the resolver method. Captured at construction so that alias copies
        /// (<c>with { Registration = ... }</c>) keep the resolver's actual return type.
        /// </summary>
        public string ResolvedTypeName { get; } = GetResolvedTypeName(Registration);

        /// <summary>
        /// Gets the method resolving the undecorated instance that this decorated service entry wraps
        /// (returning <c>Task&lt;T&gt;</c> for async-init services), or <see langword="null"/> when the entry
        /// creates the instance itself.
        /// </summary>
        public string? InnerResolverMethodName { get; init; }

        /// <summary>
        /// Formats the expression resolving <paramref name="serviceType"/> through this entry,
        /// with <paramref name="receiver"/> (e.g. <c>"c."</c> or <c>""</c>) prefixing member access.
        /// </summary>
        public virtual string FormatResolveExpression(string serviceType, string receiver) => $"{receiver}{ResolverMethodName}()";

        /// <summary>
        /// Formats the static resolver lambda used in the resolver dictionary.
        /// </summary>
        public virtual string FormatResolverLambda(string serviceType) => $"static c => {FormatResolveExpression(serviceType, "c.")}";

        /// <summary>
        /// Gets whether <see cref="FormatResolverLambda"/> is <c>static c => c.ResolverMethodName()</c>, which writers emit
        /// without formatting it. Entries that override <see cref="FormatResolveExpression"/> or <see cref="FormatResolverLambda"/> return <see langword="false"/>.
        /// </summary>
        public virtual bool HasResolverMethodLambda => true;

        protected void WriteInstanceCreationWithInjection(SourceWriter writer)
        {
            var typeDeclaration = Decorators.Length > 0 ? Registration.ServiceType.Name : "var";

            if(InnerResolverMethodName is not null)
            {
                writer.WriteLine($"{typeDeclaration} instance = {InnerResolverMethodName}();");
                return;
            }

            if(Registration.Factory is not null)
            {
                var factoryCall = BuildFactoryCallExpression();
                writer.WriteLine($"{typeDeclaration} instance = ({Registration.ImplementationType.Name}){factoryCall};");
                return;
            }

            WriteConstructorWithInjection(writer);
        }

        protected void WriteConstructorWithInjection(SourceWriter writer)
        {
            var typeDeclaration = Decorators.Length > 0 ? Registration.ServiceType.Name : "var";

            var properties = new List<ResolvedPropertyOrFieldInjection>();
            var methods = new List<ResolvedMethodInjection>();

            foreach(var member in InjectionMembers)
            {
                switch(member)
                {
                    case ResolvedPropertyOrFieldInjection propertyOrField:
                        properties.Add(propertyOrField);
                        break;

                    case ResolvedMethodInjection { Method.IsAsync: false } method:
                        methods.Add(method);
                        break;
                }
            }

            WriteConstructorWithPropertyInitializers(writer, "instance", typeDeclaration, ConstructorParameters, properties);

            foreach(var method in methods)
            {
                var methodArgs = BuildMethodArguments(method, Registration.Key);
                writer.WriteLine($"instance.{method.Method.Name}({methodArgs});");
            }
        }

        protected void WriteDecoratorApplication(SourceWriter writer) =>
            WriteDecoratorApplication(writer, Decorators, Registration.Key, "instance", "decorator");

        /// <summary>
        /// Writes the statements wrapping variable <paramref name="instanceVar"/> with <paramref name="decorators"/>,
        /// the first decorator outermost. Decorators with injection members are built in variables named
        /// <paramref name="decoratorVarPrefix"/> followed by an index.
        /// </summary>
        public static void WriteDecoratorApplication(
            SourceWriter writer,
            ImmutableEquatableArray<ResolvedDecorator> decorators,
            string? registrationKey,
            string instanceVar,
            string decoratorVarPrefix)
        {
            for(var i = decorators.Length - 1; i >= 0; i--)
            {
                var decorator = decorators[i];
                var decoratorName = decorator.Decorator.ImplementationType.Name;
                var hasInjectionMembers = decorator.InjectionMembers.Length > 0;

                var args = new List<string>(1 + decorator.Parameters.Length)
                {
                    instanceVar
                };

                foreach(var parameter in decorator.Parameters)
                {
                    args.Add(parameter.Dependency.FormatExpression(parameter.IsOptional));
                }

                var argsString = string.Join(", ", args);

                if(hasInjectionMembers)
                {
                    var decoratorVarName = $"{decoratorVarPrefix}{decorators.Length - 1 - i}";
                    WriteDecoratorCreationWithInjection(writer, decoratorVarName, decoratorName, decorator.InjectionMembers, argsString, registrationKey);
                    writer.WriteLine($"{instanceVar} = {decoratorVarName};");
                }
                else
                {
                    writer.WriteLine($"{instanceVar} = new {decoratorName}({argsString});");
                }
            }
        }

        protected void WriteAsyncInstanceCreationBody(SourceWriter writer)
        {
            if(InnerResolverMethodName is not null)
            {
                writer.WriteLine($"{Registration.ServiceType.Name} instance = await {InnerResolverMethodName}().ConfigureAwait(false);");
                writer.WriteLine();
                WriteDecoratorApplication(writer);
                writer.WriteLine("return instance;");
                return;
            }

            var properties = new List<ResolvedPropertyOrFieldInjection>();
            var syncMethods = new List<ResolvedMethodInjection>();
            var asyncMethods = new List<ResolvedMethodInjection>();

            foreach(var member in InjectionMembers)
            {
                switch(member)
                {
                    case ResolvedPropertyOrFieldInjection propertyOrField:
                        properties.Add(propertyOrField);
                        break;

                    case ResolvedMethodInjection { Method.IsAsync: false } method:
                        syncMethods.Add(method);
                        break;

                    case ResolvedMethodInjection method:
                        asyncMethods.Add(method);
                        break;
                }
            }

            var hasMethods = syncMethods.Count > 0 || asyncMethods.Count > 0;
            var hasDecorators = Decorators.Length > 0;
            var needsTwoVarPattern = hasDecorators && hasMethods;

            var injectionVar = needsTwoVarPattern ? "baseInstance" : "instance";
            var typeDeclaration = hasDecorators && !needsTwoVarPattern ? Registration.ServiceType.Name : "var";

            if(Registration.Factory is not null)
            {
                var factoryCall = BuildFactoryCallExpression();
                writer.WriteLine($"{typeDeclaration} {injectionVar} = ({Registration.ImplementationType.Name}){factoryCall};");
            }
            else
            {
                WriteConstructorWithPropertyInitializers(writer, injectionVar, typeDeclaration, ConstructorParameters, properties);
            }

            foreach(var method in syncMethods)
            {
                var methodArgs = BuildMethodArguments(method, Registration.Key);
                writer.WriteLine($"{injectionVar}.{method.Method.Name}({methodArgs});");
            }

            foreach(var method in asyncMethods)
            {
                var methodArgs = BuildMethodArguments(method, Registration.Key);
                writer.WriteLine($"await {injectionVar}.{method.Method.Name}({methodArgs}).ConfigureAwait(false);");
            }

            if(hasDecorators)
            {
                writer.WriteLine();
                if(needsTwoVarPattern)
                {
                    writer.WriteLine($"{Registration.ServiceType.Name} instance = {injectionVar};");
                }

                WriteDecoratorApplication(writer);
            }

            writer.WriteLine("return instance;");
        }

        private string BuildFactoryCallExpression()
        {
            var factory = Registration.Factory!;
            var args = factory.OrderArguments("this", Registration.Key,
                [.. ConstructorParameters.Select(static parameter => parameter.Dependency.FormatExpression(parameter.IsOptional))]);

            var genericTypeArgs = BuildGenericFactoryTypeArgs(factory, Registration.ServiceType);
            var factoryCallPath = genericTypeArgs is not null ? $"{factory.Path}<{genericTypeArgs}>" : factory.Path;
            return $"{factoryCallPath}({string.Join(", ", args)})";
        }

        private void WriteConstructorWithPropertyInitializers(
            SourceWriter writer,
            string variableName,
            string typeDeclaration,
            ImmutableEquatableArray<ResolvedConstructorParameter> constructorParameters,
            List<ResolvedPropertyOrFieldInjection> propertyMembers)
        {
            var args = constructorParameters.Length == 0
                ? ""
                : string.Join(", ", constructorParameters.Select(static p => p.Dependency.FormatExpression(p.IsOptional)));

            if(propertyMembers.Count == 0)
            {
                writer.WriteLine($"{typeDeclaration} {variableName} = new {Registration.ImplementationType.Name}({args});");
                return;
            }

            writer.WriteLine($"{typeDeclaration} {variableName} = new {Registration.ImplementationType.Name}({args})");
            writer.WriteLine("{");
            writer.Indentation++;

            foreach(var (propertyOrField, dependency) in propertyMembers)
            {
                writer.WriteLine($"{propertyOrField.Name} = {dependency.FormatExpression(propertyOrField.IsNullable)},");
            }

            writer.Indentation--;
            writer.WriteLine("};");
        }

        private static void WriteDecoratorCreationWithInjection(
            SourceWriter writer,
            string variableName,
            string decoratorTypeName,
            ImmutableEquatableArray<ResolvedInjectionMember> injectionMembers,
            string argsString,
            string? registrationKey)
        {
            if(injectionMembers.Length == 0)
            {
                writer.WriteLine($"var {variableName} = new {decoratorTypeName}({argsString});");
                return;
            }

            // Property/field assignments go in the object initializer, method invocations follow it
            var hasPropertyAssignments = false;
            foreach(var member in injectionMembers)
            {
                if(member is not ResolvedPropertyOrFieldInjection(var propertyOrField, var dependency))
                    continue;

                if(!hasPropertyAssignments)
                {
                    hasPropertyAssignments = true;
                    writer.WriteLine($"var {variableName} = new {decoratorTypeName}({argsString})");
                    writer.WriteLine("{");
                    writer.Indentation++;
                }

                writer.WriteLine($"{propertyOrField.Name} = {dependency.FormatExpression(propertyOrField.IsNullable)},");
            }

            if(hasPropertyAssignments)
            {
                writer.Indentation--;
                writer.WriteLine("};");
            }
            else
            {
                writer.WriteLine($"var {variableName} = new {decoratorTypeName}({argsString});");
            }

            // Decorators do not support async injection methods
            foreach(var member in injectionMembers)
            {
                if(member is ResolvedMethodInjection { Method.IsAsync: false } method)
                {
                    writer.WriteLine($"{variableName}.{method.Method.Name}({BuildMethodArguments(method, registrationKey)});");
                }
            }
        }

        private static string BuildMethodArguments(ResolvedMethodInjection member, string? registrationKey)
        {
            var parameters = member.Method.Parameters;
            if(parameters.Length == 0)
                return "";

            var args = new string[parameters.Length];
            var parameterDependencies = member.ParameterDependencies;

            for(var i = 0; i < parameters.Length; i++)
            {
                var parameter = parameters[i];

                if(parameter.HasServiceKeyAttribute)
                {
                    args[i] = registrationKey ?? "null";
                    continue;
                }

                if(parameter.Type.Name is IServiceProviderTypeName or IServiceProviderGlobalTypeName)
                {
                    args[i] = "this";
                    continue;
                }

                args[i] = parameterDependencies[i].FormatExpression(parameter.IsOptional);
            }

            return string.Join(", ", args);
        }
    }
}
