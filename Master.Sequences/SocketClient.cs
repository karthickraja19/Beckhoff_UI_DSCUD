using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Teal.Library.Log;

namespace Teal.Library.Utilities
{
    public class SocketClient
    {
        private string _ip;
        private int _port;
        private string _name;
        private TcpClient _client;
        private NetworkStream _stream; 
        public ConcurrentQueue<string> tcpQueue = new ConcurrentQueue<string>();
        public String Name { get { return _name; } }

        public SocketClient(string ip, int port, string name = "")
        {
            _ip = ip;
            _port = port;
            _name = name;
        }

        public bool IsConnected
        {
            get
            {
                try
                {
                    // Null checks first
                    if (_client == null || _client.Client == null)
                        return false;

                    // Check if TCP client reports itself as connected
                    if (!_client.Connected)
                        return false;

                    // Detect half-open connection
                    bool socketReadable = _client.Client.Poll(0, SelectMode.SelectRead);
                    bool noDataWaiting = (_client.Client.Available == 0);

                    // socketReadable AND noDataWaiting → socket closed
                    if (socketReadable && noDataWaiting)
                        return false;

                    return true;
                }
                catch
                {
                    return false;
                }
            }
        }

        public bool Ping()
        {
            try
            {
                using (Ping ping = new Ping())
                {
                    PingReply pingReply = ping.Send(_ip, 1000);
                    return pingReply.Status == IPStatus.Success;
                }
            }
            catch (Exception ex)
            {
                Logger.WriteLog(LogType.Error, "SocketClient", "Ping()", "Exception Error", ex.ToString());
                return false;
            }
        }

        public bool Connect()
        {
            try
            {
                _client = new TcpClient();
                _client.Connect(_ip, _port);
                _stream = _client.GetStream();
                Logger.WriteLog(LogType.Info, "SocketClient", "Connect()", "Connection Sucessfull", _ip + $":{_port}");
                return true;
            }
            catch (Exception ex)
            {
                Logger.WriteLog(LogType.Error, "SocketClient", "Connect()", "Exception Error", ex.ToString());
                return false;
            }
        }

        public void Disconnect()
        {
            _stream?.Close();
            _client?.Close();
            _client = null;
        }

        public bool Send(string message)
        {
            try
            {
                ClearBufferData();
                if (!IsConnected || _stream == null) return false;

                if (_stream != null) // && tcpclient != null && tcpclient.Connected)
                {
                    byte[] data = Encoding.ASCII.GetBytes(message);
                    ;
                    _stream.Write(data, 0, data.Length);

                    return true;
                }
                else
                {
                    return false;
                }
            }
            catch (Exception ex)
            {
                Logger.WriteLog(LogType.Error, "SocketClient", "SendAsync()", "Exception Error", ex.ToString());
                throw;
            }
        }

        public string Receive(int bufferSize = 1024)
        {
            try
            {
                if (!IsConnected || _stream == null || !_stream.DataAvailable)
                    return null;

                byte[] buffer = new byte[bufferSize];
                int bytesRead = _stream.Read(buffer, 0, buffer.Length);

                if (bytesRead > 0)
                    return Encoding.UTF8.GetString(buffer, 0, bytesRead);
                else
                    return null;
            }
            catch (Exception ex)
            {
                Logger.WriteLog(LogType.Error, "SocketClient", "Receive()", "Exception Error", ex.ToString());
                throw;
            }
        }

        public async Task<bool> ConnectAsync()
        {
            try
            {
                _client = new TcpClient();
                await _client.ConnectAsync(_ip, _port);
                _stream = _client.GetStream();
                Logger.WriteLog(LogType.Info, "SocketClient", "ConnectAsync()", "Connection Sucessfull", _ip + $":{_port}");
                return true;
            }
            catch (Exception ex)
            {
                Logger.WriteLog(LogType.Error, "SocketClient", "ConnectAsync()", "Exception Error", ex.ToString());
                return false;
            }
        }

