using Akka.Actor;
using Neo.IO;
using Neo.FileStorage.InnerRing.Invoker;
using Neo.FileStorage.Morph.Event;
using Neo.FileStorage.Morph.Invoker;
using Neo.Plugins.util;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using static Neo.FileStorage.InnerRing.Invoker.ContractInvoker;
using static Neo.FileStorage.Morph.Event.MorphEvent;
using static Neo.Plugins.util.WorkerPool;

namespace Neo.FileStorage.InnerRing.Processors
{
    public class FsContractProcessor : IProcessor
    {
        private string name = "FsContractProcessor";
        private static UInt160 FsContractHash => Settings.Default.FsContractHash;
        private const string DepositNotification = "Deposit";
        private const string WithdrawNotification = "Withdraw";
        private const string ChequeNotification = "Cheque";
        private const string ConfigNotification = "SetConfig";
        private const string UpdateIRNotification = "InnerRingUpdate";

        private const string TxLogPrefix = "mainnet:";
        private const ulong LockAccountLifetime = 20;
        private object lockObj = new object();
        private int mintEmitCacheSize => Settings.Default.MintEmitCacheSize;
        private ulong mintEmitThreshold => Settings.Default.MintEmitThreshold;
        private long mintEmitValue = Settings.Default.MintEmitValue;
        private Dictionary<string, ulong> mintEmitCache;

        public IClient Client;
        public IActiveState ActiveState;
        public IEpochState EpochState;
        public IActorRef WorkPool;
        public Fixed8ConverterUtil Convert;
        public string Name { get => name; set => name = value; }

        public FsContractProcessor()
        {
            mintEmitCache = new Dictionary<string, ulong>(mintEmitCacheSize);
        }

        public HandlerInfo[] ListenerHandlers()
        {
            HandlerInfo depositHandler = new HandlerInfo();
            depositHandler.ScriptHashWithType = new ScriptHashWithType() { Type = DepositNotification, ScriptHashValue = FsContractHash };
            depositHandler.Handler = HandleDeposit;

            HandlerInfo withdrwaHandler = new HandlerInfo();
            withdrwaHandler.ScriptHashWithType = new ScriptHashWithType() { Type = WithdrawNotification, ScriptHashValue = FsContractHash };
            withdrwaHandler.Handler = HandleWithdraw;

            HandlerInfo chequeHandler = new HandlerInfo();
            chequeHandler.ScriptHashWithType = new ScriptHashWithType() { Type = ChequeNotification, ScriptHashValue = FsContractHash };
            chequeHandler.Handler = HandleCheque;

            HandlerInfo configHandler = new HandlerInfo();
            configHandler.ScriptHashWithType = new ScriptHashWithType() { Type = ConfigNotification, ScriptHashValue = FsContractHash };
            configHandler.Handler = HandleConfig;

            HandlerInfo updateIRHandler = new HandlerInfo();
            updateIRHandler.ScriptHashWithType = new ScriptHashWithType() { Type = UpdateIRNotification, ScriptHashValue = FsContractHash };
            updateIRHandler.Handler = HandleUpdateInnerRing;

            return new HandlerInfo[] { depositHandler, withdrwaHandler, chequeHandler, configHandler, updateIRHandler };
        }

        public ParserInfo[] ListenerParsers()
        {
            //deposit event
            ParserInfo depositParser = new ParserInfo();
            depositParser.ScriptHashWithType = new ScriptHashWithType() { Type = DepositNotification, ScriptHashValue = FsContractHash };
            depositParser.Parser = MorphEvent.ParseDepositEvent;

            //withdraw event
            ParserInfo withdrawParser = new ParserInfo();
            withdrawParser.ScriptHashWithType = new ScriptHashWithType() { Type = WithdrawNotification, ScriptHashValue = FsContractHash };
            withdrawParser.Parser = MorphEvent.ParseWithdrawEvent;

            //cheque event
            ParserInfo chequeParser = new ParserInfo();
            chequeParser.ScriptHashWithType = new ScriptHashWithType() { Type = ChequeNotification, ScriptHashValue = FsContractHash };
            chequeParser.Parser = MorphEvent.ParseChequeEvent;

            //config event
            ParserInfo configParser = new ParserInfo();
            configParser.ScriptHashWithType = new ScriptHashWithType() { Type = ConfigNotification, ScriptHashValue = FsContractHash };
            configParser.Parser = MorphEvent.ParseConfigEvent;

            //updateIR event
            ParserInfo updateIRParser = new ParserInfo();
            updateIRParser.ScriptHashWithType = new ScriptHashWithType() { Type = UpdateIRNotification, ScriptHashValue = FsContractHash };
            updateIRParser.Parser = MorphEvent.ParseUpdateInnerRingEvent;

            return new ParserInfo[] { depositParser, withdrawParser, chequeParser, configParser, updateIRParser };
        }

