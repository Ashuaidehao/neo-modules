using Neo.FileStorage.API.Session;

namespace Neo.FileStorage.Services.Util.Response
{
    public class ServerMessageStreamer
    {
        private Cfg cfg;
        private ResponseMessageReader recv;

        public ServerMessageStreamer(Cfg c, ResponseMessageReader msgRdr)
        {
            this.cfg = c;
            this.recv = msgRdr;
        }

        public IResponse Recv()
        {
            var m = this.recv();
            Helper.SetMeta(m, this.cfg);
            return m;
        }
    }
}