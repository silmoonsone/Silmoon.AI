// create a general host

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Silmoon.AI.HostingTest.Services;
using Silmoon.AspNetCore.Extensions;
using Silmoon.AspNetCore.Interfaces;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSingleton<ISilmoonConfigureService, SilmoonConfigureServiceImpl>();
builder.Services.AddSingleton<AIService>();
builder.Services.AddHostedService(provider => provider.GetRequiredService<AIService>());

builder.Services.AddSilmoonConfigure<SilmoonConfigureServiceImpl>(o =>
{
#if DEBUG
    o.DebugConfig();
#else
    o.ReleaseConfig();
#endif
});

var host = builder.Build();
await host.RunAsync();

