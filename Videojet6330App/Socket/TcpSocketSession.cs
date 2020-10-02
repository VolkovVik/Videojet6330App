using System;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Nito.AsyncEx;
using Serilog.Core;
using TcpClient = NetCoreServer.TcpClient;

namespace Videojet6330App.Socket
{
    internal class TcpSocketSession: TcpClient
    {
        private bool _stop;
        private string _result;
        private Encoding _encoding;
        private readonly Logger _logger;
        private readonly string _description;

        public TcpSocketSession(string host, int port, Logger logger): base(host, port)
        {
            _encoding = Encoding.UTF8;
            _logger = logger;
            _description = $"[Socket] TCP client {host}:{port}";
        }

        private readonly AsyncAutoResetEvent _connectEvent = new AsyncAutoResetEvent(false);
        private readonly AsyncAutoResetEvent _sendEvent = new AsyncAutoResetEvent(false);
        private readonly AsyncAutoResetEvent _receivedEvent = new AsyncAutoResetEvent(false);
        private readonly AsyncAutoResetEvent _disconnectEvent = new AsyncAutoResetEvent(false);

        public async Task<bool> Connect(int timeout)
        {
            var cts = new CancellationTokenSource(timeout);
            try
            {
                _stop = true;
                if (!ConnectAsync())
                    throw new InvalidOperationException($"{_description} could not connect");
                await _connectEvent.WaitAsync(cts.Token);
                return !cts.IsCancellationRequested;
            }
            catch (OperationCanceledException)
            {
               _logger.Error($"{_description} connect timeout");
                return false;
            }
        }

        public async Task<bool> Disconnect(int timeout)
        {
            var cts = new CancellationTokenSource(timeout);
            try
            {
                _stop = true;
                if (!DisconnectAsync())
                    throw new InvalidOperationException($"{_description} could not disconnect");
                await _disconnectEvent.WaitAsync(cts.Token);
                return !cts.IsCancellationRequested;
            }
            catch (OperationCanceledException)
            {
                _logger.Error($"{_description} disconnect timeout");
                return false;
            }
        }

        public async Task<bool> Send(string data, int timeout)
        {
            var cts = new CancellationTokenSource(timeout);
            try
            {
                if (!SendAsync(data))
                    throw new InvalidOperationException($"{_description} could not send data");
                await _sendEvent.WaitAsync(cts.Token);
                return !cts.IsCancellationRequested;
            }
            catch (OperationCanceledException)
            {
                _logger.Error($"{_description} send data timeout");
                return false;
            }
        }

        public async Task<string> Request(string data, int timeout)
        {
            var cts = new CancellationTokenSource(timeout);
            try
            {
                if (!SendAsync(data))
                    throw new InvalidOperationException($"{_description} could not request execute");
                await _receivedEvent.WaitAsync(cts.Token);
                return !cts.IsCancellationRequested ? _result : null;
            }
            catch (OperationCanceledException)
            {
                _logger.Error($"{_description} request timeout");
                return null;
            }
        }

        public async Task<string> Request(byte[] data, int timeout, Encoding encoding)
        {
            _encoding = encoding;
            var cts = new CancellationTokenSource(timeout);
            try
            {
                if (!SendAsync(data))
                    throw new InvalidOperationException($"{_description} could not request execute byte array");
                await _receivedEvent.WaitAsync(cts.Token);
                return !cts.IsCancellationRequested ? _result : null;
            }
            catch (OperationCanceledException)
            {
                _logger.Error($"{_description} request byte array timeout");
                return null;
            }
        }

        protected override void OnConnected()
        {
            _stop = false;
            _connectEvent.Set();
            _logger?.Debug($"{_description} connected a new session with Id {Id}");
        }

        protected override void OnDisconnected()
        {
            _disconnectEvent.Set();
            _logger?.Debug($"{_description} disconnected a session with Id {Id}");
            if (_stop)
                return;
            // Wait for a while...
            Thread.Sleep(1000);
            // Try to connect again
            ConnectAsync();
        }

        protected override void OnSent(long sent, long pending)
        {
            if (pending == 0)
                _sendEvent.Set();
            _logger?.Debug($"{_description} sent {sent} byte and pending {pending} byte");
        }

        protected override void OnReceived(byte[] buffer, long offset, long size)
        {
            _result = _encoding.GetString(buffer, (int) offset, (int) size);
            _receivedEvent.Set();
            _logger?.Debug($"{_description} receive {size} byte");
        }

        protected override void OnError(SocketError error) =>
            _logger?.Error($"{_description} caught an error with code {error}");
    }
}