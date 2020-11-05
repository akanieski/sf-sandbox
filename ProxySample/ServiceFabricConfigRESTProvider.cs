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
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.ReverseProxy.Configuration.ServiceFabricREST;

namespace Microsoft.Extensions.DependencyInjection
{
    public static class ServiceFabricConfigRESTProviderExtensions
    {
        public static IReverseProxyBuilder LoadFromServiceFabricREST(this IReverseProxyBuilder builder)
        {
            builder.Services.AddSingleton<IProxyConfigProvider, ServiceFabricConfigRESTProvider>();
            return builder;
        }
    }
}
namespace Microsoft.ReverseProxy.Configuration.ServiceFabricREST
{
    class ServiceFabricConfig : IProxyConfig
    {
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        internal void SignalChange()
        {
            _cts.Cancel();
        }
        public ServiceFabricConfig(List<Cluster> clusters = null, List<ProxyRoute> routes = null)
        {
            _routes = routes ?? new List<ProxyRoute>();
            _clusters = clusters ?? new List<Cluster>();
            ChangeToken = new CancellationChangeToken(_cts.Token);

        }
        private List<ProxyRoute> _routes;
        public IReadOnlyList<ProxyRoute> Routes => _routes;

        private List<Cluster> _clusters;
        public IReadOnlyList<Cluster> Clusters => _clusters;
        public IChangeToken ChangeToken { get; }
    }
    public class ServiceFabricConfigRESTProvider : IProxyConfigProvider
    {
        private static string _sfUri;
        public ServiceFabricConfigRESTProvider(IConfiguration config)
        {
            _sfUri = config["ServiceFabricUri"];
            _config = new ServiceFabricConfig();
        }
        private static ServiceFabricConfig _config;
        public IProxyConfig GetConfig()
        {
            return _config;
            // Go to SF and get configuration data from the REST API and convert to IProxyConfig
            // throw new System.NotImplementedException();
        }
        public static IProxyConfig GetCurrentConfig() => _config;

        public static async Task Update()
        {
            var clusters = new List<Cluster>();
            var routes = new List<ProxyRoute>();
            var config = new ServiceFabricConfig(clusters, routes);


            using (var client = new HttpClient())
            {
                var strApps = await client.GetStringAsync($"{_sfUri}/Applications?api-version=3.0");
                var appResponse = await JsonSerializer.DeserializeAsync<ServiceFabricResponse<Application>>(new MemoryStream(Encoding.UTF8.GetBytes(strApps)));

                foreach (var app in appResponse.Items)
                {
                    var appName = app.Name.Replace("fabric:/", "");
                    var strService = await client.GetStringAsync($"{_sfUri}/Applications/{appName}/$/GetServices?api-version=3.0");
                    var serviceResponse = await JsonSerializer.DeserializeAsync<ServiceFabricResponse<Service>>(new MemoryStream(Encoding.UTF8.GetBytes(strService)));

                    foreach (var service in serviceResponse.Items)
                    {
                        var serviceName = service.Name.Replace($"fabric:/", "");
                        var strPartitions = await client.GetStringAsync($"{_sfUri}/Applications/{appName}/$/GetServices/{serviceName}/$/GetPartitions?api-version=3.0");
                        var partitionResponse = await JsonSerializer.DeserializeAsync<ServiceFabricResponse<Partition>>(new MemoryStream(Encoding.UTF8.GetBytes(strPartitions)));

                        var cluster = new Cluster();
                        cluster.Id = serviceName;
                        clusters.Add(cluster);

                        { // Add Catch All
                            var route = new ProxyRoute();
                            route.RouteId = serviceName + ":catch-all";
                            route.ClusterId = serviceName;
                            route.Match.Path = serviceName + "/{**catch-all}";
                            route.Transforms = new List<IDictionary<string, string>>();
                            route.Transforms.Add(new Dictionary<string, string>() {
                                {"PathRemovePrefix", serviceName}
                            });
                            route.AddTransformRequestHeader("X-Forwarded-PathBase", "/" + serviceName);
          
                            routes.Add(route);
                        }
                        { // Add root match
                            var route = new ProxyRoute();
                            route.RouteId = serviceName + ":root-match";
                            route.ClusterId = serviceName;
                            route.Match.Path = serviceName;
                            route.Transforms = new List<IDictionary<string, string>>();
                            route.Transforms.Add(new Dictionary<string, string>() {
                                {"PathRemovePrefix", serviceName}
                            });
                            route.AddTransformRequestHeader("X-Forwarded-PathBase", "/" + serviceName);
                            routes.Add(route);
                        }

                        foreach (var partition in partitionResponse.Items)
                        {
                            var partitionId = partition.PartitionInformation.Id;
                            var strReplicas = await client.GetStringAsync($"{_sfUri}/Applications/{appName}/$/GetServices/{serviceName}/$/GetPartitions/{partitionId}/$/GetReplicas?api-version=3.0");
                            var replicasResponse = await JsonSerializer.DeserializeAsync<ServiceFabricResponse<Replica>>(new MemoryStream(Encoding.UTF8.GetBytes(strReplicas)));

                            foreach (var replica in replicasResponse.Items)
                            {
                                var replicaAddress = await JsonSerializer.DeserializeAsync<ReplicaAddress>(new MemoryStream(Encoding.UTF8.GetBytes(replica.Address)));
                                foreach (var endpoint in replicaAddress.Endpoints)
                                {
                                    var destination = new Destination();
                                    destination.Address = endpoint.Value;
                                    cluster.Destinations.Add($"{partitionId}:{replica.InstanceId}", destination);

                                }
                            }
                        }
                    }
                }
                var oldConfig = _config;
                _config = config;
                oldConfig.SignalChange();
            }
        }

    }



