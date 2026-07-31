using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using Utils;

namespace Phinix.LegacyRedPacketExtension.Client
{
    /// <summary>
    /// 红包 HTTP 中继传输（老方案保留，与老 submod 客户端同一总线/房间/API key）。
    ///
    /// 设计哲学 §3.6：发送/接收队列有界，超出丢最旧并记 Warning。
    /// 设计哲学 §3.8：所有日志经 hostContext.Log 分级上报；API key 等敏感信息不进日志。
    /// 设计哲学 §3.5：单次请求失败退避重试，不中断。
    /// </summary>
    internal sealed class RedPacketRelay
    {
        // 生产环境请改为你自己的域名并启用 HTTPS（与老客户端共用同一中继才能互通）。
        private const string RelayBaseUrl = "http://39.96.216.77/rp";
        private const string RelayApiKey = "ce699fa04e44eca445a9ea809ee765c88b87f8d2665f4d14c3f7c180afab467f";
        private const string RelayRoom = "phinix-global";

        private const int MaxOutgoingQueue = 512;
        private const int MaxIncomingQueue = 512;

        private static readonly object OutgoingLock = new object();
        private static readonly Queue<OutgoingProtocol> OutgoingQueue = new Queue<OutgoingProtocol>();
        private static readonly object IncomingLock = new object();
        private static readonly Queue<string> IncomingQueue = new Queue<string>();
        private static readonly object StateLock = new object();

        private static long lastSeenId;
        private static DateTime nextPollUtc = DateTime.MinValue;
        private static DateTime nextSendUtc = DateTime.MinValue;
        private static int pollInFlight;
        private static int sendInFlight;

        private const int PollIntervalMs = 700;
        private const int SendIntervalMs = 80;
        private const int RequestTimeoutMs = 5000;
        private const int FetchLimit = 200;
        private const int InitialHistoryMinutes = 20;
        private static readonly DateTime UnixEpochUtc = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        private readonly Action<string, LogLevel> log;

        public RedPacketRelay(Action<string, LogLevel> log)
        {
            this.log = log;
        }

        public void Clear()
        {
            lock (OutgoingLock)
            {
                OutgoingQueue.Clear();
            }

            lock (IncomingLock)
            {
                IncomingQueue.Clear();
            }

            lock (StateLock)
            {
                lastSeenId = 0;
                nextPollUtc = DateTime.MinValue;
                nextSendUtc = DateTime.MinValue;
            }

            Interlocked.Exchange(ref pollInFlight, 0);
            Interlocked.Exchange(ref sendInFlight, 0);
        }

        public void EnqueueProtocol(string protocolMessage, string senderUuid)
        {
            if (string.IsNullOrEmpty(protocolMessage)) return;

            lock (OutgoingLock)
            {
                if (OutgoingQueue.Count >= MaxOutgoingQueue)
                {
                    OutgoingQueue.Dequeue();
                    log?.Invoke("[RedPacket] Relay outgoing queue overflow, dropped oldest message.", LogLevel.WARNING);
                }

                OutgoingQueue.Enqueue(new OutgoingProtocol
                {
                    Message = protocolMessage,
                    SenderUuid = string.IsNullOrEmpty(senderUuid) ? "unknown" : senderUuid,
                    EventId = Guid.NewGuid().ToString("N")
                });
            }
        }

        public bool TryDequeueIncoming(out string protocolMessage)
        {
            lock (IncomingLock)
            {
                if (IncomingQueue.Count > 0)
                {
                    protocolMessage = IncomingQueue.Dequeue();
                    return true;
                }
            }

            protocolMessage = null;
            return false;
        }

        public void Update()
        {
            DateTime now = DateTime.UtcNow;

            bool shouldSend = false;
            lock (StateLock)
            {
                if (now >= nextSendUtc)
                {
                    shouldSend = true;
                    nextSendUtc = now.AddMilliseconds(SendIntervalMs);
                }
            }

            if (shouldSend && HasOutgoing())
            {
                if (Interlocked.CompareExchange(ref sendInFlight, 1, 0) == 0)
                {
                    ThreadPool.QueueUserWorkItem(_ => SendOnceWorker());
                }
            }

            bool shouldPoll = false;
            lock (StateLock)
            {
                if (now >= nextPollUtc)
                {
                    shouldPoll = true;
                    nextPollUtc = now.AddMilliseconds(PollIntervalMs);
                }
            }

            if (shouldPoll && Interlocked.CompareExchange(ref pollInFlight, 1, 0) == 0)
            {
                ThreadPool.QueueUserWorkItem(_ => PollWorker());
            }
        }

        private bool HasOutgoing()
        {
            lock (OutgoingLock)
            {
                return OutgoingQueue.Count > 0;
            }
        }

