namespace SourceGen.Ioc;

partial class IocSourceGenerator
{
    /// <summary>
    /// Base for async-init service entries whose entry method returns <c>Task&lt;T&gt;</c>.
    /// </summary>
    private closed record class AsyncServiceContainerEntry(
        ServiceRegistrationModel Registration,
        string ResolverMethodName,
        ImmutableEquatableArray<ResolvedConstructorParameter> ConstructorParameters,
        ImmutableEquatableArray<ResolvedInjectionMember> InjectionMembers,
        ImmutableEquatableArray<ResolvedDecorator> Decorators) : ServiceContainerEntry(Registration, ResolverMethodName, ConstructorParameters, InjectionMembers, Decorators)
    {
        /// <summary>
        /// Gets the name of the method that callers invoke to obtain the <c>Task&lt;T&gt;</c>.
        /// </summary>
        public abstract string AsyncEntryMethodName { get; }

        protected string TaskReturnTypeName => $"global::System.Threading.Tasks.Task<{ResolvedTypeName}>";

        /// <summary>
        /// Async-init services are exposed as <c>Task&lt;ServiceType&gt;</c> instead of <c>ServiceType</c>,
        /// because their resolver returns a task.
        /// </summary>
        public override string GetExposedServiceType(string serviceType) => $"global::System.Threading.Tasks.Task<{serviceType}>";

        /// <summary>
        /// Gets whether exposing this entry as <c>Task&lt;serviceType&gt;</c> requires projecting the resolver task,
        /// because <c>Task&lt;T&gt;</c> is invariant.
        /// </summary>
        public bool NeedsTaskProjection(string serviceType) => serviceType != ResolvedTypeName;

        /// <summary>
        /// Formats the call returning <c>Task&lt;serviceType&gt;</c>.
        /// </summary>
        public override string FormatResolveExpression(string serviceType, string receiver)
        {
            var call = $"{receiver}{AsyncEntryMethodName}()";
            return NeedsTaskProjection(serviceType)
                ? $"{ProjectTaskMethodName}<{serviceType}, {ResolvedTypeName}>({call})"
                : call;
        }

        public override bool HasResolverMethodLambda => false;

        protected void WriteCreateMethod(SourceWriter writer)
        {
            writer.WriteLine($"private async {TaskReturnTypeName} {GetAsyncCreateMethodName(ResolverMethodName)}()");
            writer.WriteLine("{");
            writer.Indentation++;

            WriteAsyncInstanceCreationBody(writer);

            writer.Indentation--;
            writer.WriteLine("}");
        }
    }

    private sealed record class AsyncContainerEntry(
        ServiceRegistrationModel Registration,
        string ResolverMethodName,
        string FieldName,
        bool IsEager,
        ThreadSafeStrategy ThreadSafeStrategy,
        ImmutableEquatableArray<ResolvedConstructorParameter> ConstructorParameters,
        ImmutableEquatableArray<ResolvedInjectionMember> InjectionMembers,
        ImmutableEquatableArray<ResolvedDecorator> Decorators) : AsyncServiceContainerEntry(Registration, ResolverMethodName, ConstructorParameters, InjectionMembers, Decorators)
    {
        public override string AsyncEntryMethodName => GetAsyncResolverMethodName(ResolverMethodName);

        public override bool IsEagerlyInitialized => IsEager;

        // Scopes never create singletons themselves; the root owns and disposes them
        public override bool DelegatesToRoot => Registration.Lifetime == ServiceLifetime.Singleton;

        public override void WriteCopyFromParent(SourceWriter writer)
        {
            writer.WriteLine($"{FieldName} = parent.{FieldName};");
        }

        public override void WriteField(SourceWriter writer)
        {
            writer.WriteLine($"private {TaskReturnTypeName}? {FieldName};");

            if(ThreadSafeStrategy == ThreadSafeStrategy.SemaphoreSlim)
            {
                writer.WriteLine($"private readonly global::System.Threading.SemaphoreSlim {FieldName}Semaphore = new(1, 1);");
            }
        }

        public override void WriteResolver(SourceWriter writer)
        {
            var createMethodName = GetAsyncCreateMethodName(ResolverMethodName);

            writer.WriteLine($"private async {TaskReturnTypeName} {AsyncEntryMethodName}()");
            writer.WriteLine("{");
            writer.Indentation++;

            writer.WriteLine($"if({FieldName} is not null)");
            writer.Indentation++;
            writer.WriteLine($"return await {FieldName}.ConfigureAwait(false);");
            writer.Indentation--;

            if(DelegatesToRoot)
            {
                writer.WriteLine("if(_root is not null)");
                writer.Indentation++;
                writer.WriteLine($"return await _root.{AsyncEntryMethodName}().ConfigureAwait(false);");
                writer.Indentation--;
            }

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

            WriteCreateMethod(writer);
        }

        public override void WriteEagerInit(SourceWriter writer)
        {
            if(IsEager)
            {
                writer.WriteLine($"_ = {AsyncEntryMethodName}();");
            }
        }

        public override void WriteDisposal(SourceWriter writer, bool isAsync)
        {
            WriteDisposeCall(writer, FieldName, isAsync);

            if(ThreadSafeStrategy == ThreadSafeStrategy.SemaphoreSlim)
            {
                writer.WriteLine($"{FieldName}Semaphore.Dispose();");
            }
        }

        private void WriteAsyncResolverBodyNone(SourceWriter writer, string createMethodName)
        {
            writer.WriteLine($"{FieldName} = {createMethodName}();");
            writer.WriteLine($"return await {FieldName}.ConfigureAwait(false);");
        }

        private void WriteAsyncResolverBodySemaphoreSlim(SourceWriter writer, string createMethodName)
        {
            writer.WriteLine($"await {FieldName}Semaphore.WaitAsync().ConfigureAwait(false);");
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

            writer.WriteLine($"return await {FieldName}.ConfigureAwait(false);");
        }
    }

    private sealed record class AsyncTransientContainerEntry(
        ServiceRegistrationModel Registration,
        string ResolverMethodName,
        ImmutableEquatableArray<ResolvedConstructorParameter> ConstructorParameters,
        ImmutableEquatableArray<ResolvedInjectionMember> InjectionMembers,
        ImmutableEquatableArray<ResolvedDecorator> Decorators) : AsyncServiceContainerEntry(Registration, ResolverMethodName, ConstructorParameters, InjectionMembers, Decorators)
    {
        public override string AsyncEntryMethodName => GetAsyncCreateMethodName(ResolverMethodName);

        public override void WriteResolver(SourceWriter writer) => WriteCreateMethod(writer);
    }
}
