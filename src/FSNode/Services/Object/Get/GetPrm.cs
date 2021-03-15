using NeoFS.API.v2.Object;
using Neo.FSNode.Services.Object.Util;

namespace Neo.FSNode.Services.Object.Get
{
    public class GetPrm : GetCommonPrm
    {
        public static GetPrm FromRequest(GetRequest request)
        {
            var prm = new GetPrm
            {
                Address = request.Body.Address,
                Raw = request.Body.Raw,
            };
            prm.WithCommonPrm(CommonPrm.FromRequest(request));
            return prm;
        }
    }
}