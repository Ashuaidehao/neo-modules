using Google.Protobuf;
using Neo.FileStorage.API.Cryptography.Tz;
using Neo.FileStorage.API.Netmap;
using Neo.FileStorage.API.Refs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FSStorageGroup = Neo.FileStorage.API.StorageGroup.StorageGroup;
using FSObject = Neo.FileStorage.API.Object.Object;

namespace Neo.FileStorage.Services.Audit.Auditor
{
    public partial class Context
    {
        private int porRequests;
        private int porRetries;

        private void ExecutePoR()
        {
            if (Expired) return;
            var tasks = new Task[AuditTask.SGList.Count];
            for (int i = 0; i < AuditTask.SGList.Count; i++)
            {
                tasks[i] = Task.Run(() =>
                {
                    CheckStorageGroupPoR(i, AuditTask.SGList[i]);
                });
            }
            Task.WaitAll(tasks);
            report.SetPoRCounters((uint)porRequests, (uint)porRetries);
        }

        private void CheckStorageGroupPoR(int index, ObjectID oid)
        {
            FSStorageGroup sg;
            try
            {
                sg = ContainerCommunacator.GetStorageGroup(AuditTask, oid);
            }
            catch (Exception)
            {
                return;
            }
            var members = sg.Members.ToList();
            UpdateSGInfo(index, members);
            int acc_req = 0, acc_retries = 0;
            ulong total_size = 0;
            byte[] tzhash = null;
            foreach (var member in members)
            {
                List<List<Node>> placement;
                try
                {
                    placement = BuildPlacement(member);
                }
                catch (Exception)
                {
                    continue;
                }
                var random = new Random();
                var flat = placement.Flatten().OrderBy(p => random.Next()).ToList();
                for (int j = 0; j < flat.Count; j++)
                {
                    acc_req++;
                    if (0 < j) acc_retries++;
                    FSObject header;
                    try
                    {
                        header = ContainerCommunacator.GetHeader(AuditTask, flat[j], oid, true);
                    }
                    catch (Exception)
                    {
                        continue;
                    }
                    UpdateHeader(header);
                    if (tzhash is null)
                        tzhash = header.Header.HomomorphicHash.Sum.ToByteArray();
                    else
                    {
                        try
                        {
                            tzhash = TzHash.Concat(new List<byte[]> { tzhash, header.Header.HomomorphicHash.Sum.ToByteArray() });
                        }
                        catch
                        {
                            break;
                        }
                    }
                    total_size += header.Header.PayloadLength;
                    break;
                }
            }
            Interlocked.Add(ref porRequests, acc_req);
            Interlocked.Add(ref porRetries, acc_retries);
            var size_check = sg.ValidationDataSize == total_size;
            var tz_check = tzhash.SequenceEqual(sg.ValidationHash.ToByteArray());
            if (size_check && tz_check)
                report.PassedPoR(oid);
            else
            {
                if (!size_check)
                    Utility.Log(nameof(CheckStorageGroupPoR), LogLevel.Debug, $"storage group size check failed, expected={sg.ValidationHash}, actual={total_size}");
                else
                    Utility.Log(nameof(CheckStorageGroupPoR), LogLevel.Debug, $"storage group tz hash check failed");
                report.FailedPoR(oid);
            }
        }

        private void UpdateSGInfo(int index, List<ObjectID> members)
        {
            sgMembersCache[index] = members;
        }
    }
}