        public HandlerInfo[] TimersHandlers()
        {
            return new HandlerInfo[] { };
        }

        public void HandleDeposit(IContractEvent morphEvent)
        {
            DepositEvent depositeEvent = (DepositEvent)morphEvent;
            Dictionary<string, string> pairs = new Dictionary<string, string>();
            pairs.Add("notification", ":");
            pairs.Add("type", "deposit");
            pairs.Add("value", depositeEvent.Id.ToHexString());
            Neo.Utility.Log(Name, LogLevel.Info, pairs.ParseToString());
            WorkPool.Tell(new NewTask() { process = Name, task = new Task(() => ProcessDeposit(depositeEvent)) });
        }

        public void HandleWithdraw(IContractEvent morphEvent)
        {
            WithdrawEvent withdrawEvent = (WithdrawEvent)morphEvent;
            Dictionary<string, string> pairs = new Dictionary<string, string>();
            pairs.Add("notification", ":");
            pairs.Add("type", "withdraw");
            pairs.Add("value", withdrawEvent.Id.ToHexString());
            Neo.Utility.Log(Name, LogLevel.Info, pairs.ParseToString());
            WorkPool.Tell(new NewTask() { process = Name, task = new Task(() => ProcessWithdraw(withdrawEvent)) });
        }

        public void HandleCheque(IContractEvent morphEvent)
        {
            ChequeEvent chequeEvent = (ChequeEvent)morphEvent;
            Dictionary<string, string> pairs = new Dictionary<string, string>();
            pairs.Add("notification", ":");
            pairs.Add("type", "cheque");
            pairs.Add("value", chequeEvent.Id.ToHexString());
            Neo.Utility.Log(Name, LogLevel.Info, pairs.ParseToString());
            WorkPool.Tell(new NewTask() { process = Name, task = new Task(() => ProcessCheque(chequeEvent)) });
        }

        public void HandleConfig(IContractEvent morphEvent)
        {
            ConfigEvent configEvent = (ConfigEvent)morphEvent;
            Dictionary<string, string> pairs = new Dictionary<string, string>();
            pairs.Add("notification", ":");
            pairs.Add("type", "setConfig");
            pairs.Add("key", configEvent.Key.ToHexString());
            pairs.Add("value", configEvent.Value.ToHexString());
            Neo.Utility.Log(Name, LogLevel.Info, pairs.ParseToString());
            WorkPool.Tell(new NewTask() { process = Name, task = new Task(() => ProcessConfig(configEvent)) });
        }

        public void HandleUpdateInnerRing(IContractEvent morphEvent)
        {
            UpdateInnerRingEvent updateInnerRingEvent = (UpdateInnerRingEvent)morphEvent;
            Dictionary<string, string> pairs = new Dictionary<string, string>();
            pairs.Add("notification", ":");
            pairs.Add("type", "update inner ring");
            Neo.Utility.Log(Name, LogLevel.Info, pairs.ParseToString());
            WorkPool.Tell(new NewTask() { process = Name, task = new Task(() => ProcessUpdateInnerRing(updateInnerRingEvent)) });
        }

