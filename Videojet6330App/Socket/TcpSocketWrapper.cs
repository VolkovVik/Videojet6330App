using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Polly;
using Serilog;
using Serilog.Core;

namespace Videojet6330App.Socket
{
    class TcpSocketWrapper
    {
        private readonly int _port;
        private readonly string _host;
        private readonly Logger _logger;
        private readonly TcpSocketClient _client;

        private int _countRetry;
        private const int ReconnectIntervalSec = 3;

        public event EventHandler<bool> OnConnectErrorStatusChanged;

        private readonly AutoResetEvent _waitHandler = new AutoResetEvent(true);

        public TcpSocketWrapper(string host, int port)
        {
            _logger = new LoggerConfiguration()
                     .MinimumLevel.Debug()
                     .WriteTo.Console()
                     .WriteTo.File($"C:\\ASPU\\myapp\\printer_{host}_{port}_.txt", rollingInterval:RollingInterval.Day)
                     .CreateLogger();

            _host = host;
            _port = port;
            _client = new TcpSocketClient(_host, _port, _logger);
        }

        public void Disconnect()
        {
            _client?.Disconnect();
            _logger?.Dispose();
        }

        public bool IsConnect => _client?.IsConnect ?? false;

        public async Task Connect(int retryCount, CancellationToken cancellationToken = default)
        {
            try
            {
                _waitHandler.WaitOne();
                _countRetry = 0;

                var exceptionPolicy = Policy.Handle<Exception>()
                                            .WaitAndRetryAsync(retryCount,
                                                 _ => TimeSpan.FromSeconds(ReconnectIntervalSec),
                                                 (exc, _) => OnRetry(exc));

                var falseResultPolicy = Policy.HandleResult(false)
                                              .WaitAndRetryAsync(retryCount,
                                                   _ => TimeSpan.FromSeconds(ReconnectIntervalSec),
                                                   (result, _) => OnRetry());

                var policy = exceptionPolicy.WrapAsync(falseResultPolicy);
                await policy.ExecuteAsync(token => _client.Connect(), cancellationToken);
            }
            finally
            {
                _waitHandler.Set();
            }
        }

        public async Task<string> Request(string payload, int retryCount, CancellationToken cancellationToken = default)
        {
            try
            {
                _waitHandler.WaitOne();
                _countRetry = 0;
                var policy = Policy.Handle<Exception>()
                                   .WaitAndRetryAsync(retryCount,
                                        _ => TimeSpan.FromSeconds(ReconnectIntervalSec),
                                        (exc, _) => OnRetry(exc));
                return await policy.ExecuteAsync(token => _client.Request(payload),
                    cancellationToken);
            }
            finally
            {
                _waitHandler.Set();
            }
        }

        public async Task<string> Request(byte[] payload, int retryCount, Encoding encoding, CancellationToken cancellationToken = default)
        {
            try
            {
                _waitHandler.WaitOne();
                _countRetry = 0;
                var policy = Policy.Handle<Exception>()
                                   .WaitAndRetryAsync(retryCount,
                                        _ => TimeSpan.FromSeconds(ReconnectIntervalSec),
                                        (exc, _) => OnRetry(exc));
                return await policy.ExecuteAsync(token => _client.Request(payload, encoding),
                    cancellationToken);
            }
            finally
            {
                _waitHandler.Set();
            }
        }

        public async Task Send(string payload, CancellationToken cancellationToken = default)
        {
            try
            {
                _waitHandler.WaitOne();
                _countRetry = 0;
                var policy = Policy.Handle<Exception>()
                                   .WaitAndRetryForeverAsync(
                                        _ => TimeSpan.FromSeconds(ReconnectIntervalSec),
                                        (exc, _) => OnRetry(exc));
                await policy.ExecuteAsync(token => _client.Send(payload), cancellationToken);
            }
            finally
            {
                _waitHandler.Set();
            }
        }

        public async Task Send(string payload, int retryCount, CancellationToken cancellationToken = default)
        {
            try
            {
                _waitHandler.WaitOne();
                _countRetry = 0;
                var policy = Policy.Handle<Exception>()
                                   .WaitAndRetryAsync(retryCount,
                                        _ => TimeSpan.FromSeconds(ReconnectIntervalSec),
                                        (exc, _) => OnRetry(exc));
                await policy.ExecuteAsync(token => _client.Send(payload), cancellationToken);
            }
            finally
            {
                _waitHandler.Set();
            }
        }

        private void OnRetry(Exception exc)
        {
            _logger?.Error(exc, "[Socket] Operation {host}:{port} failed. Retry count {countRetry}. Try retry request",
                _host, _port, _countRetry++);
            OnConnectErrorStatusChanged?.Invoke(this, true);
        }

        private void OnRetry()
        {
            _logger?.Error("[Socket] Operation {host}:{port} failed. Retry count {countRetry}. Try retry request",
                _host, _port, _countRetry++);
            OnConnectErrorStatusChanged?.Invoke(this, true);
        }
    }
}