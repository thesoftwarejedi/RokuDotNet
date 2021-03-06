using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace RokuDotNet.Client
{
    public sealed class RokuDeviceDiscoveryClient : IRokuDeviceDiscoveryClient
    {
        #region IRokuDeviceDiscoveryClient Members

        public event EventHandler<DeviceDiscoveredEventArgs> DeviceDiscovered;

        public void DiscoverDevicesAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            DiscoverDevicesAsync(null, cancellationToken);
        }

        public void DiscoverDevicesAsync(Func<DiscoveredDeviceContext, Task<bool>> onDeviceDiscovered, CancellationToken cancellationToken = default(CancellationToken))
        {
            //all interfaces which are up, then every IPv4 address for each interface
            var ips= NetworkInterface.GetAllNetworkInterfaces().Where(a => a.OperationalStatus == OperationalStatus.Up).SelectMany(a=>a.GetIPProperties().UnicastAddresses.Where(b => b.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)).Select(a => a.Address);

            foreach (var ip in ips)
            {
                Task.Run(() => Discover(ip, cancellationToken, onDeviceDiscovered));
            }

        }

        #endregion

        private async void Discover(IPAddress ip, CancellationToken cancellationToken, Func<DiscoveredDeviceContext, Task<bool>> onDeviceDiscovered)
        {
            string discoverRequest =
"M-SEARCH * HTTP/1.1\n" +
"Host: 239.255.255.250:1900\n" +
"Man: \"ssdp:discover\"\n" +
"ST: roku:ecp\n" +
"";
            var bytes = Encoding.UTF8.GetBytes(discoverRequest);

            using (var udpClient = new UdpClient())
            {
                udpClient.Client.Bind(new IPEndPoint(ip, 0));

                await udpClient.SendAsync(bytes, bytes.Length, "239.255.255.250", 1900).ConfigureAwait(false);

                while (!cancellationToken.IsCancellationRequested)
                {
                    var receiveTask = Task.Run(
                        async () =>
                        {
                            try
                            {
                                return await udpClient.ReceiveAsync().ConfigureAwait(false);
                            }
                            catch (ObjectDisposedException)
                            {
                                    // NOTE: We assume that a disposal exception is an attempt to cancel an
                                    //       outstanding ReceiveAsync() by closing the socket (disposing the
                                    //       UdpClient).

                                    throw new OperationCanceledException();
                            }
                        });

                    var cancellationTask = Task.Delay(TimeSpan.FromMilliseconds(-1), cancellationToken);

                    var completedTask = await Task.WhenAny(receiveTask, cancellationTask).ConfigureAwait(false);

                    if (completedTask == cancellationTask)
                    {
                        // NOTE: We allow the OperationCanceledException to bubble up, causing disposal of the
                        //       UdpClient which would force any pending ReceiveAsync() to throw an 
                        //       ObjectDisposedException.

                        await cancellationTask.ConfigureAwait(false);

                        return;
                    }

                    var rawResponse = await receiveTask.ConfigureAwait(false);
                    var response = await ParseResponseAsync(rawResponse.Buffer).ConfigureAwait(false);

                    if (response.StatusCode == 200
                        && response.Headers.TryGetValue("ST", out string stHeader)
                        && stHeader == "roku:ecp"
                        && response.Headers.TryGetValue("LOCATION", out string location)
                        && Uri.TryCreate(location, UriKind.Absolute, out Uri locationUri)
                        && response.Headers.TryGetValue("USN", out string serialNumber))
                    {
                        var device = new RokuDevice(locationUri, serialNumber);

                        bool cancelDiscovery = false;

                        if (onDeviceDiscovered != null)
                        {
                            cancelDiscovery = await onDeviceDiscovered(new DiscoveredDeviceContext(device, locationUri, serialNumber)).ConfigureAwait(false);
                        }

                        var args = new DeviceDiscoveredEventArgs(device, locationUri, serialNumber)
                        {
                            CancelDiscovery = cancelDiscovery
                        };

                        this.DeviceDiscovered?.Invoke(this, args);

                        cancelDiscovery = args.CancelDiscovery;

                        if (cancelDiscovery)
                        {
                            return;
                        }
                    }
                }
            }
        }

        private async Task<HttpResponse> ParseResponseAsync(byte[] response)
        {
            using (var stream = new MemoryStream(response))
            using (var reader = new StreamReader(stream))
            {
                string statusLine = await reader.ReadLineAsync().ConfigureAwait(false);
                string[] splitStatusLine = statusLine.Split(new[] { ' ' }, 3, StringSplitOptions.RemoveEmptyEntries);
                
                string httpVersion = splitStatusLine[0];
                int statusCode = Int32.Parse(splitStatusLine[1]);
                string statusMessage = splitStatusLine[2];

                var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                while (!reader.EndOfStream)
                {
                    string line = await reader.ReadLineAsync().ConfigureAwait(false);
                    var colonIndex = line.IndexOf(':');

                    if (colonIndex >= 0)
                    {
                        string header = line.Substring(0, colonIndex).Trim();
                        string value = line.Substring(colonIndex + 1).Trim();

                        headers[header] = value;
                    }
                }

                return new HttpResponse
                {
                    HttpVersion = httpVersion,
                    StatusCode = statusCode,
                    StatusMessage = statusMessage,
                    Headers = headers
                };
            }
        }

        private sealed class HttpResponse
        {
            public string HttpVersion { get; set; }

            public int StatusCode { get; set; }

            public string StatusMessage { get; set; }

            public IDictionary<string, string> Headers { get; set; }
        }
    }
}