        public async Task<bool> SendAsync(string message)
        {
            try
            {
                ClearBufferData();
                if (!IsConnected || _stream == null) return false;
                byte[] data = Encoding.ASCII.GetBytes(message);
                await _stream.WriteAsync(data, 0, data.Length);
                Logger.WriteLog(LogType.Info, "SocketClient", "SendAsync()", "Send Data Sucessfull", _ip + $":{_port}");
                return true;
            }
            catch (Exception ex)
            {
                Logger.WriteLog(LogType.Error, "SocketClient", "SendAsync(string message)", "Exception Error", ex.ToString());
                return false;
            }
        }

        public async Task<string> ReceiveAsync(int bufferSize = 1024)
        {
            try
            {
                if (!IsConnected || _stream == null)
                {
                    return null;
                }

                byte[] buffer = new byte[bufferSize];
                StringBuilder sb = new StringBuilder();

                while (_stream.DataAvailable)
                {
                    int bytesRead = await _stream.ReadAsync(buffer, 0, bufferSize);
                    if (bytesRead == 0)
                    {
                        return null;
                    }

                    sb.Append(Encoding.ASCII.GetString(buffer, 0, bytesRead));
                }

                return sb.ToString();
            }
            catch (Exception ex)
            {
                Logger.WriteLog(LogType.Error, "SocketClient", "ReceiveAsync()", "Exception Error", ex.ToString());
                throw;
            }
        }

