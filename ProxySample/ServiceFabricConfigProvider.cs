using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;
using Microsoft.ReverseProxy.Abstractions;
using Microsoft.ReverseProxy.Configuration;
using Microsoft.ReverseProxy.Service;
using System.IO;
using System.Text;
using SFService = System.Fabric.Query.Service;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.ReverseProxy.Configuration.ServiceFabric;
using Microsoft.Azure.Management.ServiceFabric;
using System.Fabric;
using System.Collections.Concurrent;
using System.Linq;
using System.Fabric.Query;
using Microsoft.AspNetCore.Routing;
using System;
using System.Threading.Tasks.Dataflow;

namespace Microsoft.Extensions.DependencyInjection
{
    public static class ServiceFabricConfigProviderExtensions
    {
        public static IReverseProxyBuilder LoadFromServiceFabric(this IReverseProxyBuilder builder)
        {
            builder.Services.AddSingleton<IProxyConfigProvider, ServiceFabricConfigProvider>();
            return builder;
        }
    }
}
namespace Microsoft.ReverseProxy.Configuration.ServiceFabric
{
    class ServiceFabricConfig : IProxyConfig
    {
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();

        private List<ProxyRoute> _routes;
        public IReadOnlyList<ProxyRoute> Routes => _routes;

        private List<Cluster> _clusters;
        public IReadOnlyList<Cluster> Clusters => _clusters;
        public IChangeToken ChangeToken { get; }

        public ServiceFabricConfig(List<Cluster> clusters = null, List<ProxyRoute> routes = null)
        {
            _routes = routes ?? new List<ProxyRoute>();
            _clusters = clusters ?? new List<Cluster>();
            ChangeToken = new CancellationChangeToken(_cts.Token);
        }

        internal void SignalChange()
        {
            _cts.Cancel();
        }
    }
    public class ServiceFabricConfigProvider : IProxyConfigProvider
    {
        private static string _clusterConnection;
        
        public ServiceFabricConfigProvider(IConfiguration config)
        {
            _clusterConnection = config["ServiceFabricClusterConnection"];
            _config = new ServiceFabricConfig();
            _fabricClient = new FabricClient(_clusterConnection);
        }

        private static ServiceFabricConfig _config;
        private static FabricClient _fabricClient;

        public IProxyConfig GetConfig()
        {
            return _config;
            // Go to SF and get configuration data from the REST API and convert to IProxyConfig
            // throw new System.NotImplementedException();
        }

        public static IProxyConfig GetCurrentConfig() => _config;

        public static async Task Update()
        {

            try
            {
                var routes = new ConcurrentBag<ProxyRoute>();
                var clusters = new ConcurrentBag<Cluster>();

                ApplicationList apps = null;
                do
                {
                    apps = await _fabricClient.QueryManager.GetApplicationPagedListAsync(new System.Fabric.Description.ApplicationQueryDescription()
                    {
                        MaxResults = Int32.MaxValue
                    });

                    await apps.AsyncParallelForEach(async app =>
                    {
                        ServiceList services = null; 

                        do
                        {
                            services = await _fabricClient.QueryManager.GetServicePagedListAsync(new System.Fabric.Description.ServiceQueryDescription(app.ApplicationName) { MaxResults = Int32.MaxValue });

                            await services.AsyncParallelForEach(async service =>
                            {
                                var cluster = new Cluster();
                                var serviceName = service.ServiceName.ToString().Replace("fabric:/", "");
                                cluster.Id = serviceName;
                                clusters.Add(cluster);
                                var destinations = new ConcurrentDictionary<string, Destination>();

                                { // Add Catch All
                                    var route = new ProxyRoute();
                                    route.RouteId = serviceName + ":catch-all";
                                    route.ClusterId = serviceName;
                                    route.Match.Path = serviceName + "/{**catch-all}";
                                    route.Transforms = new List<IDictionary<string, string>>();
                                    route.AddTransformPathRemovePrefix(new AspNetCore.Http.PathString("/" + serviceName));
                                    route.AddTransformRequestHeader("X-Forwarded-PathBase", "/" + serviceName);

                                    routes.Add(route);
                                }
                                { // Add root match
                                    var route = new ProxyRoute();
                                    route.RouteId = serviceName + ":root-match";
                                    route.ClusterId = serviceName;
                                    route.Match.Path = serviceName;
                                    route.Transforms = new List<IDictionary<string, string>>();
                                    route.AddTransformPathRemovePrefix(new AspNetCore.Http.PathString("/" + serviceName));
                                    route.AddTransformRequestHeader("X-Forwarded-PathBase", "/" + serviceName);
                                    routes.Add(route);
                                }

                                ServicePartitionList partitions = null;

                                do
                                {
                                    partitions = partitions == null ?
                                        await _fabricClient.QueryManager.GetPartitionListAsync(service.ServiceName) :
                                        await _fabricClient.QueryManager.GetPartitionListAsync(app.ApplicationName, services.ContinuationToken);
                               
                                    await partitions.AsyncParallelForEach(async partition =>
                                    {
                                        var partitionId = partition.PartitionInformation.Id;                                        
                                        ServiceReplicaList replicas = null;

                                        do
                                        {
                                            replicas = replicas == null ?
                                                await _fabricClient.QueryManager.GetReplicaListAsync(partitionId) :
                                                await _fabricClient.QueryManager.GetReplicaListAsync(partitionId, services.ContinuationToken);

                                            await replicas.AsyncParallelForEach(async replica =>
                                            {
                                                var endpointSet = JsonSerializer.Deserialize<ReplicaAddress>(replica.ReplicaAddress);
                                                foreach (var endpoint in endpointSet.Endpoints)
                                                {
                                                    var destination = new Destination();
                                                    destination.Address = endpoint.Value;
                                                    destinations.TryAdd($"{partitionId}:{replica.Id}", destination);
                                                }
                                            });
                                        }
                                        while (!string.IsNullOrEmpty(replicas.ContinuationToken));
                                    });
                                }
                                while (!string.IsNullOrEmpty(partitions.ContinuationToken));
                                foreach (var dest in destinations)
                                {
                                    cluster.Destinations.Add(dest);
                                }
                            });
                        } 
                        while (!string.IsNullOrEmpty(services.ContinuationToken));
                    });
                } 
                while (!string.IsNullOrEmpty(apps.ContinuationToken));


                var config = new ServiceFabricConfig(clusters.ToList(), routes.ToList());
                var oldConfig = _config;
                _config = config;
                oldConfig.SignalChange();
            }catch (Exception ex)
            {

            }
        }

    }
    public class ReplicaAddress
    {
        public Dictionary<string, string> Endpoints { get; set; }
    }


    public static class AsyncExtensions
    {
        public static Task AsyncParallelForEach<T>(this IEnumerable<T> source, Func<T, Task> body, int maxDegreeOfParallelism = DataflowBlockOptions.Unbounded, TaskScheduler scheduler = null)
        {
            var options = new ExecutionDataflowBlockOptions
            {
                MaxDegreeOfParallelism = maxDegreeOfParallelism
            };
            if (scheduler != null)
                options.TaskScheduler = scheduler;
            var block = new ActionBlock<T>(body, options);
            foreach (var item in source)
                block.Post(item);
            block.Complete();
            return block.Completion;
        }
    }
}