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
    private readonly Config config = config;
    private readonly Kubernetes kubernetes = kubernetes;
    private readonly ILogger<Server> logger = logger;

    public async Task Run()
    {
        TcpListener listener = new(IPAddress.IPv6Any, config.LocalPort);
        listener.Server.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.IPv6Only, false);
        listener.Start();
        while (!config.CancelToken.IsCancellationRequested)
        {
            Socket client = await listener.AcceptSocketAsync(config.CancelToken);
            this.HandleClient(client);
        }

        this.logger.LogInformation("Server finished");
    }

    private async void HandleClient(Socket client)
    {
        this.logger.LogDebug($"HandleClient {client.RemoteEndPoint}");
        var name = GenerateContainerName(client.RemoteEndPoint!);
        Socket? destinationClient = null;
        try
        {
            var body = new V1Pod()
            {
                Metadata = new V1ObjectMeta()
                {
                    Name = name,
                },
                Spec = new V1PodSpec()
                {
                    Containers = new V1Container[]
                    {
                        new()
                        {
                            Image = this.config.DestinationImage,
                            Name = name,
                        }
                    }
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
        await this.DeletePod(name);
        this.logger.LogDebug($"HandleClients {client.RemoteEndPoint}");
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

    private async Task DeletePod(string name)
    {
        this.logger.LogDebug($"Deleting pod {name}");
        await this.kubernetes.DeleteNamespacedPodAsync(name, this.config.Namespace);
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
