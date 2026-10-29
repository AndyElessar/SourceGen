namespace SourceGen.Ioc;

partial class IocSourceGenerator
{
    private sealed record class InstanceContainerEntry(
        ServiceRegistrationModel Registration,
        string ResolverMethodName,
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

        public override string FormatResolveExpression(string serviceType, string receiver) => Registration.Instance!;

        public override string FormatResolverLambda(string serviceType) => $"static _ => {Registration.Instance}";

        public override bool HasResolverMethodLambda => false;
    }

    /// <summary>
    /// An eager singleton: the root constructor creates it before the container can be used from other threads,
    /// so its resolver needs no synchronization, and scopes copy the instance.
    /// </summary>
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
            writer.WriteLine($"private {ResolvedTypeName} {FieldName} = null!;");
        }

        public override void WriteResolver(SourceWriter writer)
        {
            var createMethodName = GetCreateMethodName(ResolverMethodName);

            // Tiny fast path (inlinable); creation lives in a separate slow-path method
            writer.WriteLine($"private {ResolvedTypeName} {ResolverMethodName}() => {FieldName} ?? {createMethodName}();");
            writer.WriteLine();

            writer.WriteLine($"private {ResolvedTypeName} {createMethodName}()");
            writer.WriteLine("{");
            writer.Indentation++;

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

        public override bool IsEagerlyInitialized => true;

        public override void WriteEagerInit(SourceWriter writer)
        {
            writer.WriteLine($"{ResolverMethodName}();");
        }

        public override void WriteCopyFromParent(SourceWriter writer)
        {
            writer.WriteLine($"{FieldName} = parent.{FieldName};");
        }

        public override void WriteDisposal(SourceWriter writer, bool isAsync)
        {
            WriteDisposeCall(writer, FieldName, isAsync);
        }
    }

    /// <param name="IsEager">
    /// Whether scope constructors create the (scoped) service eagerly. The root still creates it on demand,
    /// possibly from several threads, so the resolver stays synchronized.
    /// </param>
    private sealed record class LazyThreadSafeContainerEntry(
        ServiceRegistrationModel Registration,
        string ResolverMethodName,
        string FieldName,
        bool IsEager,
        ThreadSafeStrategy ThreadSafeStrategy,
        ImmutableEquatableArray<ResolvedConstructorParameter> ConstructorParameters,
        ImmutableEquatableArray<ResolvedInjectionMember> InjectionMembers,
        ImmutableEquatableArray<ResolvedDecorator> Decorators) : ServiceContainerEntry(Registration, ResolverMethodName, ConstructorParameters, InjectionMembers, Decorators)
    {
        // Scopes never create singletons themselves; the root owns and disposes them
        public override bool DelegatesToRoot => Registration.Lifetime == ServiceLifetime.Singleton;

        public override bool IsEagerlyInitialized => IsEager;

        public override void WriteEagerInit(SourceWriter writer)
        {
            if(IsEager)
            {
                writer.WriteLine($"{ResolverMethodName}();");
            }
        }

        public override void WriteCopyFromParent(SourceWriter writer)
        {
            writer.WriteLine($"{FieldName} = parent.{FieldName};");
        }

        public override void WriteField(SourceWriter writer)
        {
            writer.WriteLine($"private {ResolvedTypeName}? {FieldName};");
            WriteSyncField(writer, ThreadSafeStrategy, FieldName);
        }

        public override void WriteResolver(SourceWriter writer)
        {
            var createMethodName = GetCreateMethodName(ResolverMethodName);

            // Tiny fast path (inlinable); creation and synchronization live in a separate slow-path method
            writer.WriteLine($"private {ResolvedTypeName} {ResolverMethodName}() => {FieldName} ?? {createMethodName}();");
            writer.WriteLine();

            writer.WriteLine($"private {ResolvedTypeName} {createMethodName}()");
            writer.WriteLine("{");
            writer.Indentation++;

            if(DelegatesToRoot)
            {
                writer.WriteLine($"if(_root is not null) return _root.{ResolverMethodName}();");
                writer.WriteLine();
            }

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
            WriteDisposeCall(writer, FieldName, isAsync);

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
            writer.WriteLine($"var existing = global::System.Threading.Interlocked.CompareExchange(ref {FieldName}, instance, null);");
            writer.WriteLine("if(existing is not null)");
            writer.WriteLine("{");
            writer.Indentation++;
            // A losing decorator wraps the shared inner instance, so disposing it could dispose that instance too
            if(InnerResolverMethodName is null)
            {
                // Not DisposeService: resolution must not throw for an IAsyncDisposable-only losing instance
                writer.WriteLine("((object)instance as global::System.IDisposable)?.Dispose();");
            }

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
            writer.WriteLine($"private {ResolvedTypeName} {ResolverMethodName}()");
            writer.WriteLine("{");
            writer.Indentation++;

            if(InjectionMembers.Length == 0 && Decorators.Length == 0)
            {
                if(Registration.Factory is not null)
                {
                    var factory = Registration.Factory;
                    var args = factory.OrderArguments("this", Registration.Key,
                        [.. ConstructorParameters.Select(static parameter => parameter.Dependency.FormatExpression(parameter.IsOptional))]);

                    var genericTypeArgs = BuildGenericFactoryTypeArgs(factory, Registration.ServiceType);
                    var argList = string.Join(", ", args);
                    if(genericTypeArgs is null)
                    {
                        writer.WriteLine($"return ({Registration.ImplementationType.Name}){factory.Path}({argList});");
                    }
                    else
                    {
                        writer.WriteLine($"return ({Registration.ImplementationType.Name}){factory.Path}<{genericTypeArgs}>({argList});");
                    }
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
}
