using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Silmoon.AI.Terminal.Services;
using Silmoon.Extensions.Hosting.Extensions;
using Silmoon.Extensions.Hosting.Interfaces;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSingleton<ISilmoonConfigureService, SilmoonConfigureServiceImpl>();
builder.Services.AddSingleton<ClientService>();
builder.Services.AddSingleton<LocalMcpService>();
builder.Services.AddHostedService(provider => provider.GetRequiredService<ClientService>());

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

