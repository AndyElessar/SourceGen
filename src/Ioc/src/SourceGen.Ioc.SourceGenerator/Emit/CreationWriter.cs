using SourceGen.Ioc.Model;

namespace SourceGen.Ioc.Emit;

/// <summary>
/// Writes the steps that create an instance of a closed definition (Annotations.spec, "Order"): <c>new</c> with the
/// constructor arguments and the object initializer, the assignments, and the injection methods. The container and the
/// service collection extension give the expression of each argument.
/// </summary>
internal static class CreationWriter
{
    /// <summary>Writes the creation into the local <c>instance</c>.</summary>
    /// <param name="writer">The writer.</param>
    /// <param name="definition">The closed definition.</param>
    /// <param name="declare">Whether to declare the local <c>instance</c>.</param>
    /// <param name="argument">The expression of the dependency at an index of <see cref="ClosedDefinition.Dependencies"/>.</param>
    /// <param name="assignment">
    /// The statement that assigns the dependency at an index to a member, or <see langword="null"/> to keep the initializer.
    /// </param>
    /// <param name="disposeOnFailure">
    /// Whether a failure after <c>new</c> disposes the instance and throws again (Container.Output.spec and
    /// ServiceCollection.Output.spec, "Failure during creation"). The caller has the helper <c>__DisposeLost</c>.
    /// </param>
    public static void Write(SourceWriter writer, ClosedDefinition definition, bool declare, Func<int, string> argument, Func<int, string, string?> assignment, bool disposeOnFailure)
    {
        string implementation = TypeNames.CSharp(definition.Implementation);
        ImmutableEquatableArray<Dependency> parameters = definition.ConstructorParameters;

        // A parameter by reference gets a local variable, passed with "in"
        for(int i = 0; i < parameters.Length; i++)
        {
            if(parameters[i].ByReference)
            {
                writer.WriteLine($"{TypeNames.CSharp(parameters[i].Type)} arg{i} = {argument(i)};");
            }
        }

        InjectedMember[] initializers = [.. definition.Members.Where(static m => m.Kind == MemberKind.Initializer)];
        string end = initializers.Length == 0 ? ";" : string.Empty;
        string open = parameters.Length == 0 ? ")" : string.Empty;
        if(declare)
        {
            writer.WriteLine($"{implementation} instance = new {implementation}({open}{(parameters.Length == 0 ? end : string.Empty)}");
        }
        else
        {
            writer.WriteLine($"instance = new {implementation}({open}{(parameters.Length == 0 ? end : string.Empty)}");
        }

        if(parameters.Length > 0)
        {
            writer.Indentation++;
            for(int i = 0; i < parameters.Length; i++)
            {
                bool last = i == parameters.Length - 1;
                if(parameters[i].ByReference)
                {
                    writer.WriteLine($"in arg{i}{(last ? ")" : ",")}{(last ? end : string.Empty)}");
                }
                else
                {
                    writer.WriteLine($"{argument(i)}{(last ? ")" : ",")}{(last ? end : string.Empty)}");
                }
            }

            writer.Indentation--;
        }

        int index = parameters.Length;
        if(initializers.Length > 0)
        {
            writer.WriteLine('{');
            writer.Indentation++;
            foreach(InjectedMember member in initializers)
            {
                writer.WriteLine($"{member.Name} = {argument(index++)},");
            }

            writer.Indentation--;
            writer.WriteLine("};");
        }

        if(!definition.HasStepsAfterNew)
        {
            return;
        }

        if(disposeOnFailure)
        {
            writer.WriteLine();
            writer.WriteLine("try");
            writer.WriteLine('{');
            writer.Indentation++;
        }

        foreach(InjectedMember member in definition.Members.Where(static m => m.Kind == MemberKind.Assignment))
        {
            if(assignment(index++, member.Name) is { } statement)
            {
                writer.WriteLine(statement);
            }
        }

        foreach(InjectedMember member in definition.Members.Where(static m => m.Kind == MemberKind.Method))
        {
            int count = member.Dependencies.Length;
            if(count == 0)
            {
                writer.WriteLine($"instance.{member.Name}();");
                continue;
            }

            writer.WriteLine($"instance.{member.Name}(");
            writer.Indentation++;
            for(int i = 0; i < count; i++)
            {
                writer.WriteLine($"{argument(index++)}{(i < count - 1 ? "," : ");")}");
            }

            writer.Indentation--;
        }

        if(disposeOnFailure)
        {
            // The instance is not stored, so nothing else disposes it. An exception of the disposal goes to the callback
            writer.Indentation--;
            writer.WriteLine('}');
            writer.WriteLine("catch");
            writer.WriteLine('{');
            writer.WriteLine("    __DisposeLost(instance);");
            writer.WriteLine("    throw;");
            writer.WriteLine('}');
            writer.WriteLine();
        }
    }
}
