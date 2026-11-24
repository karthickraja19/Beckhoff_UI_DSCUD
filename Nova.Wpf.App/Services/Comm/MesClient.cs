using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Nova.Abstractions.Services;
using Teal.Library.Contracts.Models;
using Teal.Library.Log;

namespace Nova.Core.Comm
{
    /// <summary>
    /// <see cref="IMesClient"/> over HTTP: posts the GetCmdResult body of the MES WebAPI
    /// and reads <c>{"Result":...,"RetMsg":"..."}</c> back.
    /// </summary>
    /// <remarks>
    /// The body always carries every field of the template - MES rejects a request with
    /// a field missing - and only the ones a command uses are filled in.
    /// </remarks>
    public sealed class MesClient : IMesClient, IDisposable
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

        private readonly string _url;
        private readonly HttpClient _http = new HttpClient { Timeout = Timeout };

        /// <param name="url">
        /// The GetCmdResult address, e.g. "http://10.32.16.114/api/Assy/GetCmdResult". A bare
        /// host ("http://10.32.16.114") has the path added. Blank leaves MES unconfigured.
        /// </param>
        /// <param name="stateUrl">
        /// The machine-state address; blank uses the host of <paramref name="url"/> with
        /// "/api/machine/collmachinestatefromcard".
        /// </param>
        public MesClient(string url, string terminalName, string stateUrl = null)
        {
            _url = Normalise(url);
            TerminalName = (terminalName ?? string.Empty).Trim();
            _stateUrl = StateUrl(stateUrl, _url);
        }

        private readonly string _stateUrl;

        public async Task<MesResult> PostMachineStateAsync(string json)
        {
            if (_stateUrl == null)
                return MesResult.NotReached("MES is not configured on this machine (MesUrl in App.config).");

            Log("MES state " + _stateUrl + ": " + json);

            try
            {
                using (var content = new StringContent(json ?? string.Empty, Encoding.UTF8, "application/json"))
                using (HttpResponseMessage response = await _http.PostAsync(_stateUrl, content).ConfigureAwait(false))
                {
                    string answer = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    Log("MES state answer (" + (int)response.StatusCode + "): " + answer);

                    if (!response.IsSuccessStatusCode)
                        return MesResult.NotReached("MES answered HTTP " + (int)response.StatusCode + ".");

                    return Parse(answer);
                }
            }
            catch (Exception ex)
            {
                Log("MES state report failed: " + ex.Message);
                return MesResult.NotReached("MES could not be reached: " + ex.Message);
            }
        }

        private static string StateUrl(string configured, string cmdUrl)
        {
            if (!string.IsNullOrWhiteSpace(configured))
                return configured.Trim();
            if (cmdUrl == null)
                return null;

            Uri uri;
            return Uri.TryCreate(cmdUrl, UriKind.Absolute, out uri)
                ? uri.GetLeftPart(UriPartial.Authority) + "/api/machine/collmachinestatefromcard"
                : null;
        }

        public bool IsConfigured
        {
            get { return _url != null; }
        }

        public string TerminalName { get; }

        public async Task<MesResult> GetCmdResultAsync(string command, string serialNumber, string testData = "", string results = "", string empNo = "", string terminalName = null)
        {
            if (_url == null)
                return MesResult.NotReached("MES is not configured on this machine (MesUrl in App.config).");

            var body = new JObject
            {
                ["empNo"] = empNo ?? "",
                ["terminalName"] = string.IsNullOrWhiteSpace(terminalName) ? TerminalName : terminalName.Trim(),
                ["serial_Number"] = serialNumber ?? "",
                ["machine"] = "",
                ["toolingNo"] = "",
                ["lotNo"] = "",
                ["kpsn"] = "",
                ["workOrder"] = "",
                ["cavity"] = "",
                ["testData"] = testData ?? "",
                ["results"] = results ?? "",
                ["status"] = "",
                ["pCmd"] = command ?? "",
                ["defectCode"] = "",
            };

            string json = body.ToString(Formatting.None);
            Log("MES request " + _url + ": " + json);

            try
            {
                using (var content = new StringContent(json, Encoding.UTF8, "application/json"))
                using (HttpResponseMessage response = await _http.PostAsync(_url, content).ConfigureAwait(false))
                {
                    string answer = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    Log("MES answer (" + (int)response.StatusCode + "): " + answer);

                    if (!response.IsSuccessStatusCode)
                        return MesResult.NotReached("MES answered HTTP " + (int)response.StatusCode + ".");

                    return Parse(answer);
                }
            }
            catch (Exception ex)
            {
                Log("MES request failed: " + ex.Message);
                return MesResult.NotReached("MES could not be reached: " + ex.Message);
            }
        }

        public void Dispose()
        {
            _http.Dispose();
        }

        private static MesResult Parse(string answer)
        {
            try
            {
                JObject reply = JObject.Parse(answer ?? string.Empty);
                JToken result = reply["Result"];
                JToken message = reply["RetMsg"];

                if (result == null || result.Type != JTokenType.Boolean)
                    return MesResult.NotReached("MES answered without a Result: " + answer);

                return new MesResult
                {
                    Ok = result.Value<bool>(),
                    Message = message != null ? message.ToString() : string.Empty,
                    Reached = true,
                };
            }
            catch (JsonException)
            {
                return MesResult.NotReached("MES answered something that is not JSON: " + answer);
            }
        }

        private static string Normalise(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return null;

            string trimmed = url.Trim().TrimEnd('/');
            return trimmed.IndexOf("/api/", StringComparison.OrdinalIgnoreCase) >= 0
                ? trimmed
                : trimmed + "/api/Assy/GetCmdResult";
        }

        /// <summary>Slide 96: the machine log records every interaction with MES.</summary>
        private static void Log(string text)
        {
            try
            {
                Logger.WriteLog(LogType.Info, "MES", "GetCmdResult", text, string.Empty);
            }
            catch (Exception)
            {
                // Logging must never stop the request.
            }
        }
    }
}