        public void ClearBufferData()
        {
            try
            {
                byte[] buffer = new byte[600001];
                if (_stream != null)
                {
                    while (_stream.DataAvailable)
                    {
                        _stream.Read(buffer, 0, buffer.Length);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.WriteLog(LogType.Error, "SocketClient", "ClearBufferData()", "Exception Error", ex.ToString());
                throw;
            }
        }

        public async Task<string> Trigger(string trigger_cmd, int trigger_retry_count, int timeoutMs = 10000, int pollIntervalMs = 100)
        {
            try
            {
                for (int i = 0; i < trigger_retry_count; i++)
                {
                    string response = await SendAndReceiveAsync(trigger_cmd, timeoutMs, pollIntervalMs);
                    if (!string.IsNullOrWhiteSpace(response) && response != "No Data Recieved" && response != "Timeout" && response != "Error" && response != "Failed to send command")
                    {
                        return response;
                    }
                }
            }
            catch (Exception ex)
            {
                Exception Ex = ex;
                Logger.WriteLog(LogType.Error, "SocketClient", "TCPTrigger()", "Exception Error", Ex.ToString());
                return "Alarm";
            }

            return "Alarm";
        }

        public async Task<string> SendAndReceiveAsync(string val, int timeoutMs = 4000, int pollIntervalMs = 100)
        {
            try
            {
                ClearBufferData();
                if (!Send(val))
                {
                    return "Failed to send command";
                }

                int elapsed = 0;
                Stopwatch stopwatch = new Stopwatch();
                while (stopwatch.ElapsedMilliseconds < timeoutMs)
                {
                    stopwatch.Start();
                    string response = Receive(600001);
                    if (!string.IsNullOrWhiteSpace(response) && response != "No Data Recieved")
                    {
                        return response;
                    }

                    await Task.Delay(pollIntervalMs);
                    elapsed += pollIntervalMs;
                }

                return "Timeout";
            }
            catch (Exception ex2)
            {
                Exception ex = ex2;
                Logger.WriteLog(LogType.Error, "SocketClient", "SendAndReceiveAsync()", "Exception Error", ex.ToString());
                return "Error";
            }
        }

        public async Task<string> RobotSendandReceiveAsync(string val, int retry_Count, bool Calib_mode = false, bool return_resp = false, int timeoutMs = 60500, int pollIntervalMs = 200)
        {
            try
            {
                ClearBufferData();
                if (!Send(val))
                {
                    return "Failed to send command";
                }

                int elapsed = 0;
                Stopwatch stopwatch = new Stopwatch();
                stopwatch.Start();

                while (stopwatch.ElapsedMilliseconds < timeoutMs)
                {
                    await Task.Delay(pollIntervalMs);
                    //Thread.Sleep(pollIntervalMs);
                    string response = Receive(600001);
                    if (!string.IsNullOrEmpty(response))
                    {
                        if (return_resp)
                        {
                            return response;
                        }

                        string[] parts;
                        if (Calib_mode)
                        {
                            parts = response.Split(',');
                            if (parts != null && parts.Length != 0)
                            {
                                if (parts[1] == "0" && parts[2] == "1")
                                {
                                }

                                if (parts[1] == "1" && parts[2] == "0")
                                {
                                    return response;
                                }
                            }
                        }

                        parts = SplitOutputString(response);
                        if (parts != null && parts.Length != 0)
                        {
                            if (parts[1] == "1" && parts[2] == "0")
                            {
                            }

                            if (parts[1] == "0" && parts[2] == "1")
                            {
                                if (response.Replace("END\r\n", "$").Split('$').Length != 0 && !string.IsNullOrEmpty(response.Replace("END\r\n", "$").Split('$')[1]))
                                {
                                    return response.Replace("END\r\n", "$").Split('$')[1];
                                }

                                return response;
                            }
                        }
                    }

                    elapsed += pollIntervalMs;
                }

                return "TimeOut";
            }
            catch (Exception ex)
            {
                Exception Ex = ex;
                Logger.WriteLog(LogType.Error, "", "SocketClient", Ex.ToString(), Ex.Message);
                return "Error";
            }
        }
        public string RobotSendandReceiveSync(string val, int retry_Count, bool Calib_mode = false, bool return_resp = false, int timeoutMs = 60500, int pollIntervalMs = 200)
        {
            try
            {
                ClearBufferData();
                if (!Send(val))
                {
                    return "Failed to send command";
                }

                int elapsed = 0;
                Stopwatch stopwatch = new Stopwatch();
                stopwatch.Start();

                while (stopwatch.ElapsedMilliseconds < timeoutMs)
                {
                    // await Task.Delay(pollIntervalMs);
                    Thread.Sleep(pollIntervalMs);
                    string response = Receive(600001);
                    if (!string.IsNullOrEmpty(response))
                    {
                        if (return_resp)
                        {
                            return response;
                        }

                        string[] parts;
                        if (Calib_mode)
                        {
                            parts = response.Split(',');
                            if (parts != null && parts.Length != 0)
                            {
                                if (parts[1] == "0" && parts[2] == "1")
                                {
                                }

                                if (parts[1] == "1" && parts[2] == "0")
                                {
                                    return response;
                                }
                            }
                        }

                        parts = SplitOutputString(response);
                        if (parts != null && parts.Length != 0)
                        {
                            if (parts[1] == "1" && parts[2] == "0")
                            {
                            }

                            if (parts[1] == "0" && parts[2] == "1")
                            {
                                if (response.Replace("END\r\n", "$").Split('$').Length != 0 && !string.IsNullOrEmpty(response.Replace("END\r\n", "$").Split('$')[1]))
                                {
                                    return response.Replace("END\r\n", "$").Split('$')[1];
                                }

                                return response;
                            }
                        }
                    }

                    elapsed += pollIntervalMs;
                }

                return "TimeOut";
            }
            catch (Exception ex)
            {
                Exception Ex = ex;
                Logger.WriteLog(LogType.Error, "", "SocketClient", Ex.ToString(), Ex.Message);
                return "Error";
            }
        }
        public string[] SplitOutputString(string resp)
        {
            try
            {
                if (!string.IsNullOrEmpty(resp))
                {
                    resp = resp.Replace("END\r\n", "$");
                    if (resp.Length > 0)
                    {
                        string[] array = resp.Split('$');
                        if (array.Length != 0)
                        {
                            return string.IsNullOrEmpty(array[1]) ? array[0].Split(',') : array[1].Split(',');
                        }

                        return resp.Split(',');
                    }

                    return null;
                }

                return null;
            }
            catch (Exception ex)
            {
                Logger.WriteLog(LogType.Error, "", "SocketClient", ex.ToString(), ex.Message);
                return null;
            }
        }
    }
}
