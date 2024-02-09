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

KubernetesClientConfiguration k8sConfig;
if (KubernetesClientConfiguration.IsInCluster())
{
    k8sConfig = KubernetesClientConfiguration.InClusterConfig();
}
else
{
    k8sConfig = KubernetesClientConfiguration.BuildConfigFromConfigFile();
}

var serviceProvider = new ServiceCollection()
    .AddSingleton(config)
    .AddSingleton(new Kubernetes(k8sConfig))
    .AddSingleton<Server>()
    .AddLogging(loggingBuilder =>
    {
        loggingBuilder.SetMinimumLevel(LogLevel.Debug);
        loggingBuilder.AddConsole();
    })
    .BuildServiceProvider(validateScopes: true);

var server = serviceProvider.GetRequiredService<Server>();
await server.Run();
