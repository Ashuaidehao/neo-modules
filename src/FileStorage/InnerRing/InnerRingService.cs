using Akka.Actor;
using Neo.SmartContract;
using System;
using Neo.Plugins.util;
using Neo.Wallets.NEP6;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Neo.Cryptography.ECC;
using Neo.IO;
using Google.Protobuf;
using Neo.FileStorage.InnerRing.Processors;
using Neo.FileStorage.Services.Audit;
using Neo.FileStorage.Morph.Event;
using static Neo.FileStorage.Morph.Event.Listener;
using Neo.FileStorage.API.Audit;
using Neo.FileStorage.Morph.Invoker;
using Neo.FileStorage.InnerRing.Invoker;
using Neo.FileStorage.Utils;
using static Neo.FileStorage.InnerRing.Timer.BlockTimer;
using static Neo.FileStorage.InnerRing.Timer.Helper;
using Neo.Network.P2P.Payloads;
using static Neo.FileStorage.InnerRing.Processors.SettlementProcessor;
using Neo.IO.Data.LevelDB;

namespace Neo.FileStorage.InnerRing
{
    /// <summary>
    /// InneringService is the entry for processing all events related to the inner ring node.
    /// All events will be distributed according to type.(2 event types:MainContractEvent and MorphContractEvent)
    /// Life process:Start--->Assignment event--->Stop
    /// </summary>
    public class InnerRingService : UntypedActor, IState
    {
        //event 
        public class ContractEvent { public NotifyEventArgs notify; public bool flag; };
        public class BlockEvent { public Block block; public bool flag; };
        public class Start { };
        public class Stop { };
        // event producers
        private IActorRef morphEventListener;
        private IActorRef mainEventListener;
        private List<IActorRef> blockTimers;
        private IActorRef epochTimer;
        // global state
        private Client mainNetClient;
        private Client morphClient;
        private long epochCounter;
        private InnerRingIndexer statusIndex;
        private Fixed8ConverterUtil precision;
        private IActorRef auditTaskManager;
        // internal variables
        private readonly NEP6Wallet wallet;

        private BalanceContractProcessor balanceContractProcessor;
        private ContainerContractProcessor containerContractProcessor;
        private FsContractProcessor fsContractProcessor;
        private NetMapContractProcessor netMapContractProcessor;
        private GovernanceProcessor governanceProcessor;
        private SettlementProcessor settlementProcessor;
        private AlphabetContractProcessor alphabetContractProcessor;
        private AuditContractProcessor auditContractProcessor;

        private RpcClientCache clientCache;
        private DB _db;

