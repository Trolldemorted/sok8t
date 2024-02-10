using k8s;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sok8t;
using System.CommandLine;
using System.Formats.Asn1;


class Program
{
    static async Task<int> Main(string[] args)
    {
        Argument<int> localPortArgument = new(name: "localPort", description: "The local port to listen on", getDefaultValue: () => 8000);
        Argument<int> remotePortArgument = new(name: "targetPort", description: "The target port to connect to");
        Argument<string> namespaceArgument = new(name: "namespace", description: "The target port to connect to");
        Argument<string> imageArgument = new(name: "image", description: "The image to start");
        Option<string?> imagePullSecretOption = new(name: "--imagePullSecret", description: "Pull secret to fetch image");

        RootCommand rootCommand = new("socat for kubernetes");
        rootCommand.AddArgument(localPortArgument);
        rootCommand.AddArgument(remotePortArgument);
        rootCommand.AddArgument(namespaceArgument);
        rootCommand.AddArgument(imageArgument);
        rootCommand.AddOption(imagePullSecretOption);
        rootCommand.SetHandler(Run, localPortArgument, remotePortArgument, namespaceArgument, imageArgument, imagePullSecretOption);
        await rootCommand.InvokeAsync(args);
        return 0;
    }

    public static async Task Run(int localPort, int remotePort, string k8sNamespace, string image, string? imagePullSecret)
    {
        var cancelSource = new CancellationTokenSource();
        var config = new Config(
            localPort,
            remotePort,
            k8sNamespace,
            image,
            imagePullSecret,
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
    }
}

