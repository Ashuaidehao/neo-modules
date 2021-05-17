using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Neo.FileStorage.Morph.Invoker;
using Neo.FileStorage.Services.Reputaion.EigenTrust.Calculate;
using Neo.FileStorage.Utils;

namespace Neo.FileStorage.Services.Reputaion.EigenTrust.Control
{
    public class Controller
    {
        public Client MorphClient { get; init; }
        public Calculator DaughterTrustCalculator { get; init; }
        public IActorRef WorkerPool { get; init; }
        private uint iterationNumber;
        private readonly ConcurrentDictionary<ulong, IterationContextCancellable> ctxs = new();

        public void Continue(ulong epoch)
        {
            if (!ctxs.TryGetValue(epoch, out IterationContextCancellable context))
            {
                CancellationTokenSource cancel = new();
                context = new()
                {
                    Context = new()
                    {
                        Cancellation = cancel.Token,
                        Epoch = epoch,
                    },
                    Cancel = cancel,
                };
                ctxs[epoch] = context;
            }
            context.Context.Last = context.Context.Index == iterationNumber - 1;
            WorkerPool.Tell(new WorkerPool.NewTask
            {
                Process = "Reputation",
                Task = Task.Run(() =>
                {
                    DaughterTrustCalculator.Calculate(context.Context);
                    context.Context.Index++;
                }),
            });
            if (context.Context.Last)
            {
                ctxs.Remove(epoch, out _);
                var iterations = MorphClient.EigenTrustIterations();
                iterationNumber = (uint)iterations;
            }
        }
    }
}