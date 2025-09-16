using Microsoft.ServiceFabric.Services.Communication.Runtime;
using System;
using System.Collections.Generic;
using System.Fabric;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace UserService.Infrastructure
{
    /// <summary>
    /// A simple implementation of ICommunicationListener using HttpListener.
    /// </summary>
    public class HttpListenerCommunicationListener : ICommunicationListener
    {
        private readonly ServiceContext _serviceContext;
        private readonly string _endpointName;
        private readonly Func<HttpListenerContext, CancellationToken, Task> _requestHandler;

        private HttpListener? _listener;
        private string? _listeningAddress;
        private CancellationTokenSource? _cts;

        public HttpListenerCommunicationListener(
            ServiceContext serviceContext,
            string endpointName,
            Func<HttpListenerContext, CancellationToken, Task> requestHandler)
        {
            _serviceContext = serviceContext;
            _endpointName = endpointName;
            _requestHandler = requestHandler;
        }

        /// <summary>
        /// Called by Service Fabric to open the listener.
        /// </summary>
        public Task<string> OpenAsync(CancellationToken cancellationToken)
        {
            var ep = _serviceContext.CodePackageActivationContext.GetEndpoint(_endpointName);

            // Listen on all IPs for the given port
            _listeningAddress = $"http://+:{ep.Port}/";

            _listener = new HttpListener();
            _listener.Prefixes.Add(_listeningAddress);
            _listener.Start();

            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            // Start loop
            _ = Task.Run(() => ListenLoopAsync(_cts.Token));

            // Replace '+' with actual node IP/FQDN
            var publishAddress = _listeningAddress.Replace("+", FabricRuntime.GetNodeContext().IPAddressOrFQDN);
            return Task.FromResult(publishAddress);
        }

        /// <summary>
        /// Called by Service Fabric when the service is closing.
        /// </summary>
        public Task CloseAsync(CancellationToken cancellationToken)
        {
            StopListener();
            return Task.CompletedTask;
        }

        /// <summary>
        /// Called by Service Fabric if the service is aborted.
        /// </summary>
        public void Abort() => StopListener();

        private async Task ListenLoopAsync(CancellationToken ct)
        {
            if (_listener == null) return;

            try
            {
                while (!ct.IsCancellationRequested)
                {
                    var ctx = await _listener.GetContextAsync().ConfigureAwait(false);

                    _ = Task.Run(() => _requestHandler(ctx, ct));
                }
            }
            catch (HttpListenerException) { /* listener closed */ }
            catch (ObjectDisposedException) { /* ignore */ }
        }

        private void StopListener()
        {
            try
            {
                _cts?.Cancel();
                _listener?.Close();
                _listener = null;
            }
            catch { /* swallow */ }
        }
    }
}