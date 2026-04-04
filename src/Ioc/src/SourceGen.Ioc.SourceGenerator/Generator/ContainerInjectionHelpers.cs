using static SourceGen.Ioc.SourceGenerator.Models.Constants;

namespace SourceGen.Ioc;

partial class IocSourceGenerator
{

    /// <summary>
    /// Writes instance creation with property/method injection.
    /// </summary>
    /// <param name="variableType">Optional explicit type for the variable. When null, 'var' is used.</param>
    private static void WriteInstanceCreationWithInjection(
        SourceWriter writer,
        string varName,
        ServiceRegistrationModel reg,
        bool hasFactory,
        string? variableType,
        ContainerRegistrationGroups groups)
    {
        var typeDeclaration = variableType ?? "var";

        if(hasFactory)
        {
            var factoryCall = BuildFactoryCallForContainer(reg.Factory!, reg, groups);
            writer.WriteLine($"{typeDeclaration} {varName} = ({reg.ImplementationType.Name}){factoryCall};");
            return;
        }

        WriteConstructorWithInjection(writer, varName, typeDeclaration, reg, groups);
    }

    /// <summary>
    /// Writes constructor invocation with property/field and method injection.
    /// </summary>
    private static void WriteConstructorWithInjection(
        SourceWriter writer,
        string varName,
        string typeDeclaration,
        ServiceRegistrationModel reg,
        ContainerRegistrationGroups groups)
    {
        var (properties, methods) = CategorizeInjectionMembers(reg.InjectionMembers);
        var args = BuildConstructorArgumentsString(reg, groups);

        WriteConstructorWithPropertyInitializers(writer, varName, typeDeclaration, reg.ImplementationType.Name, args, properties, groups);
        WriteMethodInjectionCalls(writer, varName, methods, reg, groups);
    }

    /// <summary>
    /// Categorizes injection members into properties/fields and methods.
    /// </summary>
    private static (List<InjectionMemberData>? Properties, List<InjectionMemberData>? Methods) CategorizeInjectionMembers(
        ImmutableEquatableArray<InjectionMemberData> injectionMembers)
    {
        List<InjectionMemberData>? properties = null;
        List<InjectionMemberData>? methods = null;

        foreach(var member in injectionMembers)
        {
            if(member.MemberType is InjectionMemberType.Property or InjectionMemberType.Field)
            {
                properties ??= [];
                properties.Add(member);
            }
            else if(member.MemberType == InjectionMemberType.Method)
            {
                methods ??= [];
                methods.Add(member);
            }
        }

        return (properties, methods);
    }

    /// <summary>
    /// Writes constructor invocation with optional property initializers.
    /// </summary>
    private static void WriteConstructorWithPropertyInitializers(
        SourceWriter writer,
        string varName,
        string typeDeclaration,
        string typeName,
        string args,
        List<InjectionMemberData>? properties,
        ContainerRegistrationGroups groups)
    {
        if(properties is not { Count: > 0 })
        {
            writer.WriteLine($"{typeDeclaration} {varName} = new {typeName}({args});");
            return;
        }

        writer.WriteLine($"{typeDeclaration} {varName} = new {typeName}({args})");
        writer.WriteLine("{");
        writer.Indentation++;

        foreach(var prop in properties)
        {
            var resolveCall = BuildServiceResolutionCallForContainer(prop.Type!, prop.Key, prop.IsNullable, groups);
            writer.WriteLine($"{prop.Name} = {resolveCall},");
        }

        writer.Indentation--;
        writer.WriteLine("};");
    }

    /// <summary>
    /// Writes method injection calls.
    /// </summary>
    private static void WriteMethodInjectionCalls(
        SourceWriter writer,
        string varName,
        List<InjectionMemberData>? methods,
        ServiceRegistrationModel reg,
        ContainerRegistrationGroups groups)
    {
        if(methods is null)
            return;

        foreach(var method in methods)
        {
            var methodArgs = method.Parameters is { Length: > 0 }
                ? string.Join(", ", method.Parameters.Select(p => BuildParameterForContainer(p, reg, groups)))
                : "";
            writer.WriteLine($"{varName}.{method.Name}({methodArgs});");
        }
    }

    /// <summary>
    /// Writes decorator application code.
    /// Decorators are applied in reverse order (from innermost to outermost),
    /// matching the behavior of Register mode.
    /// </summary>
    private static void WriteDecoratorApplication(
        SourceWriter writer,
        string varName,
        ServiceRegistrationModel reg,
        ContainerRegistrationGroups groups)
    {
        // Decorators array is in order from outermost to innermost,
        // we iterate in reverse order for building the chain from inner to outer
        var decorators = reg.Decorators;
        for(int i = decorators.Length - 1; i >= 0; i--)
        {
            var decorator = decorators[i];
            var hasInjectionMembers = decorator.InjectionMembers?.Length > 0;
            var argsString = string.Join(", ", GetDecoratorArguments(varName, decorator, groups));

            if(hasInjectionMembers)
            {
                // When decorator has injection members, use a temporary variable with concrete type
                // to allow accessing the decorator's properties/methods before assigning to interface variable
                var decoratorVarName = $"decorator{decorators.Length - 1 - i}";
                WriteDecoratorCreationWithInjection(writer, decoratorVarName, decorator, argsString, reg, groups);

                // Assign to the interface variable
                writer.WriteLine($"{varName} = {decoratorVarName};");
            }
            else
            {
                // No injection members, directly assign to the interface variable
                writer.WriteLine($"{varName} = new {decorator.Name}({argsString});");
            }
        }
    }