        /// <summary>
        /// Constructor.
        /// 4 Tasks:
        /// 1)Build mainnet and morph clients
        /// 2)Build mainnet and morph contract event handlers
        /// 3)Build mainnet and morph event listeners
        /// 4)Initialization
        /// </summary>
        /// <param name="system">NeoSystem</param>
        public InnerRingService(NeoSystem main, NeoSystem side, NEP6Wallet pwallet = null, Client pMainNetClient = null, Client pMorphClient = null)
        {
            precision = new Fixed8ConverterUtil();
            if (pwallet is null)
            {
                wallet = new NEP6Wallet(Settings.Default.WalletPath, main.Settings);
                wallet.Unlock(Settings.Default.Password);
            }
            else wallet = pwallet;
            if (pMainNetClient is null)
            {
                mainNetClient = new Client()
                {
                    client = new MorphClient()
                    {
                        wallet = wallet,
                        system = main,
                    }
                };
            }
            else morphClient = pMorphClient;
            if (pMorphClient is null)
            {
                morphClient = new Client()
                {
                    client = new MorphClient()
                    {
                        wallet = wallet,
                        system = side,
                    }
                };
            }
            else morphClient = pMorphClient;
            //Build 2 listeners(MorphEventListener&MainEventListener).
            morphEventListener = side.ActorSystem.ActorOf(Listener.Props("MorphEventListener"));
            mainEventListener = main.ActorSystem.ActorOf(Listener.Props("MainEventListener"));
            statusIndex = new InnerRingIndexer(morphClient, Settings.Default.IndexerTimeout);
            // create audit processor
            clientCache = new RpcClientCache() { wallet = wallet };
            auditTaskManager = side.ActorSystem.ActorOf(Manager.Props(Settings.Default.QueueCapacity, clientCache, Settings.Default.MaxPDPSleepInterval));
            auditContractProcessor = new AuditContractProcessor()
            {
                MorphCli = morphClient,
                ClientCache = clientCache,
                TaskManager = auditTaskManager,
                reporter = this,
            };
            // create settlement processor dependencies
            var auditCalcDeps = new AuditSettlementDeps()
            {
                client = morphClient,
                clientCache = clientCache,
            };
            var basicSettlementDeps = new BasicIncomeSettlementDeps()
            {
                client = morphClient,
            };
            var auditSettlementCalc = new Calculator(auditCalcDeps);
            settlementProcessor = new SettlementProcessor()
            {
                basicIncome = basicSettlementDeps,
                auditProc = auditSettlementCalc,
                State = this,
            };
            var locodeValidator = new Validator(null);
            // create governance processor
            governanceProcessor = new GovernanceProcessor()
            {
                MainCli = mainNetClient,
                MorphCli = morphClient,
                State = this,
            };
            // create netmap processor
            //todo
            netMapContractProcessor = new NetMapContractProcessor()
            {
                MorphCli = morphClient,
                State = this,
                NetmapSnapshot = new CleanupTable(Settings.Default.CleanupEnabled, Settings.Default.CleanupThreshold),
                WorkPool = side.ActorSystem.ActorOf(WorkerPool.Props("NetMapContract Processor", Settings.Default.NetmapContractWorkersSize)),
                HandleNewAudit = OnlyActiveEventHandler(auditContractProcessor.HandleNewAuditRound),
                HandleAuditSettlements = OnlyAlphabetEventHandler(settlementProcessor.HandleAuditEvent),
                HandleAlphabetSync = governanceProcessor.HandleAlphabetSync,
                NodeValidator = locodeValidator
            };
            morphEventListener.Tell(new BindProcessorEvent() { processor = netMapContractProcessor });
            // create container processor
            containerContractProcessor = new ContainerContractProcessor()
            {
                MorphCli = morphClient,
                State = this,
                WorkPool = side.ActorSystem.ActorOf(WorkerPool.Props("ContainerContract Processor", Settings.Default.ContainerContractWorkersSize))
            };
            morphEventListener.Tell(new BindProcessorEvent() { processor = containerContractProcessor });
            // create balance processor
            balanceContractProcessor = new BalanceContractProcessor()
            {
                MainCli = mainNetClient,
                Convert = precision,
                State = this,
                WorkPool = side.ActorSystem.ActorOf(WorkerPool.Props("BalanceContract Processor", Settings.Default.BalanceContractWorkersSize))
            };
            morphEventListener.Tell(new BindProcessorEvent() { processor = balanceContractProcessor });
            // todo: create reputation processor
            // create  neofs processor
            fsContractProcessor = new FsContractProcessor()
            {
                MorphCli = morphClient,
                Convert = precision,
                State = this,
                WorkPool = side.ActorSystem.ActorOf(WorkerPool.Props("FsContract Processor", Settings.Default.FsContractWorkersSize))
            };
            mainEventListener.Tell(new BindProcessorEvent() { processor = fsContractProcessor });
            // create alphabet processor
            alphabetContractProcessor = new AlphabetContractProcessor()
            {
                MorphCli = morphClient,
                StorageEmission = Settings.Default.StorageEmission,
                WorkPool = side.ActorSystem.ActorOf(WorkerPool.Props("AlphabetContract Processor", Settings.Default.AlphabetContractWorkersSize))
            };
            // todo: create vivid id component
            // initialize epoch timers
            epochTimer = NewEpochTimer(new EpochTimerArgs()
            {
                context = side,
                processor = netMapContractProcessor,
                client = morphClient,
                epoch = this,
                epochDuration = Settings.Default.EpochDuration,
                stopEstimationDMul = Settings.Default.StopEstimationDMul,
                stopEstimationDDiv = Settings.Default.StopEstimationDDiv,
                collectBasicIncome = new SubEpochEventHandler()
                {
                    handler = settlementProcessor.HandleIncomeCollectionEvent,
                    durationMul = Settings.Default.CollectBasicIncomeMul,
                    durationDiv = Settings.Default.CollectBasicIncomeDiv,
                },
                distributeBasicIncome = new SubEpochEventHandler()
                {
                    handler = settlementProcessor.HandleIncomeDistributionEvent,
                    durationMul = Settings.Default.DistributeBasicIncomeMul,
                    durationDiv = Settings.Default.DistributeBasicIncomeDiv,
                }
            });
            blockTimers.Add(epochTimer);
            // initialize emission timer
            var emissionTimer = NewEmissionTimer(new EmitTimerArgs()
            {
                context = side,
                processor = alphabetContractProcessor,
                epochDuration = Settings.Default.AlphabetDuration
            });
            blockTimers.Add(emissionTimer);
            //todo
            //notary
        }

        public void InitConfigFromBlockchain()
        {
            ulong epoch;
            try
            {
                epoch = morphClient.GetEpoch();
            }
            catch
            {
                throw new Exception("can't read epoch");
            }
            uint balancePrecision;
            try
            {
                balancePrecision = morphClient.BalancePrecision();
            }
            catch
            {
                throw new Exception("can't read balance contract precision");
            }
            SetEpochCounter((ulong)epoch);
            precision.SetBalancePrecision(balancePrecision);
            Dictionary<string, string> pairs = new Dictionary<string, string>();
            pairs.Add("read config from blockchain", ":");
            pairs.Add("active", IsActive().ToString());
            pairs.Add("epoch", epoch.ToString());
            pairs.Add("precision", balancePrecision.ToString());
            Utility.Log("InnerRingService", LogLevel.Info, pairs.ParseToString());
        }

