#nullable enable

using static SourceGen.Ioc.SourceGenerator.Models.Constants;

using System;
using System.Collections.Generic;
using System.Linq;

namespace SourceGen.Ioc;

partial class IocSourceGenerator
{
    internal abstract record class ContainerEntry
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
    }

    private abstract record class ServiceContainerEntry(
        ServiceRegistrationModel Registration,
        string ResolverMethodName,
        ImmutableEquatableArray<ResolvedConstructorParameter> ConstructorParameters,
        ImmutableEquatableArray<ResolvedInjectionMember> InjectionMembers,
        ImmutableEquatableArray<ResolvedDecorator> Decorators) : ContainerEntry
    {
        protected void WriteInstanceCreationWithInjection(SourceWriter writer)
        {
            var typeDeclaration = Decorators.Length > 0 ? Registration.ServiceType.Name : "var";

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

            var properties = new List<ResolvedInjectionMember>();
            var methods = new List<ResolvedInjectionMember>();

            foreach(var member in InjectionMembers)
            {
                switch(member.Member.MemberType)
                {
                    case InjectionMemberType.Property:
                    case InjectionMemberType.Field:
                        properties.Add(member);
                        break;

                    case InjectionMemberType.Method:
                        methods.Add(member);
                        break;
                }
            }

            WriteConstructorWithPropertyInitializers(writer, "instance", typeDeclaration, ConstructorParameters, properties);

            foreach(var method in methods)
            {
                var methodArgs = BuildMethodArguments(method, Registration.Key);
                writer.WriteLine($"instance.{method.Member.Name}({methodArgs});");
            }
        }

        protected void WriteDecoratorApplication(SourceWriter writer)
        {
            for(var i = Decorators.Length - 1; i >= 0; i--)
            {
                var decorator = Decorators[i];
                var decoratorName = decorator.Decorator.ImplementationType.Name;
                var hasInjectionMembers = decorator.InjectionMembers.Length > 0;

                var args = new List<string>(1 + decorator.Parameters.Length)
                {
                    "instance"
                };

                foreach(var parameter in decorator.Parameters)
                {
                    args.Add(parameter.Dependency.FormatExpression(parameter.IsOptional));
                }

                var argsString = string.Join(", ", args);

                if(hasInjectionMembers)
                {
                    var decoratorVarName = $"decorator{Decorators.Length - 1 - i}";
                    WriteDecoratorCreationWithInjection(writer, decoratorVarName, decoratorName, decorator.InjectionMembers, argsString, Registration.Key);
                    writer.WriteLine($"instance = {decoratorVarName};");
                }
                else
                {
                    writer.WriteLine($"instance = new {decoratorName}({argsString});");
                }
            }
        }

        protected void WriteAsyncInstanceCreationBody(SourceWriter writer)
        {
            var properties = new List<ResolvedInjectionMember>();
            var syncMethods = new List<ResolvedInjectionMember>();
            var asyncMethods = new List<ResolvedInjectionMember>();

            foreach(var member in InjectionMembers)
            {
                switch(member.Member.MemberType)
                {
                    case InjectionMemberType.Property:
                    case InjectionMemberType.Field:
                        properties.Add(member);
                        break;

                    case InjectionMemberType.Method:
                        syncMethods.Add(member);
                        break;

                    case InjectionMemberType.AsyncMethod:
                        asyncMethods.Add(member);
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
                writer.WriteLine($"{injectionVar}.{method.Member.Name}({methodArgs});");
            }

            foreach(var method in asyncMethods)
            {
                var methodArgs = BuildMethodArguments(method, Registration.Key);
                writer.WriteLine($"await {injectionVar}.{method.Member.Name}({methodArgs});");
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

            var args = new List<string>();
            if(factory.HasServiceProvider)
            {
                args.Add("this");
            }

            if(factory.HasKey && Registration.Key is not null)
            {
                args.Add(Registration.Key);
            }

            foreach(var parameter in ConstructorParameters)
            {
                args.Add(parameter.Dependency.FormatExpression(parameter.IsOptional));
            }

            var genericTypeArgs = BuildGenericFactoryTypeArgs(factory, Registration.ServiceType);
            var factoryCallPath = genericTypeArgs is not null ? $"{factory.Path}<{genericTypeArgs}>" : factory.Path;
            return $"{factoryCallPath}({string.Join(", ", args)})";
        }

        private void WriteConstructorWithPropertyInitializers(
            SourceWriter writer,
            string variableName,
            string typeDeclaration,
            ImmutableEquatableArray<ResolvedConstructorParameter> constructorParameters,
            List<ResolvedInjectionMember> propertyMembers)
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

            foreach(var member in propertyMembers)
            {
                if(member.Dependency is null)
                    throw new InvalidOperationException($"Missing resolved dependency for injection member '{member.Member.Name}'.");

                writer.WriteLine($"{member.Member.Name} = {member.Dependency.FormatExpression(member.Member.IsNullable)},");
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

            var propertyAssignments = new List<string>();
            var methodInvocations = new List<string>();

            foreach(var member in injectionMembers)
            {
                switch(member.Member.MemberType)
                {
                    case InjectionMemberType.Property:
                    case InjectionMemberType.Field:
                        if(member.Dependency is null)
                            throw new InvalidOperationException($"Missing resolved dependency for decorator member '{member.Member.Name}'.");

                        propertyAssignments.Add($"{member.Member.Name} = {member.Dependency.FormatExpression(member.Member.IsNullable)},");
                        break;

                    case InjectionMemberType.Method:
                        var args = BuildMethodArguments(member, registrationKey);
                        methodInvocations.Add($"{variableName}.{member.Member.Name}({args});");
                        break;
                }
            }

            if(propertyAssignments.Count == 0)
            {
                writer.WriteLine($"var {variableName} = new {decoratorTypeName}({argsString});");
            }
            else
            {
                writer.WriteLine($"var {variableName} = new {decoratorTypeName}({argsString})");
                writer.WriteLine("{");
                writer.Indentation++;
                foreach(var assignment in propertyAssignments)
                {
                    writer.WriteLine(assignment);
                }

                writer.Indentation--;
                writer.WriteLine("};");
            }

            foreach(var invocation in methodInvocations)
            {
                writer.WriteLine(invocation);
            }
        }

        private static string BuildMethodArguments(ResolvedInjectionMember member, string? registrationKey)
        {
            var parameters = member.Member.Parameters;
            if(parameters is null or { Length: 0 })
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

                if(parameterDependencies.Length > i)
                {
                    args[i] = parameterDependencies[i].FormatExpression(parameter.IsOptional);
                    continue;
                }

                if(parameters.Length == 1 && member.Dependency is not null)
                {
                    args[i] = member.Dependency.FormatExpression(parameter.IsOptional);
                    continue;
                }

                throw new InvalidOperationException($"Missing resolved dependency for injection method parameter '{parameter.Name}' in member '{member.Member.Name}'.");
            }

            return string.Join(", ", args);
        }
    }

    private sealed record class InstanceContainerEntry(
        ServiceRegistrationModel Registration,
        string ResolverMethodName,
        string FieldName,
        ImmutableEquatableArray<ResolvedConstructorParameter> ConstructorParameters,
        ImmutableEquatableArray<ResolvedInjectionMember> InjectionMembers,
        ImmutableEquatableArray<ResolvedDecorator> Decorators) : ServiceContainerEntry(Registration, ResolverMethodName, ConstructorParameters, InjectionMembers, Decorators)
    {
        public override void WriteField(SourceWriter writer)
        {
        }

        public override void WriteResolver(SourceWriter writer)
        {
        }
    }

    private sealed record class EagerContainerEntry(
        ServiceRegistrationModel Registration,
        string ResolverMethodName,
        string FieldName,
        ImmutableEquatableArray<ResolvedConstructorParameter> ConstructorParameters,
        ImmutableEquatableArray<ResolvedInjectionMember> InjectionMembers,
        ImmutableEquatableArray<ResolvedDecorator> Decorators) : ServiceContainerEntry(Registration, ResolverMethodName, ConstructorParameters, InjectionMembers, Decorators)
    {
        public override void WriteField(SourceWriter writer)
        {
            var returnType = Decorators.Length > 0 ? Registration.ServiceType.Name : Registration.ImplementationType.Name;
            writer.WriteLine($"private {returnType} {FieldName} = null!;");
        }

        public override void WriteResolver(SourceWriter writer)
        {
            var returnType = Decorators.Length > 0 ? Registration.ServiceType.Name : Registration.ImplementationType.Name;

            writer.WriteLine($"private {returnType} {ResolverMethodName}()");
            writer.WriteLine("{");
            writer.Indentation++;

            writer.WriteEarlyReturnIfNotNull(FieldName);
            writer.WriteLine();

            WriteInstanceCreationWithInjection(writer);

            if(Decorators.Length > 0)
            {
                writer.WriteLine();
                WriteDecoratorApplication(writer);
            }

            writer.WriteLine();
            writer.WriteFieldAssignAndReturn(FieldName, "instance");

            writer.Indentation--;
            writer.WriteLine("}");
        }

        public override void WriteEagerInit(SourceWriter writer)
        {
            writer.WriteLine($"{ResolverMethodName}();");
        }

        public override void WriteDisposal(SourceWriter writer, bool isAsync)
        {
            var disposeMethod = isAsync ? "await DisposeServiceAsync" : "DisposeService";
            writer.WriteLine($"{disposeMethod}({FieldName});");
        }
    }

    private sealed record class LazyThreadSafeContainerEntry(
        ServiceRegistrationModel Registration,
        string ResolverMethodName,
        string FieldName,
        ThreadSafeStrategy ThreadSafeStrategy,
        ImmutableEquatableArray<ResolvedConstructorParameter> ConstructorParameters,
        ImmutableEquatableArray<ResolvedInjectionMember> InjectionMembers,
        ImmutableEquatableArray<ResolvedDecorator> Decorators) : ServiceContainerEntry(Registration, ResolverMethodName, ConstructorParameters, InjectionMembers, Decorators)
    {
        public override void WriteField(SourceWriter writer)
        {
            var returnType = Decorators.Length > 0 ? Registration.ServiceType.Name : Registration.ImplementationType.Name;
            writer.WriteLine($"private {returnType}? {FieldName};");

            var syncFieldDeclaration = ThreadSafeStrategy switch
            {
                ThreadSafeStrategy.Lock => $"private readonly Lock {FieldName}Lock = new();",
                ThreadSafeStrategy.SemaphoreSlim => $"private readonly SemaphoreSlim {FieldName}Semaphore = new(1, 1);",
                ThreadSafeStrategy.SpinLock => $"private SpinLock {FieldName}SpinLock = new(false);",
                _ => null
            };

            if(syncFieldDeclaration is not null)
            {
                writer.WriteLine(syncFieldDeclaration);
            }
        }

        public override void WriteResolver(SourceWriter writer)
        {
            var returnType = Decorators.Length > 0 ? Registration.ServiceType.Name : Registration.ImplementationType.Name;

            writer.WriteLine($"private {returnType} {ResolverMethodName}()");
            writer.WriteLine("{");
            writer.Indentation++;

            writer.WriteEarlyReturnIfNotNull(FieldName);
            writer.WriteLine();

            Action<SourceWriter> writeResolverBody = ThreadSafeStrategy switch
            {
                ThreadSafeStrategy.None => WriteResolverBodyNone,
                ThreadSafeStrategy.Lock => WriteResolverBodyLock,
                ThreadSafeStrategy.SemaphoreSlim => WriteResolverBodySemaphoreSlim,
                ThreadSafeStrategy.SpinLock => WriteResolverBodySpinLock,
                ThreadSafeStrategy.CompareExchange => WriteResolverBodyCompareExchange,
                _ => WriteResolverBodyNone
            };

            writeResolverBody(writer);

            writer.Indentation--;
            writer.WriteLine("}");
        }

        public override void WriteDisposal(SourceWriter writer, bool isAsync)
        {
            var disposeMethod = isAsync ? "await DisposeServiceAsync" : "DisposeService";
            writer.WriteLine($"{disposeMethod}({FieldName});");

            if(ThreadSafeStrategy == ThreadSafeStrategy.SemaphoreSlim)
            {
                writer.WriteLine($"{FieldName}Semaphore.Dispose();");
            }
        }

        private void WriteResolverBodyNone(SourceWriter writer)
        {
            WriteInstanceCreationAndAssignment(writer);
        }

        private void WriteResolverBodyLock(SourceWriter writer)
        {
            writer.WriteLine($"lock({FieldName}Lock)");
            writer.WriteLine("{");
            writer.Indentation++;

            writer.WriteEarlyReturnIfNotNull(FieldName);
            writer.WriteLine();

            WriteInstanceCreationAndAssignment(writer);

            writer.Indentation--;
            writer.WriteLine("}");
        }

        private void WriteResolverBodySemaphoreSlim(SourceWriter writer)
        {
            writer.WriteLine($"{FieldName}Semaphore.Wait();");
            writer.WriteLine("try");
            writer.WriteLine("{");
            writer.Indentation++;

            writer.WriteEarlyReturnIfNotNull(FieldName);
            writer.WriteLine();

            WriteInstanceCreationAndAssignment(writer);

            writer.Indentation--;
            writer.WriteLine("}");
            writer.WriteLine("finally");
            writer.WriteLine("{");
            writer.Indentation++;
            writer.WriteLine($"{FieldName}Semaphore.Release();");
            writer.Indentation--;
            writer.WriteLine("}");
        }

        private void WriteResolverBodySpinLock(SourceWriter writer)
        {
            writer.WriteLine("bool lockTaken = false;");
            writer.WriteLine("try");
            writer.WriteLine("{");
            writer.Indentation++;

            writer.WriteLine($"{FieldName}SpinLock.Enter(ref lockTaken);");
            writer.WriteEarlyReturnIfNotNull(FieldName);
            writer.WriteLine();

            WriteInstanceCreationAndAssignment(writer);

            writer.Indentation--;
            writer.WriteLine("}");
            writer.WriteLine("finally");
            writer.WriteLine("{");
            writer.Indentation++;
            writer.WriteLine($"if(lockTaken) {FieldName}SpinLock.Exit();");
            writer.Indentation--;
            writer.WriteLine("}");
        }

        private void WriteResolverBodyCompareExchange(SourceWriter writer)
        {
            WriteInstanceCreationWithInjection(writer);

            if(Decorators.Length > 0)
            {
                writer.WriteLine();
                WriteDecoratorApplication(writer);
            }

            writer.WriteLine();
            writer.WriteLine($"var existing = Interlocked.CompareExchange(ref {FieldName}, instance, null);");
            writer.WriteLine("if(existing is not null)");
            writer.WriteLine("{");
            writer.Indentation++;
            writer.WriteLine("DisposeService(instance);");
            writer.WriteLine("return existing;");
            writer.Indentation--;
            writer.WriteLine("}");
            writer.WriteLine("return instance;");
        }

        private void WriteInstanceCreationAndAssignment(SourceWriter writer)
        {
            WriteInstanceCreationWithInjection(writer);

            if(Decorators.Length > 0)
            {
                writer.WriteLine();
                WriteDecoratorApplication(writer);
            }

            writer.WriteLine();
            writer.WriteFieldAssignAndReturn(FieldName, "instance");
        }
    }

    private sealed record class TransientContainerEntry(
        ServiceRegistrationModel Registration,
        string ResolverMethodName,
        ImmutableEquatableArray<ResolvedConstructorParameter> ConstructorParameters,
        ImmutableEquatableArray<ResolvedInjectionMember> InjectionMembers,
        ImmutableEquatableArray<ResolvedDecorator> Decorators) : ServiceContainerEntry(Registration, ResolverMethodName, ConstructorParameters, InjectionMembers, Decorators)
    {
        public override void WriteResolver(SourceWriter writer)
        {
            var returnType = Decorators.Length > 0 ? Registration.ServiceType.Name : Registration.ImplementationType.Name;

            writer.WriteLine($"private {returnType} {ResolverMethodName}()");
            writer.WriteLine("{");
            writer.Indentation++;

            if(InjectionMembers.Length == 0 && Decorators.Length == 0)
            {
                if(Registration.Factory is not null)
                {
                    var factory = Registration.Factory;
                    var args = new List<string>();

                    if(factory.HasServiceProvider)
                    {
                        args.Add("this");
                    }

                    if(factory.HasKey && Registration.Key is not null)
                    {
                        args.Add(Registration.Key);
                    }

                    foreach(var parameter in ConstructorParameters)
                    {
                        args.Add(parameter.Dependency.FormatExpression(parameter.IsOptional));
                    }

                    var genericTypeArgs = BuildGenericFactoryTypeArgs(factory, Registration.ServiceType);
                    var factoryCallPath = genericTypeArgs is not null ? $"{factory.Path}<{genericTypeArgs}>" : factory.Path;
                    writer.WriteLine($"return ({Registration.ImplementationType.Name}){factoryCallPath}({string.Join(", ", args)});");
                }
                else
                {
                    var ctorArgs = ConstructorParameters.Length == 0
                        ? ""
                        : string.Join(", ", ConstructorParameters.Select(static p => p.Dependency.FormatExpression(p.IsOptional)));
                    writer.WriteLine($"return new {Registration.ImplementationType.Name}({ctorArgs});");
                }

                writer.Indentation--;
                writer.WriteLine("}");
                return;
            }

            WriteInstanceCreationWithInjection(writer);

            if(Decorators.Length > 0)
            {
                writer.WriteLine();
                WriteDecoratorApplication(writer);
            }

            writer.WriteLine("return instance;");

            writer.Indentation--;
            writer.WriteLine("}");
        }
    }

    private sealed record class AsyncContainerEntry(
        ServiceRegistrationModel Registration,
        string ResolverMethodName,
        string FieldName,
        ThreadSafeStrategy ThreadSafeStrategy,
        ImmutableEquatableArray<ResolvedConstructorParameter> ConstructorParameters,
        ImmutableEquatableArray<ResolvedInjectionMember> InjectionMembers,
        ImmutableEquatableArray<ResolvedDecorator> Decorators) : ServiceContainerEntry(Registration, ResolverMethodName, ConstructorParameters, InjectionMembers, Decorators)
    {
        public override void WriteField(SourceWriter writer)
        {
            var returnType = Decorators.Length > 0 ? Registration.ServiceType.Name : Registration.ImplementationType.Name;
            var taskReturnType = $"global::System.Threading.Tasks.Task<{returnType}>";

            writer.WriteLine($"private {taskReturnType}? {FieldName};");

            if(ThreadSafeStrategy == ThreadSafeStrategy.SemaphoreSlim)
            {
                writer.WriteLine($"private readonly global::System.Threading.SemaphoreSlim {FieldName}Semaphore = new(1, 1);");
            }
        }

        public override void WriteResolver(SourceWriter writer)
        {
            var returnType = Decorators.Length > 0 ? Registration.ServiceType.Name : Registration.ImplementationType.Name;
            var asyncMethodName = GetAsyncResolverMethodName(ResolverMethodName);
            var createMethodName = GetAsyncCreateMethodName(ResolverMethodName);
            var taskReturnType = $"global::System.Threading.Tasks.Task<{returnType}>";

            writer.WriteLine($"private async {taskReturnType} {asyncMethodName}()");
            writer.WriteLine("{");
            writer.Indentation++;

            writer.WriteLine($"if({FieldName} is not null)");
            writer.Indentation++;
            writer.WriteLine($"return await {FieldName};");
            writer.Indentation--;
            writer.WriteLine();

            if(ThreadSafeStrategy == ThreadSafeStrategy.SemaphoreSlim)
            {
                WriteAsyncResolverBodySemaphoreSlim(writer, createMethodName);
            }
            else
            {
                WriteAsyncResolverBodyNone(writer, createMethodName);
            }

            writer.Indentation--;
            writer.WriteLine("}");
            writer.WriteLine();

            writer.WriteLine($"private async {taskReturnType} {createMethodName}()");
            writer.WriteLine("{");
            writer.Indentation++;

            WriteAsyncInstanceCreationBody(writer);

            writer.Indentation--;
            writer.WriteLine("}");
        }

        public override void WriteEagerInit(SourceWriter writer)
        {
            writer.WriteLine($"_ = {GetAsyncResolverMethodName(ResolverMethodName)}();");
        }

        public override void WriteDisposal(SourceWriter writer, bool isAsync)
        {
            var disposeMethod = isAsync ? "await DisposeServiceAsync" : "DisposeService";
            writer.WriteLine($"{disposeMethod}({FieldName});");

            if(ThreadSafeStrategy == ThreadSafeStrategy.SemaphoreSlim)
            {
                writer.WriteLine($"{FieldName}Semaphore.Dispose();");
            }
        }

        private void WriteAsyncResolverBodyNone(SourceWriter writer, string createMethodName)
        {
            writer.WriteLine($"{FieldName} = {createMethodName}();");
            writer.WriteLine($"return await {FieldName};");
        }

        private void WriteAsyncResolverBodySemaphoreSlim(SourceWriter writer, string createMethodName)
        {
            writer.WriteLine($"await {FieldName}Semaphore.WaitAsync();");
            writer.WriteLine("try");
            writer.WriteLine("{");
            writer.Indentation++;

            writer.WriteLine($"if({FieldName} is null)");
            writer.WriteLine("{");
            writer.Indentation++;
            writer.WriteLine($"{FieldName} = {createMethodName}();");
            writer.Indentation--;
            writer.WriteLine("}");
            writer.Indentation--;
            writer.WriteLine("}");
            writer.WriteLine("finally");
            writer.WriteLine("{");
            writer.Indentation++;
            writer.WriteLine($"{FieldName}Semaphore.Release();");
            writer.Indentation--;
            writer.WriteLine("}");

            writer.WriteLine($"return await {FieldName};");
        }
    }

    private sealed record class AsyncTransientContainerEntry(
        ServiceRegistrationModel Registration,
        string ResolverMethodName,
        ImmutableEquatableArray<ResolvedConstructorParameter> ConstructorParameters,
        ImmutableEquatableArray<ResolvedInjectionMember> InjectionMembers,
        ImmutableEquatableArray<ResolvedDecorator> Decorators) : ServiceContainerEntry(Registration, ResolverMethodName, ConstructorParameters, InjectionMembers, Decorators)
    {
        public override void WriteResolver(SourceWriter writer)
        {
            var returnType = Decorators.Length > 0 ? Registration.ServiceType.Name : Registration.ImplementationType.Name;
            var createMethodName = GetAsyncCreateMethodName(ResolverMethodName);
            var taskReturnType = $"global::System.Threading.Tasks.Task<{returnType}>";

            writer.WriteLine($"private async {taskReturnType} {createMethodName}()");
            writer.WriteLine("{");
            writer.Indentation++;

            WriteAsyncInstanceCreationBody(writer);

            writer.Indentation--;
            writer.WriteLine("}");
        }
    }

    private sealed record class LazyWrapperContainerEntry(
        string InnerServiceTypeName,
        string InnerImplTypeName,
        string FieldName,
        string InnerResolverMethodName,
        string? Key,
        bool EmitCollectionResolver,
        ImmutableEquatableArray<string> CollectionFieldNames) : ContainerEntry
    {
        public override void WriteField(SourceWriter writer)
        {
            writer.WriteLine($"private readonly global::System.Lazy<{InnerServiceTypeName}> {FieldName};");
        }

        public override void WriteResolver(SourceWriter writer)
        {
        }

        public override void WriteInit(SourceWriter writer)
        {
            writer.WriteLine($"{FieldName} = new global::System.Lazy<{InnerServiceTypeName}>(() => {InnerResolverMethodName}(), global::System.Threading.LazyThreadSafetyMode.ExecutionAndPublication);");
        }

        public override void WriteCollectionResolver(SourceWriter writer)
        {
            if(!EmitCollectionResolver)
            {
                return;
            }

            var wrapperTypeName = $"global::System.Lazy<{InnerServiceTypeName}>";
            var arrayMethodName = GetLazyArrayResolverMethodName(InnerServiceTypeName);

            writer.WriteLine($"private {wrapperTypeName}[] {arrayMethodName}() =>");
            writer.Indentation++;
            writer.WriteLine("[");
            writer.Indentation++;

            foreach(var fieldName in CollectionFieldNames)
            {
                writer.WriteLine($"{fieldName},");
            }

            writer.Indentation--;
            writer.WriteLine("];");
            writer.Indentation--;
        }
    }

    private sealed record class FuncWrapperContainerEntry(
        string InnerServiceTypeName,
        string InnerImplTypeName,
        string FieldName,
        string InnerResolverMethodName,
        string? Key,
        bool EmitCollectionResolver,
        ImmutableEquatableArray<string> CollectionFieldNames) : ContainerEntry
    {
        public override void WriteField(SourceWriter writer)
        {
            writer.WriteLine($"private readonly global::System.Func<{InnerServiceTypeName}> {FieldName};");
        }

        public override void WriteResolver(SourceWriter writer)
        {
        }

        public override void WriteInit(SourceWriter writer)
        {
            writer.WriteLine($"{FieldName} = new global::System.Func<{InnerServiceTypeName}>(() => {InnerResolverMethodName}());");
        }

        public override void WriteCollectionResolver(SourceWriter writer)
        {
            if(!EmitCollectionResolver)
            {
                return;
            }

            var wrapperTypeName = $"global::System.Func<{InnerServiceTypeName}>";
            var arrayMethodName = GetFuncArrayResolverMethodName(InnerServiceTypeName);

            writer.WriteLine($"private {wrapperTypeName}[] {arrayMethodName}() =>");
            writer.Indentation++;
            writer.WriteLine("[");
            writer.Indentation++;

            foreach(var fieldName in CollectionFieldNames)
            {
                writer.WriteLine($"{fieldName},");
            }

            writer.Indentation--;
            writer.WriteLine("];");
            writer.Indentation--;
        }
    }

    private sealed record class KvpWrapperContainerEntry(
        string KeyTypeName,
        string ValueTypeName,
        string KeyExpr,
        string ResolverMethodName,
        string KvpResolverMethodName) : ContainerEntry
    {
        public override void WriteResolver(SourceWriter writer)
        {
            var kvpTypeName = $"global::System.Collections.Generic.KeyValuePair<{KeyTypeName}, {ValueTypeName}>";
            writer.WriteLine($"private {kvpTypeName} {KvpResolverMethodName}() => new {kvpTypeName}({KeyExpr}, {ResolverMethodName}());");
        }
    }

    private sealed record class CollectionContainerEntry(
        string ElementServiceTypeName,
        string ArrayMethodName,
        ImmutableEquatableArray<ResolvedDependency> ElementResolvers) : ContainerEntry
    {
        public override void WriteResolver(SourceWriter writer)
        {
            writer.WriteLine($"private {ElementServiceTypeName}[] {ArrayMethodName}() =>");
            writer.Indentation++;
            writer.WriteLine("[");
            writer.Indentation++;

            foreach(var elementResolver in ElementResolvers)
            {
                writer.WriteLine($"{elementResolver.FormatExpression(false)},");
            }

            writer.Indentation--;
            writer.WriteLine("];");
            writer.Indentation--;
        }
    }
}