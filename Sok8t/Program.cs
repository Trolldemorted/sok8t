using k8s;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sok8t;

var cancelSource = new CancellationTokenSource();
var config = new Config(
    8000,
    80,
    "nginx",
    "testns",
    cancelSource.Token);

var serviceProvider = new ServiceCollection()
    .AddSingleton(config)
    .AddSingleton(new Kubernetes(KubernetesClientConfiguration.BuildConfigFromConfigFile()))
    .AddSingleton<Server>()
    .AddLogging(loggingBuilder =>
    {
        loggingBuilder.SetMinimumLevel(LogLevel.Debug);
        loggingBuilder.AddConsole();
    })
    .BuildServiceProvider(validateScopes: true);

var server = serviceProvider.GetRequiredService<Server>();
await server.Run();
