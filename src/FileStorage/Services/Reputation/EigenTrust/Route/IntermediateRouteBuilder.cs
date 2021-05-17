using System.Collections.Generic;
using Neo.FileStorage.API.Netmap;
using Neo.FileStorage.Services.Reputaion.Common;

namespace Neo.FileStorage.Services.Reputaion.EigenTrust.Route
{
    public class IntermediateRouteBuilder : IBuilder
    {
        public ManagerBuilder ManagerBuilder { get; init; }

        public List<NodeInfo> NextStage(ulong epoch, Trust trust, List<NodeInfo> passed)
        {
            if (1 < passed.Count) return new();
            return ManagerBuilder.BuilderManagers(epoch, trust.Peer);
        }
    }
}