        private void SendOnceWorker()
        {
            OutgoingProtocol outbound = null;
            try
            {
                lock (OutgoingLock)
                {
                    if (OutgoingQueue.Count > 0)
                    {
                        outbound = OutgoingQueue.Dequeue();
                    }
                }

                if (outbound == null) return;

                string url = RelayBaseUrl.TrimEnd('/') + "/v1/raw";
                byte[] bodyBytes = Encoding.UTF8.GetBytes(outbound.Message);

                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
                request.Method = "POST";
                request.Timeout = RequestTimeoutMs;
                request.ReadWriteTimeout = RequestTimeoutMs;
                request.Proxy = null;
                request.ContentType = "text/plain; charset=utf-8";
                request.ContentLength = bodyBytes.Length;
                request.Headers["X-Api-Key"] = RelayApiKey;
                request.Headers["X-Room"] = RelayRoom;
                request.Headers["X-Sender"] = outbound.SenderUuid;
                request.Headers["X-Event-Id"] = outbound.EventId;

                using (Stream requestStream = request.GetRequestStream())
                {
                    requestStream.Write(bodyBytes, 0, bodyBytes.Length);
                }

                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                using (StreamReader reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
                {
                    reader.ReadToEnd();
                }

                log?.Invoke("[RedPacket] Relay send ok (event " + outbound.EventId + ").", LogLevel.DEBUG);
            }
            catch (Exception ex)
            {
                log?.Invoke("[RedPacket] Relay send failed, will retry: " + ex, LogLevel.WARNING);
                if (outbound != null)
                {
                    lock (OutgoingLock)
                    {
                        OutgoingQueue.Enqueue(outbound);
                    }
                }

                lock (StateLock)
                {
                    nextSendUtc = DateTime.UtcNow.AddSeconds(1);
                }
            }
            finally
            {
                Interlocked.Exchange(ref sendInFlight, 0);
            }
        }

        private void PollWorker()
        {
            try
            {
                long afterId;
                lock (StateLock)
                {
                    afterId = lastSeenId;
                }

                long sinceMs = 0;
                if (afterId <= 0)
                {
                    DateTime cutoffUtc = DateTime.UtcNow.AddMinutes(-InitialHistoryMinutes);
                    sinceMs = (long)(cutoffUtc - UnixEpochUtc).TotalMilliseconds;
                    if (sinceMs < 0) sinceMs = 0;
                }

                string url = string.Concat(
                    RelayBaseUrl.TrimEnd('/'),
                    "/v1/raw?room=",
                    Uri.EscapeDataString(RelayRoom),
                    "&after_id=",
                    afterId.ToString(),
                    "&limit=",
                    FetchLimit.ToString(),
                    sinceMs > 0 ? "&since_ms=" : string.Empty,
                    sinceMs > 0 ? sinceMs.ToString() : string.Empty
                );

                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
                request.Method = "GET";
                request.Timeout = RequestTimeoutMs;
                request.ReadWriteTimeout = RequestTimeoutMs;
                request.Proxy = null;
                request.Headers["X-Api-Key"] = RelayApiKey;

                string responseText;
                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                using (StreamReader reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
                {
                    responseText = reader.ReadToEnd();
                }

                ParseRawResponse(responseText);
            }
            catch (Exception ex)
            {
                log?.Invoke("[RedPacket] Relay poll failed, will retry: " + ex, LogLevel.WARNING);
                lock (StateLock)
                {
                    nextPollUtc = DateTime.UtcNow.AddSeconds(2);
                }
            }
            finally
            {
                Interlocked.Exchange(ref pollInFlight, 0);
            }
        }

        private static void ParseRawResponse(string responseText)
        {
            if (string.IsNullOrEmpty(responseText)) return;

            string[] lines = responseText.Split(new[] { '\n' }, StringSplitOptions.None);
            if (lines.Length == 0) return;

            long currentLastSeen;
            lock (StateLock)
            {
                currentLastSeen = lastSeenId;
            }

            long maxId = currentLastSeen;

            string firstLine = lines[0].Trim();
            if (long.TryParse(firstLine, out long reportedLastId))
            {
                maxId = Math.Max(maxId, reportedLastId);
            }

            for (int i = 1; i < lines.Length; i++)
            {
                string line = lines[i];
                if (string.IsNullOrEmpty(line)) continue;

                int tabIndex = line.IndexOf('\t');
                if (tabIndex <= 0) continue;

                string idPart = line.Substring(0, tabIndex).Trim();
                if (!long.TryParse(idPart, out long idValue)) continue;
                if (idValue <= currentLastSeen) continue;

                string b64 = line.Substring(tabIndex + 1).Trim();
                if (string.IsNullOrEmpty(b64)) continue;

                string message;
                try
                {
                    byte[] bytes = Convert.FromBase64String(b64);
                    message = Encoding.UTF8.GetString(bytes);
                }
                catch
                {
                    continue;
                }

                lock (IncomingLock)
                {
                    if (IncomingQueue.Count >= MaxIncomingQueue)
                    {
                        IncomingQueue.Dequeue();
                    }
                    IncomingQueue.Enqueue(message);
                }

                if (idValue > maxId) maxId = idValue;
            }

            lock (StateLock)
            {
                if (maxId > lastSeenId)
                {
                    lastSeenId = maxId;
                }
            }
        }

        private sealed class OutgoingProtocol
        {
            public string Message;
            public string SenderUuid;
            public string EventId;
        }
    }
}