        public void ProcessDeposit(DepositEvent depositeEvent)
        {
            if (!IsActive())
            {
                Neo.Utility.Log(Name, LogLevel.Info, "passive mode, ignore deposit");
                return;
            }
            //invoke
            try
            {
                List<byte> coment = new List<byte>();
                coment.AddRange(System.Text.Encoding.UTF8.GetBytes(TxLogPrefix));
                coment.AddRange(depositeEvent.Id);
                ContractInvoker.Mint(Client, depositeEvent.To.ToArray(), Convert.ToBalancePrecision(depositeEvent.Amount), coment.ToArray());
            }
            catch (Exception e)
            {
                Neo.Utility.Log(Name, LogLevel.Error, string.Format("can't transfer assets to balance contract,{0}", e.Message));
            }

            var curEpoch = EpochState.EpochCounter();
            var receiver = depositeEvent.To;
            lock (lockObj)
            {
                var ok = mintEmitCache.TryGetValue(receiver.ToString(), out ulong value);
                if (ok && ((value + mintEmitThreshold) >= curEpoch))
                {
                    Dictionary<string, string> pairs = new Dictionary<string, string>();
                    pairs.Add("receiver", receiver.ToString());
                    pairs.Add("last_emission", value.ToString());
                    pairs.Add("current_epoch", curEpoch.ToString());
                    Neo.Utility.Log(Name, LogLevel.Warning, string.Format("double mint emission declined,{0}", pairs.ParseToString()));
                }
                //transferGas
                try
                {
                    ((MorphClient)Client).TransferGas(depositeEvent.To, mintEmitValue);
                }
                catch (Exception e)
                {
                    Neo.Utility.Log(Name, LogLevel.Error, string.Format("can't transfer native gas to receiver,{0}", e.Message));
                }
                mintEmitCache.Add(receiver.ToString(), curEpoch);
            }
        }

        public void ProcessWithdraw(WithdrawEvent withdrawEvent)
        {
            if (!IsActive())
            {
                Neo.Utility.Log(Name, LogLevel.Info, "passive mode, ignore withdraw");
                return;
            }
            if (withdrawEvent.Id.Length < UInt160.Length)
            {
                Neo.Utility.Log(Name, LogLevel.Error, "tx id size is less than script hash size");
                return;
            }
            UInt160 lockeAccount = null;
            try
            {
                lockeAccount = new UInt160(withdrawEvent.Id.Take(UInt160.Length).ToArray());
            }
            catch (Exception e)
            {
                Neo.Utility.Log(Name, LogLevel.Error, string.Format("can't create lock account,{0}", e.Message));
                return;
            }
            try
            {
                ulong curEpoch = EpochCounter();
                //invoke
                ContractInvoker.LockAsset(Client, withdrawEvent.Id, withdrawEvent.UserAccount, lockeAccount, Convert.ToBalancePrecision(withdrawEvent.Amount), curEpoch + LockAccountLifetime);
            }
            catch (Exception e)
            {
                Neo.Utility.Log(Name, LogLevel.Error, string.Format("can't lock assets for withdraw,{0}", e.Message));
            }
        }

        public void ProcessCheque(ChequeEvent chequeEvent)
        {
            if (!IsActive())
            {
                Neo.Utility.Log(Name, LogLevel.Info, "passive mode, ignore cheque");
                return;
            }
            //invoke
            try
            {
                List<byte> coment = new List<byte>();
                coment.AddRange(System.Text.Encoding.UTF8.GetBytes(TxLogPrefix));
                coment.AddRange(chequeEvent.Id);

                ContractInvoker.Burn(Client, chequeEvent.LockAccount.ToArray(), Convert.ToBalancePrecision(chequeEvent.Amount), coment.ToArray());
            }
            catch (Exception e)
            {
                Neo.Utility.Log(Name, LogLevel.Error, string.Format("can't transfer assets to fed contract,{0}", e.Message));
            }
        }

        public void ProcessConfig(ConfigEvent configEvent)
        {
            if (!IsActive())
            {
                Neo.Utility.Log(Name, LogLevel.Info, "passive mode, ignore deposit");
                return;
            }
            //invoke
            try
            {
                ContractInvoker.SetConfig(Client, configEvent.Id, configEvent.Key, configEvent.Value);
            }
            catch (Exception e)
            {
                Neo.Utility.Log(Name, LogLevel.Error, string.Format("can't relay set config event,{0}", e.Message));
            }
        }

        public void ProcessUpdateInnerRing(UpdateInnerRingEvent updateInnerRingEvent)
        {
            if (!IsActive())
            {
                Neo.Utility.Log(Name, LogLevel.Info, "passive mode, ignore deposit");
                return;
            }
            //invoke
            try
            {
                ContractInvoker.UpdateInnerRing(Client, updateInnerRingEvent.Keys);
            }
            catch (Exception e)
            {
                Neo.Utility.Log(Name, LogLevel.Error, string.Format("can't relay update inner ring event,{0}", e.Message));
            }
        }

        public ulong EpochCounter()
        {
            return EpochState.EpochCounter();
        }

        public bool IsActive()
        {
            return ActiveState.IsActive();
        }
    }
}
