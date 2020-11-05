using System.Collections.Generic;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;
using Microsoft.ReverseProxy.Abstractions;
using Microsoft.ReverseProxy.Configuration;
using Microsoft.ReverseProxy.Service;

namespace Microsoft.Extensions.DependencyInjection
{
    public static class InMemoryConfigProviderExtensions
    {
        public static IReverseProxyBuilder LoadFromStaticConfig(this IReverseProxyBuilder builder)
        {
            builder.Services.AddSingleton<IProxyConfigProvider>(new CustomConfigProvider());
            return builder;
        }
    }
}
namespace Microsoft.ReverseProxy.Configuration
{
    class StaticConfig : IProxyConfig
    {
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        internal void SignalChange()
        {
            _cts.Cancel();
        }
        public StaticConfig()
        {
            ChangeToken = new CancellationChangeToken(_cts.Token);
            var cluster1 = new Cluster()
            {

                Id = "cluster1"
            };
            cluster1.Destinations.Add("cluster1/destination1", new Destination()
            {
                Address = @"http://127.0.0.1:4000"
            });
            var cluster2 = new Cluster()
            {

                Id = "cluster2"
            };
            cluster2.Destinations.Add("cluster2/destination1", new Destination()
            {
                Address = @"http://127.0.0.1:3000"
            });
            _clusters = new List<Cluster>();
            _clusters.Add(cluster1);
            _clusters.Add(cluster2);


            var route1 = new ProxyRoute()
            {
                RouteId = "route1",
                ClusterId = "cluster1"
            };
            route1.Transforms = new List<IDictionary<string, string>>();
            route1.Transforms.Add(new Dictionary<string, string>(){
                { "PathRemovePrefix", "/app"}
            });
            route1.Match.Path = "/app";
            var route2 = new ProxyRoute()
            {
                RouteId = "route2",
                ClusterId = "cluster2"
            };
            route2.Transforms = new List<IDictionary<string, string>>();
            route2.Transforms.Add(new Dictionary<string, string>(){
                { "PathRemovePrefix", "/status"}
            });
            route2.Match.Path = "/status";
            _routes = new List<ProxyRoute>();
            _routes.Add(route1);
            _routes.Add(route2);
        }
        private List<ProxyRoute> _routes;
        public IReadOnlyList<ProxyRoute> Routes => _routes;

        private List<Cluster> _clusters;
        public IReadOnlyList<Cluster> Clusters => _clusters;
        public IChangeToken ChangeToken { get; }
    }
    public class CustomConfigProvider : IProxyConfigProvider
    {
        public CustomConfigProvider()
        {
            _config = new StaticConfig();

        }
        private StaticConfig _config;
        public IProxyConfig GetConfig()
        {
            return _config;
            // Go to SF and get configuration data from the REST API and convert to IProxyConfig
            // throw new System.NotImplementedException();
        }
    }
}