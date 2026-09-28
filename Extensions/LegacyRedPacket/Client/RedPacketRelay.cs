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

        // A maximum-size (4 MiB) stateful item needs up to 750 bounded chunks.
        private const int MaxOutgoingQueue = 1024;
        private const int MaxIncomingQueue = 512;

        private static readonly object OutgoingLock = new object();
        private static readonly Queue<OutgoingProtocol> OutgoingQueue = new Queue<OutgoingProtocol>();
        private static readonly object IncomingLock = new object();
        private static readonly Queue<IncomingProtocol> IncomingQueue = new Queue<IncomingProtocol>();
        private static readonly HashSet<long> QueuedIncomingIds = new HashSet<long>();
        private static readonly object StateLock = new object();

        private static long lastSeenId;
        private static DateTime nextPollUtc = DateTime.MinValue;
        private static DateTime nextSendUtc = DateTime.MinValue;
        private static int pollInFlight;
        private static int sendInFlight;
        private static int pollWorkerSequence;
        private static int sendWorkerSequence;
        private static int generation;

        private const int PollIntervalMs = 700;
        private const int SendIntervalMs = 80;
        private const int RequestTimeoutMs = 5000;
        // Keep a full response below RedPacketLimits.MaxResponseBytes even when
        // every event is a near-maximum state chunk.
        private const int FetchLimit = 96;
        private const int InitialHistoryMinutes = 20;
        private static readonly DateTime UnixEpochUtc = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        private readonly Action<string, LogLevel> log;
        private string lastSendFailure;
        private DateTime nextSendFailureLogUtc;

        public RedPacketRelay(Action<string, LogLevel> log)
        {
            this.log = log;
        }

        public void Clear()
        {
            Interlocked.Increment(ref generation);
            lastSendFailure = null;
            nextSendFailureLogUtc = DateTime.MinValue;

            lock (OutgoingLock)
            {
                OutgoingQueue.Clear();
            }

            lock (IncomingLock)
            {
                IncomingQueue.Clear();
                QueuedIncomingIds.Clear();
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

        public bool TryPeekIncoming(out string protocolMessage)
        {
            lock (IncomingLock)
            {
                if (IncomingQueue.Count > 0)
                {
                    protocolMessage = IncomingQueue.Peek().Message;
                    return true;
                }
            }

            protocolMessage = null;
            return false;
        }

        public void AcknowledgeIncoming()
        {
            lock (IncomingLock)
            {
                if (IncomingQueue.Count == 0) return;
                IncomingProtocol item = IncomingQueue.Dequeue();
                QueuedIncomingIds.Remove(item.Id);
                lock (StateLock)
                {
                    if (item.Id > lastSeenId) lastSeenId = item.Id;
                }
            }
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
                int workerToken = Interlocked.Increment(ref sendWorkerSequence);
                if (Interlocked.CompareExchange(ref sendInFlight, workerToken, 0) == 0)
                {
                    int capturedGen = generation;
                    if (Volatile.Read(ref sendInFlight) == workerToken)
                        ThreadPool.QueueUserWorkItem(_ => SendOnceWorker(capturedGen, workerToken));
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

            if (shouldPoll)
            {
                int pollToken = Interlocked.Increment(ref pollWorkerSequence);
                if (Interlocked.CompareExchange(ref pollInFlight, pollToken, 0) == 0)
                {
                    int capturedGen = generation;
                    if (Volatile.Read(ref pollInFlight) == pollToken)
                        ThreadPool.QueueUserWorkItem(_ => PollWorker(capturedGen, pollToken));
                }
            }
        }

        private bool HasOutgoing()
        {
            lock (OutgoingLock)
            {
                return OutgoingQueue.Count > 0;
            }
        }

        private void SendOnceWorker(int capturedGen, int workerToken)
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

                if (generation != capturedGen) return;
                lastSendFailure = null;
                log?.Invoke("[RedPacket] Relay send ok (event " + outbound.EventId + ").", LogLevel.DEBUG);
            }
            catch (Exception ex)
            {
                if (generation != capturedGen) return;
                string failure = ex.ToString();
                DateTime now = DateTime.UtcNow;
                if (failure != lastSendFailure || now >= nextSendFailureLogUtc)
                {
                    lastSendFailure = failure;
                    nextSendFailureLogUtc = now.AddSeconds(30);
                    log?.Invoke("[RedPacket] Relay send failed, will retry: " + failure, LogLevel.WARNING);
                }
                if (outbound != null)
                {
                    lock (OutgoingLock)
                    {
                        if (OutgoingQueue.Count < MaxOutgoingQueue)
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
                // Only the worker that acquired this exact slot may release it.
                Interlocked.CompareExchange(ref sendInFlight, 0, workerToken);
            }
        }

        private void PollWorker(int capturedGen, int workerToken)
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

                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                {
                    if (response.ContentLength > RedPacketLimits.MaxResponseBytes)
                    {
                        log?.Invoke("[RedPacket] Relay response too large (" + response.ContentLength + " bytes), skipping.", LogLevel.WARNING);
                        return;
                    }

                    using (StreamReader reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
                    {
                        ParseRawResponseStreaming(reader, capturedGen);
                    }
                }
            }
            catch (Exception ex)
            {
                if (generation != capturedGen) return;
                log?.Invoke("[RedPacket] Relay poll failed, will retry: " + ex, LogLevel.WARNING);
                lock (StateLock)
                {
                    nextPollUtc = DateTime.UtcNow.AddSeconds(2);
                }
            }
            finally
            {
                Interlocked.CompareExchange(ref pollInFlight, 0, workerToken);
            }
        }

        private void ParseRawResponseStreaming(StreamReader reader, int capturedGen)
        {
            if (reader == null) return;

            string firstLine = reader.ReadLine();
            if (string.IsNullOrEmpty(firstLine)) return;

            long currentLastSeen;
            lock (StateLock)
            {
                currentLastSeen = lastSeenId;
            }

            firstLine = firstLine.Trim();

            int lineCount = 0;
            int totalBytes = firstLine.Length;
            bool queueFull = false;
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                lineCount++;
                totalBytes += line.Length;

                // RP-06: 响应行数和累计字节限制
                if (lineCount > FetchLimit || totalBytes > RedPacketLimits.MaxResponseBytes)
                    break;

                if (string.IsNullOrEmpty(line)) continue;

                int tabIndex = line.IndexOf('\t');
                if (tabIndex <= 0) continue;

                string idPart = line.Substring(0, tabIndex).Trim();
                if (!long.TryParse(idPart, out long idValue)) continue;
                if (idValue <= currentLastSeen) continue;

                string b64 = line.Substring(tabIndex + 1).Trim();
                string message = string.Empty;

                // RP-06: Base64 编码长度预检查（解码后约为 3/4）
                if (!string.IsNullOrEmpty(b64) && b64.Length <= RedPacketProtocol.MaxWireMessageChars)
                {
                    try
                    {
                        byte[] bytes = Convert.FromBase64String(b64);
                        message = Encoding.UTF8.GetString(bytes);
                    }
                    catch (FormatException) { }
                }

                // RP-06: 解码后消息长度检查
                if (message.Length > RedPacketProtocol.MaxWireMessageChars) message = string.Empty;

                // RP-16: generation 检查，防止旧 worker 回灌
                if (generation != capturedGen) return;

                lock (IncomingLock)
                {
                    if (QueuedIncomingIds.Contains(idValue)) continue;
                    if (IncomingQueue.Count >= MaxIncomingQueue)
                    {
                        queueFull = true;
                        break;
                    }
                    IncomingQueue.Enqueue(new IncomingProtocol { Id = idValue, Message = message });
                    QueuedIncomingIds.Add(idValue);
                }
            }

            if (queueFull)
                log?.Invoke("[RedPacket] Relay incoming queue is full; cursor held for retry.", LogLevel.WARNING);
        }

        private sealed class IncomingProtocol
        {
            public long Id;
            public string Message;
        }

        private sealed class OutgoingProtocol
        {
            public string Message;
            public string SenderUuid;
            public string EventId;
        }
    }
}
