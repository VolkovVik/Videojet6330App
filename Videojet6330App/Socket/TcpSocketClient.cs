using System;
using System.Text;
using System.Threading.Tasks;
using Serilog.Core;

namespace Videojet6330App.Socket
{
    internal class TcpSocketClient
    {
        private readonly int _port;
        private readonly string _host;
        private readonly Logger _logger;
        private readonly string _description;

        private TcpSocketSession _client;

        public bool IsConnect => _client?.IsConnected ?? false;

        public TcpSocketClient(string host, int port, Logger logger)
        {
            _host = host;
            _port = port;
            _logger = logger;
            _description = $"[Socket] TCP client {_host}:{_port}";
        }

        public async Task<bool> Connect()
        {
            _logger?.Debug($"{_description} create");
            // Create a new TCP chat client
            _client = new TcpSocketSession(_host, _port, _logger);
            // Connect the client
            _logger?.Debug($"{_description} connecting");
            var result = await _client.Connect(5000);
            _logger?.Debug(result
                ? $"{_description} connected"
                : $"{_description} not connected");
            return result;
        }

        public async Task Disconnect()
        {
            // Disconnect the client
            _logger?.Debug($"{_description} disconnecting");
            await _client.Disconnect(5000);
            _logger?.Debug($"{_description} disconnect");
            _logger?.Dispose();
        }

        public async Task<string> Request(string payload)
        {
            _logger?.Debug($"{_description} send {payload.Replace("\r","<CR>").TrimEnd('\r', '\n')}");

            CheckConnect();
            var result = await _client.Request(payload, 20000);
            _logger?.Debug(string.IsNullOrWhiteSpace(result)
                ? $"{_description} not receive data"
                : $"{_description} receive {result.TrimEnd('\r', '\n')}");
            return !string.IsNullOrWhiteSpace(result)
                ? result
                : throw new InvalidOperationException( $"{_description} not receive data");
        }

        public async Task<string> Request(byte[] payload, Encoding encoding)
        {
            _logger?.Debug(
                $"{_description} send {encoding.GetString(payload).Replace("\r", "<CR>").TrimEnd('\r', '\n')}");

            CheckConnect();
            var result = await _client.Request(payload, 20000, encoding);
            _logger?.Debug(string.IsNullOrWhiteSpace(result)
                ? $"{_description} not receive data byte"
                : $"{_description} receive {result.TrimEnd('\r', '\n')}");
            return !string.IsNullOrWhiteSpace(result)
                ? result
                : throw new InvalidOperationException( $"{_description} not receive data byte");
        }

        public async Task Send(string payload)
        {
            _logger?.Debug($"{_description} send {payload.Replace("\r", "<CR>").TrimEnd('\r', '\n')}");

            CheckConnect();
            await _client.Send(payload, 20000);
        }

        private void CheckConnect()
        {
            if (_client.IsConnected) return;

            _logger?.Error($"{_description} not connected");
            throw new InvalidOperationException($"{_description} not connected");
        }
    }
}