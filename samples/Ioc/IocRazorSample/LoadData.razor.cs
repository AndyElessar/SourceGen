using SourceGen.Ioc;
using IocSample.Shared;
using Microsoft.AspNetCore.Components;

namespace IocRazorSample;

[IocRegister]
partial class LoadData
{
    [Inject]
    public ILogger<LoadData> Logger { get; set; } = null!;
}