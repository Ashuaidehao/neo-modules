using System.Collections.Concurrent;
using System.Collections.Generic;
using Neo.FileStorage.API.Cryptography.Tz;
using Neo.FileStorage.API.Refs;
using FSObject = Neo.FileStorage.API.Object.Object;

namespace Neo.FileStorage.Services.Audit.Auditor
{
    public partial class Context
    {
        private const int hashRangeNumber = 4;
        private readonly int minGamePayloadSize = hashRangeNumber * new TzHash().HashSize;
        public IContainerCommunicator ContainerCommunacator;
        public AuditTask AuditTask;
        public ulong MaxPDPInterval;//MillisecondsTimeout
        private Report report;
        private readonly ConcurrentDictionary<string, ShortHeader> HeaderCache = default;
        private readonly List<GamePair> pairs = default;
        private readonly ConcurrentDictionary<ulong, PairMemberInfo> pairedNodes = default;
        private bool Expired => AuditTask.Cancellation.IsCancellationRequested;

        public void Execute()
        {
            Initialize();
            ExecutePoR();
            ExecutePoP();
            ExecutePDP();
            Complete();
            WriteReport();
        }

        private void Initialize()
        {
            report = new Report();
            report.SetContainerID(AuditTask.ContainerID);
        }

        private void Complete()
        {
            if (Expired) return;
            report.SetComplete();
        }

        private void WriteReport()
        {
            AuditTask.Reporter.WriteReport(report);
        }

        private ulong ObjectSize(ObjectID oid)
        {
            if (HeaderCache.TryGetValue(oid.ToBase58String(), out ShortHeader header))
                return header.ObjectSize;
            return 0;
        }

        private void UpdateHeader(FSObject header)
        {
            HeaderCache[header.ObjectId.ToBase58String()] = new ShortHeader
            {
                TzHash = header.Header.HomomorphicHash.Sum.ToByteArray(),
                ObjectSize = header.PayloadSize,
            };
        }
    }
}