    /// <summary>
    /// Yields decorator constructor arguments without allocating a List.
    /// First parameter is always the inner instance (the decorated service).
    /// </summary>
    private static IEnumerable<string> GetDecoratorArguments(string innerInstance, TypeData decorator, ContainerRegistrationGroups groups)
    {
        yield return innerInstance;

        if(decorator.ConstructorParameters?.Length > 1)
        {
            // Skip the first parameter (it's the inner/decorated service)
            foreach(var param in decorator.ConstructorParameters.Skip(1))
            {
                yield return BuildServiceResolutionCallForContainer(param.Type, param.ServiceKey, param.IsNullable, groups);
            }
        }
    }

    /// <summary>
    /// Writes decorator creation with object initializer for property/field injection,
    /// and method calls for method injection.
    /// </summary>
    private static void WriteDecoratorCreationWithInjection(
        SourceWriter writer,
        string varName,
        TypeData decorator,
        string argsString,
        ServiceRegistrationModel reg,
        ContainerRegistrationGroups groups)
    {
        var injectionMembers = decorator.InjectionMembers;
        if(injectionMembers is null or { Length: 0 })
        {
            writer.WriteLine($"var {varName} = new {decorator.Name}({argsString});");
            return;
        }

        List<string>? propertyAssignments = null;
        List<string>? methodInvocations = null;

        foreach(var member in injectionMembers)
        {
            switch(member.MemberType)
            {
                case InjectionMemberType.Property:
                case InjectionMemberType.Field:
                    if(member.Type is not null)
                    {
                        var resolveCall = BuildServiceResolutionCallForContainer(member.Type, member.Key, member.IsNullable, groups);
                        propertyAssignments ??= [];
                        propertyAssignments.Add($"{member.Name} = {resolveCall},");
                    }
                    break;

                case InjectionMemberType.Method:
                    var methodArgs = member.Parameters is { Length: > 0 }
                        ? string.Join(", ", member.Parameters.Select(p => BuildParameterForInjectionMethod(p, reg, groups)))
                        : "";
                    methodInvocations ??= [];
                    methodInvocations.Add($"{varName}.{member.Name}({methodArgs});");
                    break;
            }
        }

        WriteDecoratorConstructorWithProperties(writer, varName, decorator.Name, argsString, propertyAssignments);

        if(methodInvocations is not null)
        {
            foreach(var invocation in methodInvocations)
            {
                writer.WriteLine(invocation);
            }
        }
    }

    /// <summary>
    /// Writes decorator constructor with optional property initializers.
    /// </summary>
    private static void WriteDecoratorConstructorWithProperties(
        SourceWriter writer,
        string varName,
        string decoratorName,
        string argsString,
        List<string>? propertyAssignments)
    {
        if(propertyAssignments is not { Count: > 0 })
        {
            writer.WriteLine($"var {varName} = new {decoratorName}({argsString});");
            return;
        }

        writer.WriteLine($"var {varName} = new {decoratorName}({argsString})");
        writer.WriteLine("{");
        writer.Indentation++;

        foreach(var assignment in propertyAssignments)
        {
            writer.WriteLine(assignment);
        }

        writer.Indentation--;
        writer.WriteLine("};");
    }

    /// <summary>
    /// Builds instance creation inline (for return statements).
    /// </summary>
    private static string BuildInstanceCreationInline(
        ServiceRegistrationModel reg,
        bool hasFactory,
        ContainerRegistrationGroups groups)
    {
        if(hasFactory)
        {
            var factoryCall = BuildFactoryCallForContainer(reg.Factory!, reg, groups);
            return $"({reg.ImplementationType.Name}){factoryCall}";
        }

        var args = BuildConstructorArgumentsString(reg, groups);
        return $"new {reg.ImplementationType.Name}({args})";
    }

    /// <summary>
    /// Builds constructor arguments as a string.
    /// </summary>
    private static string BuildConstructorArgumentsString(ServiceRegistrationModel reg, ContainerRegistrationGroups groups)
    {
        var parameters = reg.ImplementationType.ConstructorParameters;
        if(parameters is null or { Length: 0 })
            return "";

        return string.Join(", ", parameters.Select(p => BuildParameterForContainer(p, reg, groups)));
    }

    /// <summary>
    /// Builds a single parameter for injection method (handles IServiceProvider and service resolution).
    /// </summary>
    private static string BuildParameterForInjectionMethod(ParameterData param, ServiceRegistrationModel reg, ContainerRegistrationGroups groups)
    {
        if(param.Type.Name is IServiceProviderTypeName or IServiceProviderGlobalTypeName)
            return "this";

        return BuildServiceResolutionCallForContainer(param.Type, param.ServiceKey, param.IsOptional, groups);
    }

    /// <summary>
    /// Builds a single parameter for container (constructor, method injection, or factory).
    /// Handles [ServiceKey], [FromKeyedServices], IServiceProvider, and regular service resolution.
    /// </summary>
    private static string BuildParameterForContainer(ParameterData param, ServiceRegistrationModel reg, ContainerRegistrationGroups groups)
    {
        if(param.HasServiceKeyAttribute)
            return reg.Key ?? "null";

        if(param.Type.Name is IServiceProviderTypeName or IServiceProviderGlobalTypeName)
            return "this";

        return BuildServiceResolutionCallForContainer(param.Type, param.ServiceKey, param.IsOptional, groups);
    }
}
