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

        private readonly int _port;
        private readonly string _host;
        private readonly Logger _logger;

        public TcpSocketSession(string host, int port, Logger logger): base(host, port)
        {
            _host = host;
            _port = port;
            _logger = logger;
            _encoding = Encoding.UTF8;
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
                    throw new InvalidOperationException("TCP client could not connect");
                await _connectEvent.WaitAsync(cts.Token);
                return !cts.IsCancellationRequested;
            }
            catch (OperationCanceledException)
            {
                _logger.Error("[Socket] TCP client connect timeout");
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
                    throw new InvalidOperationException("TCP client could not disconnect");
                await _disconnectEvent.WaitAsync(cts.Token);
                return !cts.IsCancellationRequested;
            }
            catch (OperationCanceledException)
            {
                _logger.Error("[Socket] TCP client disconnect timeout");
                return false;
            }
        }

        public async Task<bool> Send(string data, int timeout)
        {
            var cts = new CancellationTokenSource(timeout);
            try
            {
                if (!SendAsync(data))
                    throw new InvalidOperationException("TCP client could not send data");
                await _sendEvent.WaitAsync(cts.Token);
                return !cts.IsCancellationRequested;
            }
            catch (OperationCanceledException)
            {
                _logger.Error("[Socket] TCP client send timeout");
                return false;
            }
        }

        public async Task<string> Request(string data, int timeout)
        {
            var cts = new CancellationTokenSource(timeout);
            try
            {
                if (!SendAsync(data))
                    throw new InvalidOperationException("TCP client could not request execute");
                await _receivedEvent.WaitAsync(cts.Token);
                return !cts.IsCancellationRequested ? _result : null;
            }
            catch (OperationCanceledException)
            {
                _logger.Error("[Socket] TCP client request timeout");
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
                    throw new InvalidOperationException("TCP client could not request execute byte array");
                await _receivedEvent.WaitAsync(cts.Token);
                return !cts.IsCancellationRequested ? _result : null;
            }
            catch (OperationCanceledException)
            {
                _logger.Error("[Socket] TCP client request byte array timeout");
                return null;
            }
        }

        protected override void OnConnected()
        {
            _stop = false;
            _connectEvent.Set();
            _logger?.Debug($"[Socket] TCP client {_host}:{_port} connected a new session with Id {Id}");
        }

        protected override void OnDisconnected()
        {
            _disconnectEvent.Set();
            _logger?.Debug($"[Socket] TCP client {_host}:{_port} disconnected a session with Id {Id}");
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
            _logger?.Debug($"[Socket] TCP client {_host}:{_port} sent {sent} byte and pending {pending} byte");
        }

        protected override void OnReceived(byte[] buffer, long offset, long size)
        {
            _result = _encoding.GetString(buffer, (int) offset, (int) size);
            _receivedEvent.Set();
            _logger?.Debug($"[Socket] TCP client {_host}:{_port} receive {size} byte");
        }

        protected override void OnError(SocketError error) =>
            _logger?.Error($"[Socket] TCP client {_host}:{_port} caught an error with code {error}");
    }
}