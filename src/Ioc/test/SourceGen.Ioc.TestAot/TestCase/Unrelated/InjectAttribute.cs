namespace SourceGen.Ioc.TestAot.TestCase.Unrelated;

/// <summary>An attribute named like Blazor's <c>InjectAttribute</c> that the component property activator must ignore.</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class InjectAttribute : Attribute;