    public class AppParameter
    {
        public string Key { get; set; }
        public string Value { get; set; }
    }

    public class ManagedApplicationIdentity
    {
        public List<object> ManagedIdentities { get; set; }
    }

    public class Application
    {
        public string Name { get; set; }
        public string TypeName { get; set; }
        public string TypeVersion { get; set; }
        public string Status { get; set; }
        public List<AppParameter> Parameters { get; set; }
        public string HealthState { get; set; }
        public string ApplicationDefinitionKind { get; set; }
        public ManagedApplicationIdentity ManagedApplicationIdentity { get; set; }
        public string Id { get; set; }
    }

    public class Service
    {
        public string ServiceKind { get; set; }
        public string Name { get; set; }
        public string TypeName { get; set; }
        public string ManifestVersion { get; set; }
        public string HealthState { get; set; }
        public string ServiceStatus { get; set; }
        public bool IsServiceGroup { get; set; }
        public string Id { get; set; }
    }

    public class PartitionInformation
    {
        public string ServicePartitionKind { get; set; }
        public string Id { get; set; }
        public string LowKey { get; set; }
        public string HighKey { get; set; }
    }

    public class Partition
    {
        public string ServiceKind { get; set; }
        public PartitionInformation PartitionInformation { get; set; }
        public int InstanceCount { get; set; }
        public int MinInstanceCount { get; set; }
        public int MinInstancePercentage { get; set; }
        public string HealthState { get; set; }
        public string PartitionStatus { get; set; }
    }

    public class Replica
    {
        public string ServiceKind { get; set; }
        public string InstanceId { get; set; }
        public string ReplicaStatus { get; set; }
        public string HealthState { get; set; }
        public string Address { get; set; } // is a json string {"Endpoints":{"":"http:\/\/DESKTOP-7MM2D2T:30005"}}
        public string NodeName { get; set; }
        public string LastInBuildDurationInSeconds { get; set; }
    }

    public class ReplicaAddress {
        public Dictionary<string, string> Endpoints {get;set;}
    }

    public class ServiceFabricResponse<T>
    {
        public string ContinuationToken { get; set; }
        public List<T> Items { get; set; }
    }
}