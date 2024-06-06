using k8s;
using k8s.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace Sok8t;

internal class Server(Config config, Kubernetes kubernetes, ILogger<Server> logger)
{
    const string SOK8T_POD_LABEL_NAME = "SOK8T_POD_TYPE";
    const string SOK8T_WORKER = "worker";
    private readonly Config config = config;
    private readonly Kubernetes kubernetes = kubernetes;
    private readonly ILogger<Server> logger = logger;

    public async Task Run()
    {
        this.logger.LogDebug($"Running with configuration: {this.config}");
        await this.ClearNamespace();
        TcpListener listener = new(IPAddress.IPv6Any, config.LocalPort);
        listener.Server.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.IPv6Only, false);
        listener.Start();
        this.logger.LogInformation("Accepting connections");
        while (!config.CancelToken.IsCancellationRequested)
        {
            Socket client = await listener.AcceptSocketAsync(config.CancelToken);
            this.HandleClient(client);
        }

        this.logger.LogInformation("Server finished");
    }

    private async void HandleClient(Socket client)
    {
        var endpoint = client.RemoteEndPoint!;
        this.logger.LogDebug($"HandleClient {endpoint}");
        var name = GenerateContainerName(endpoint);
        Socket? destinationClient = null;
        try
        {
            var workerLabels = new Dictionary<string, string>()
            {
                { SOK8T_POD_LABEL_NAME, SOK8T_WORKER },
            };
            List<V1LocalObjectReference>? imagePullSecrets = null;
            if (config.ImagePullSecret is string imagePullSecret)
            {
                this.logger.LogDebug($"using imagePullSecret {imagePullSecret}");
                imagePullSecrets = [new V1LocalObjectReference(imagePullSecret)];
            }
            var body = new V1Pod()
            {
                Metadata = new V1ObjectMeta()
                {
                    Name = name,
                    Labels = workerLabels,
                },
                Spec = new V1PodSpec()
                {
                    Containers = new V1Container[]
                    {
                        new()
                        {
                            Image = this.config.DestinationImage,
                            Name = name,
                            ImagePullPolicy = this.config.ImagePullPolicy, 
                        }
                    },
                    ImagePullSecrets = imagePullSecrets,
                }
            };
            var pod = await this.kubernetes.CreateNamespacedPodAsync(body, this.config.Namespace, cancellationToken: this.config.CancelToken);
            var podIp = await this.WaitForPodIpAddress(name);
            this.logger.LogInformation($"Pod spawned on {podIp}");
            destinationClient = await this.WaitForPodConnection(podIp);
            await BridgeSockets(client, destinationClient);
        }
        catch (Exception e)
        {
            this.logger.LogError($"HandleClient failed: {e}");
        }
        client.Dispose();
        destinationClient?.Dispose();
        await this.TryDeletePod(name);
        this.logger.LogDebug($"HandleClients {endpoint} done");
    }

    private async Task BridgeSockets(Socket s1, Socket s2)
    {
        this.logger.LogDebug("Bridging sockets");
        var bridgeCancelSource = new CancellationTokenSource();
        var linkedTokenSource = CancellationTokenSource.CreateLinkedTokenSource([bridgeCancelSource.Token, this.config.CancelToken]);
        var task1 = Task.Run(async () =>
        {
            try
            {
                var buf = new byte[4096];
                while (!linkedTokenSource.IsCancellationRequested)
                {
                    var read = await s1.ReceiveAsync(buf, linkedTokenSource.Token);
                    if (read == 0)
                    {
                        throw new Exception("End of s1 stream");
                    }
                    await s2.SendAsync(buf.AsMemory()[..read], linkedTokenSource.Token);
                }
            }
            catch (Exception)
            {
                bridgeCancelSource.Cancel();
            }
        });
        var task2 = Task.Run(async () =>
        {
            try
            {
                var buf = new byte[4096];
                while (!linkedTokenSource.IsCancellationRequested)
                {
                    var read = await s2.ReceiveAsync(buf, linkedTokenSource.Token);
                    if (read == 0)
                    {
                        throw new Exception("End of s2 stream");
                    }
                    await s1.SendAsync(buf.AsMemory()[..read], linkedTokenSource.Token);
                }
            }
            catch (Exception)
            {
                bridgeCancelSource.Cancel();
            }
        });
        await task1;
        await task2;
    }

    private async Task ClearNamespace()
    {
        this.logger.LogDebug("Clearing namespace");
        var pods = await this.kubernetes.CoreV1.ListNamespacedPodAsync(this.config.Namespace);
        foreach (var pod in pods)
        {
            var labels = pod.Metadata?.Labels;
            if (labels != null && labels.TryGetValue(SOK8T_POD_LABEL_NAME, out string? podType) && podType == SOK8T_WORKER)
            {
                await this.TryDeletePod(pod.Name());
            }
        }
    }

    private async Task TryDeletePod(string name)
    {
        this.logger.LogDebug($"Deleting pod {name}");
        try
        {
            await this.kubernetes.DeleteNamespacedPodAsync(name, this.config.Namespace);
        }
        catch (Exception e)
        {
            this.logger.LogWarning($"Failed to delete pod {name}: {e}");
        }
    }

    private async Task<Socket> WaitForPodConnection(string podIp)
    {
        Socket client = new(
                AddressFamily.InterNetwork,
                SocketType.Stream,
                ProtocolType.Tcp);
        while (!this.config.CancelToken.IsCancellationRequested)
        {
            try
            {
                await client.ConnectAsync(new IPEndPoint(IPAddress.Parse(podIp), this.config.DestinationPort), this.config.CancelToken);
                this.logger.LogDebug($"Successfully connected to pod service {podIp}:{this.config.DestinationPort}");
                return client;
            }
            catch (SocketException)
            {
                await Task.Delay(200);
            }
        }

        throw new OperationCanceledException();
    }

    private async Task<string> WaitForPodIpAddress(string name)
    {
        while (!this.config.CancelToken.IsCancellationRequested)
        {
            var pod = await this.kubernetes.ReadNamespacedPodAsync(name, this.config.Namespace, cancellationToken: this.config.CancelToken);
            if (pod.Status?.PodIP is string ip)
            {
                return ip;
            }
            await Task.Delay(100);
        }
        throw new OperationCanceledException();
    }

    private static string GenerateContainerName(EndPoint remote)
    {
        return "sok8t-" + remote.ToString()!.Replace("[", "").Replace("]", "").Replace(":", "-").Replace(".", "-") + "-pod";
    }
}
