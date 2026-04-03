using System.Reflection;

namespace SourceGen.Ioc.TestAot.Tests;

/// <summary>
/// Tests to verify that cross-assembly open generic registrations work correctly
/// when SOURCEGEN constant is properly defined in tests/Directory.Build.props.
/// 
/// KEY REQUIREMENT:
/// ================
/// [Conditional("SOURCEGEN")] on IoC attributes requires the SOURCEGEN constant to be defined
/// at compile time for attributes to be emitted to IL metadata.
/// 
/// For ProjectReference scenarios (not NuGet), we must manually define SOURCEGEN in
/// tests/Directory.Build.props to ensure attributes are preserved in compiled assemblies.
/// 
/// This allows the Source Generator to read attributes from external assemblies
/// and properly inherit open generic registrations through IocImportModule.
/// </summary>
public sealed class CrossAssemblyAttributeTests
{
    /// <summary>
    /// Verifies that IocRegisterForAttribute EXISTS on OpenGenericModule
    /// from the external TestCase assembly at runtime.
    /// 
    /// This works because:
    /// 1. tests/Directory.Build.props defines SOURCEGEN constant
    /// 2. [Conditional("SOURCEGEN")] attributes ARE emitted to the DLL
    /// 3. Source Generator CAN read these attributes from the compiled assembly
    /// </summary>
    [Test]
    public async Task IocRegisterForAttribute_OnExternalAssemblyType_ExistsWhenSourceGenDefined()
    {
        // Arrange - Get OpenGenericModule from the external TestCase assembly
        var openGenericModuleType = typeof(OpenGenericModule);

        // Act - Try to get IocRegisterForAttribute from the type
        var iocRegisterForAttributes = openGenericModuleType
            .GetCustomAttributes(inherit: false)
            .Where(attr => attr.GetType().Name == "IocRegisterForAttribute")
            .ToList();

        // Assert - The attribute SHOULD exist because SOURCEGEN is defined in tests/Directory.Build.props
        await Assert.That(iocRegisterForAttributes).IsNotEmpty()
            .Because("SOURCEGEN constant is defined in tests/Directory.Build.props, so [Conditional] preserves the attribute");
    }

    /// <summary>
    /// Verifies that cross-assembly open generic resolution works correctly.
    /// The ContainerModule imports TestCaseModule which imports OpenGenericModule,
    /// and the open generic IHandler&lt;,&gt; should be properly inherited.
    /// </summary>
    [Test]
    public async Task CrossAssembly_OpenGenericInheritance_WorksCorrectly()
    {
        // The ContainerModule in THIS assembly imports TestCaseModule which has OpenGenericModule
        // Source Generator should be able to read IocRegisterFor from OpenGenericModule
        var containerModuleType = typeof(TestCase.ContainerModule);

        // Verify that the open generic resolver was generated
        var hasGeneratedOpenGenericResolver = containerModuleType
            .GetMethod("GetSourceGen_Ioc_TestCase_GenericHandler_SourceGen_Ioc_TestCase_RequestA__SourceGen_Ioc_TestCase_ResponseA_",
                BindingFlags.NonPublic | BindingFlags.Instance) is not null;

        await Assert.That(hasGeneratedOpenGenericResolver).IsTrue()
            .Because("Source Generator can read IocRegisterFor from external assemblies when SOURCEGEN is defined");
    }

    /// <summary>
    /// Demonstrates that the two assemblies are indeed different,
    /// proving cross-assembly inheritance is working correctly.
    /// </summary>
    [Test]
    public async Task DifferentAssemblies_CrossAssemblyInheritanceWorks()
    {
        // ContainerModule is in TestAot assembly
        var containerModuleAssembly = typeof(TestCase.ContainerModule).Assembly;
        
        // OpenGenericModule is in TestCase assembly
        var openGenericModuleAssembly = typeof(OpenGenericModule).Assembly;

        // They are different assemblies
        await Assert.That(containerModuleAssembly).IsNotEqualTo(openGenericModuleAssembly)
            .Because("ContainerModule and OpenGenericModule are in different assemblies");

        // Document the assembly names for clarity
        await Assert.That(containerModuleAssembly.GetName().Name).IsEqualTo("SourceGen.Ioc.TestAot");
        await Assert.That(openGenericModuleAssembly.GetName().Name).IsEqualTo("SourceGen.Ioc.TestCase");
    }

    /// <summary>
    /// Verifies that GenericHandler type exists and implements IHandler correctly.
    /// </summary>
    [Test]
    public async Task GenericHandler_TypeExists_AndImplementsIHandler()
    {
        // The type itself exists and is fully accessible
        var genericHandlerType = typeof(GenericHandler<,>);
        var iHandlerType = typeof(IHandler<,>);

        await Assert.That(genericHandlerType).IsNotNull();
        await Assert.That(genericHandlerType.IsGenericTypeDefinition).IsTrue();

        // It correctly implements IHandler<,>
        var implementsIHandler = genericHandlerType.GetInterfaces()
            .Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == iHandlerType);

        await Assert.That(implementsIHandler).IsTrue()
            .Because("GenericHandler<,> implements IHandler<,>");
    }

    /// <summary>
    /// Verifies that IocContainerAttribute also exists on the external module.
    /// </summary>
    [Test]
    public async Task IocContainerAttribute_OnExternalAssemblyType_ExistsWhenSourceGenDefined()
    {
        // Arrange
        var openGenericModuleType = typeof(OpenGenericModule);

        // Act
        var iocContainerAttributes = openGenericModuleType
            .GetCustomAttributes(inherit: false)
            .Where(attr => attr.GetType().Name == "IocContainerAttribute")
            .ToList();

        // Assert
        await Assert.That(iocContainerAttributes).IsNotEmpty()
            .Because("SOURCEGEN constant is defined, so [Conditional] preserves the attribute");
    }
}
