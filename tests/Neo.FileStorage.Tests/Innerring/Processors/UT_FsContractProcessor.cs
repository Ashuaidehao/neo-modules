using Akka.Actor;
using Akka.TestKit.Xunit2;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Neo.IO;
using Neo.FileStorage.InnerRing.Processors;
using Neo.FileStorage.Morph.Invoker;
using Neo.Plugins.util;
using Neo.Wallets;
using System.Collections.Generic;
using System.Linq;
using static Neo.FileStorage.Morph.Event.MorphEvent;

namespace Neo.FileStorage.Tests.InnerRing.Processors
{
    [TestClass]
    public class UT_FsContractProcessor : TestKit
    {
        private NeoSystem system;
        private FsContractProcessor processor;
        private MorphClient morphclient;
        private Wallet wallet;
        private TestActiveState activeState;

        [TestInitialize]
        public void TestSetup()
        {
            system = TestBlockchain.TheNeoSystem;
            system.ActorSystem.ActorOf(Props.Create(() => new ProcessorFakeActor()));
            wallet = TestBlockchain.wallet;
            morphclient = new MorphClient()
            {
                wallet = wallet,
                system = system
            };
            activeState = new TestActiveState();
            activeState.SetActive(true);
            processor = new FsContractProcessor()
            {
                MorphCli = new Client() { client = morphclient },
                Convert = new Fixed8ConverterUtil(),
                //ActiveState = activeState,
                //EpochState = new EpochState(),
                WorkPool = system.ActorSystem.ActorOf(Props.Create(() => new ProcessorFakeActor()))
            };
        }

        [TestMethod]
        public void HandleDepositTest()
        {
            processor.HandleDeposit(new DepositEvent()
            {
                Id = new byte[] { 0x01 },
                Amount = 0,
                From = UInt160.Zero,
                To = UInt160.Zero
            });
            var nt = ExpectMsg<ProcessorFakeActor.OperationResult2>().nt;
            Assert.IsNotNull(nt);
        }

        [TestMethod]
        public void HandleWithdrawTest()
        {
            processor.HandleWithdraw(new WithdrawEvent()
            {
                Id = new byte[] { 0x01 },
                Amount = 0,
                UserAccount = UInt160.Zero
            });
            var nt = ExpectMsg<ProcessorFakeActor.OperationResult2>().nt;
            Assert.IsNotNull(nt);
        }

        [TestMethod]
        public void HandleChequeTest()
        {
            processor.HandleCheque(new ChequeEvent()
            {
                Id = new byte[] { 0x01 },
                Amount = 0,
                UserAccount = UInt160.Zero,
                LockAccount = UInt160.Zero
            });
            var nt = ExpectMsg<ProcessorFakeActor.OperationResult2>().nt;
            Assert.IsNotNull(nt);
        }

        [TestMethod]
        public void HandleConfigTest()
        {
            processor.HandleConfig(new ConfigEvent()
            {
                Key = new byte[] { 0x01 },
                Value = new byte[] { 0x01 }
            });
            var nt = ExpectMsg<ProcessorFakeActor.OperationResult2>().nt;
            Assert.IsNotNull(nt);
        }

        [TestMethod]
        public void ProcessDepositTest()
        {
            IEnumerable<WalletAccount> accounts = wallet.GetAccounts();
            processor.ProcessDeposit(new DepositEvent()
            {
                Id = new byte[] { 0x01 },
                Amount = 0,
                From = UInt160.Zero,
                To = accounts.ToArray()[0].ScriptHash
            });
            var tx = ExpectMsg<ProcessorFakeActor.OperationResult1>().tx;
            Assert.IsNotNull(tx);
        }

        [TestMethod]
        public void ProcessWithdrawTest()
        {
            processor.ProcessWithdraw(new WithdrawEvent()
            {
                Id = UInt160.Zero.ToArray(),
                Amount = 0,
                UserAccount = UInt160.Zero
            });
            var tx = ExpectMsg<ProcessorFakeActor.OperationResult1>().tx;
            Assert.IsNotNull(tx);
        }

        [TestMethod]
        public void ProcessChequeTest()
        {
            processor.ProcessCheque(new ChequeEvent()
            {
                Id = new byte[] { 0x01 },
                Amount = 0,
                UserAccount = UInt160.Zero,
                LockAccount = UInt160.Zero
            });
            var tx = ExpectMsg<ProcessorFakeActor.OperationResult1>().tx;
            Assert.IsNotNull(tx);
        }

        [TestMethod]
        public void ProcessConfigTest()
        {
            processor.ProcessConfig(new ConfigEvent()
            {
                Id = new byte[] { 0x01 },
                Key = Neo.Utility.StrictUTF8.GetBytes("ContainerFee"),
                Value = new byte[] { 0x01 }
            });
            var tx = ExpectMsg<ProcessorFakeActor.OperationResult1>().tx;
            Assert.IsNotNull(tx);
        }

        [TestMethod]
        public void ListenerHandlersTest()
        {
            var handlerInfos = processor.ListenerHandlers();
            Assert.AreEqual(handlerInfos.Length, 5);
        }

        [TestMethod]
        public void ListenerParsersTest()
        {
            var parserInfos = processor.ListenerParsers();
            Assert.AreEqual(parserInfos.Length, 5);
        }

        [TestMethod]
        public void ListenerTimersHandlersTest()
        {
            var handlerInfos = processor.TimersHandlers();
            Assert.AreEqual(0, handlerInfos.Length);
        }
    }
}