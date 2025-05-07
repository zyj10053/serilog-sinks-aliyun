using Aliyun.Api.LOG;
using Aliyun.Api.LOG.Common.Utilities;
using Aliyun.Api.LOG.Data;
using Aliyun.Api.LOG.Request;
using Newtonsoft.Json;
using Serilog.Debugging;
using Serilog.Events;
using Serilog.Sinks.Aliyun.Convert;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Text;
using System.Threading.Tasks;
using Serilog.Sinks.PeriodicBatching;
using Serilog.Core;
using Serilog.Formatting.Json;
using Serilog.Formatting;
using Serilog.Sinks.Aliyun.Helper;

namespace Serilog.Sinks.Aliyun.Batched
{

    /// <summary>
    /// The default Aliyun sink
    /// </summary>
    public sealed class BatchedAliyunSink : IBatchedLogEventSink
    {
        private readonly string _logStore;
        private readonly bool _logMessageTemplate;
        private readonly string _topic;
        private readonly ITextFormatter _textFormatter;
        private readonly LogClient _client;
        private readonly string _source;
        private readonly string _project;

        public BatchedAliyunSink(string accessKeyId,
            string accessKeySecret,
            string endpoint,
            string project,
            string logStore,
            string topic = null,
            string source = null,
            bool logMessageTemplate = true,
            int requestTimeout = 1000,
            ITextFormatter formatter = null)
        {
            _logStore = logStore;
            _logMessageTemplate = logMessageTemplate;
            _topic = topic ?? string.Empty;
            _source = source ?? string.Empty;
            _project = project ?? string.Empty;
            _textFormatter = formatter;
            _client = new LogClient(endpoint, StringHelper.DESDecrypt(accessKeyId), StringHelper.DESDecrypt(accessKeySecret));
        }

        public Task EmitBatchAsync(IEnumerable<LogEvent> batch)
        {
            PutLogsRequest putLogsReqError = new PutLogsRequest();
            putLogsReqError.Logstore = _logStore;
            putLogsReqError.Project = _project;
            putLogsReqError.Topic = _topic;
            putLogsReqError.Source = _source;
            var logs = new List<LogItem>();
            foreach (var logEvent in batch)
            {
                List<LogContent> contents =
                [
                    new("Timestamp", logEvent.Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff zzz")),
                    new("Level", logEvent.Level.ToString()),
                    new("RenderedMessage",logEvent.RenderMessage())
                ];
                if (logEvent.Exception != null)
                {
                    contents.Add(new LogContent("Exception", StringHelper.ConvertTo(logEvent.Exception.ToString())));
                }

                foreach (var prop in logEvent.Properties)
                {
                    if (!contents.Any(x => x.Key == prop.Key))
                    {
                        contents.Add(new LogContent(prop.Key, prop.Value.ToString()));
                    }
                }

                logs.Add(new LogItem()
                {
                    Contents = contents,
                    Time = DateUtils.TimeSpan()
                });
            }

            try
            {
                putLogsReqError.LogItems = logs;
                var response = _client.PutLogs(putLogsReqError);
            }
            catch (Exception ex)
            {
                SelfLog.WriteLine(ex.Message);
            }

            return Task.CompletedTask;
        }

        public Task OnEmptyBatchAsync()
        {
            return Task.CompletedTask;
        }
    }
}