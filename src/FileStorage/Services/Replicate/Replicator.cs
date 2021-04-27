using Akka.Actor;
using Neo.FileStorage.API.Netmap;
using Neo.FileStorage.LocalObjectStorage.Engine;
using Neo.FileStorage.Network.Cache;
using Neo.FileStorage.Services.Object.Put;
using Neo.FileStorage.Services.Object.Util;
using System;
using System.Collections.Generic;
using System.Threading;
using FSAddress = Neo.FileStorage.API.Refs.Address;
using FSObject = Neo.FileStorage.API.Object.Object;

namespace Neo.FileStorage.Services.Replicate
{
    public class Replicator : UntypedActor
    {
        public class Task
        {
            public uint Quantity;
            public FSAddress Address;
            public List<Node> Nodes;
        }
        private readonly Configuration config;

        public Replicator(Configuration c)
        {
            config = c;
        }

        protected override void OnReceive(object message)
        {
            switch (message)
            {
                case Task task:
                    HandleTask(task);
                    break;
            }
        }

        private void HandleTask(Task task)
        {
            FSObject obj;
            try
            {
                obj = config.LocalStorage.Get(task.Address);
            }
            catch (Exception)
            {
                return;
            }
            RemotePutPrm prm = new()
            {
                Object = obj,
            };
            for (int i = 0; i < task.Nodes.Count && 0 < task.Quantity; i++)
            {
                var net_address = task.Nodes[i].NetworkAddress;
                var node = Network.Address.AddressFromString(net_address);
                prm.Node = node;
                config.RemoteSender.PutObject(prm, new CancellationTokenSource(config.PutTimeout).Token);
                task.Quantity--;
            }
        }

        public static Props Props(Configuration c)
        {
            return Akka.Actor.Props.Create(() => new Replicator(c));
        }
    }
}
