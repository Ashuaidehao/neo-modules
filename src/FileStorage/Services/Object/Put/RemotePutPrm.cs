using FSObject = Neo.FileStorage.API.Object.Object;

namespace Neo.FileStorage.Services.Object.Put
{
    public class RemotePutPrm
    {
        public Network.Address Node;
        public FSObject Object;
    }
}