        protected override void OnReceive(object message)
        {
            switch (message)
            {
                case Start _:
                    OnStart();
                    break;
                case Stop _:
                    OnStop();
                    break;
                case BlockEvent blockEvent:
                    OnStop();
                    break;
                case ContractEvent contractEvent:
                    OnContractEvent(contractEvent.notify, contractEvent.flag);
                    break;
                default:
                    break;
            }
        }

        private void OnStart()
        {
            try
            {
                InitConfigFromBlockchain();
                //to do notary
                VoteForSidechainValidator(Settings.Default.validators);
                morphEventListener.Tell(new BindBlockHandlerEvent()
                {
                    handler = (Block b) =>
                    {
                        Utility.Log("MorphEventListener", LogLevel.Debug, string.Format("new block,index:{0}", b.Index));
                        TickTimers();
                    }
                });
                morphEventListener.Tell(new Listener.Start());
                mainEventListener.Tell(new Listener.Start());
                StartBlockTimers();
            }
            catch (Exception e)
            {
                Utility.Log("InnerRingService", LogLevel.Error, e.Message);
                return;
            }
        }

        private void StartBlockTimers()
        {
            foreach (var blockTimer in blockTimers)
            {
                blockTimer.Tell(new ResetEvent());
            }
        }

        private void TickTimers()
        {
            foreach (var blockTimer in blockTimers)
                blockTimer.Tell(new TickEvent());
        }

        private void OnStop()
        {
            morphEventListener.Tell(new Listener.Stop());
            mainEventListener.Tell(new Listener.Stop());
        }

        private void OnContractEvent(NotifyEventArgs notify, bool flag)
        {
            if (flag) mainEventListener.Tell(new NewContractEvent() { notify = notify });
            else morphEventListener.Tell(new NewContractEvent() { notify = notify });
        }

        private void OnBlockEvent(Block block, bool flag)
        {
            if (flag) mainEventListener.Tell(new NewBlockEvent() { block = block });
            else morphEventListener.Tell(new NewBlockEvent() { block = block });
        }

        public ulong EpochCounter()
        {
            return Convert.ToUInt64(epochCounter);
        }

        public void SetEpochCounter(ulong epoch)
        {
            long temp = Convert.ToInt64(epoch);
            Interlocked.Exchange(ref epochCounter, temp);
        }

        public bool IsActive()
        {
            return InnerRingIndex() >= 0;
        }

        public bool IsAlphabet()
        {
            return AlphabetIndex() >= 0;
        }

        public int AlphabetIndex()
        {
            return statusIndex.AlphabetIndex();
        }

        public int InnerRingIndex()
        {
            return statusIndex.InnerRingIndex();
        }

        public int InnerRingSize()
        {
            return statusIndex.InnerRingSize();
        }

        public void VoteForSidechainValidator(ECPoint[] validators)
        {
            if (InnerRingIndex() < 0 || InnerRingIndex() >= Settings.Default.AlphabetContractHash.Length)
            {
                Utility.Log("", LogLevel.Info, "ignore validator vote: node not in alphabet range");
                return;
            }
            if (validators.Length == 0)
            {
                Utility.Log("", LogLevel.Info, "ignore validator vote: empty validators list");
                return;
            }
            var epoch = EpochCounter();
            for (int i = 0; i < Settings.Default.AlphabetContractHash.Length; i++)
            {
                try
                {
                    morphClient.AlphabetVote(i, epoch, validators);
                }
                catch
                {
                    Utility.Log("", LogLevel.Info, string.Format("can't invoke vote method in alphabet contract,alphabet_index:{0},epoch:{1}}", i, epoch));
                }
            }
        }

        public void InitAndVoteForSidechainValidator(ECPoint[] validators)
        {
            InitConfigFromBlockchain();
            VoteForSidechainValidator(validators);
        }

        public void WriteReport(Report r)
        {
            IEnumerable<Wallets.WalletAccount> accounts = wallet.GetAccounts();
            DataAuditResult res = r.Result();
            res.PublicKey = ByteString.CopyFrom(accounts.ToArray()[0].GetKey().PublicKey.ToArray());
            morphClient.InvokePutAuditResult(res.ToByteArray());
        }

        public void ResetEpochTimer()
        {
            epochTimer.Tell(new ResetEvent());
        }

        public Action<IContractEvent> OnlyActiveEventHandler(Action<IContractEvent> f)
        {
            return (IContractEvent morphEvent) => { if (IsActive()) f(morphEvent); };
        }
        public Action<IContractEvent> OnlyAlphabetEventHandler(Action<IContractEvent> f)
        {
            return (IContractEvent morphEvent) => { if (IsAlphabet()) f(morphEvent); };
        }

        public static Props Props(NeoSystem main, NeoSystem side, NEP6Wallet pwallet = null, Client pMainNetClient = null, Client pMorphClient = null)
        {
            return Akka.Actor.Props.Create(() => new InnerRingService(main, side, pwallet, pMainNetClient, pMorphClient));
        }
    }
}
