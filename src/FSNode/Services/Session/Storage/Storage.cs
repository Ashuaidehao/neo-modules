using Google.Protobuf;
using Grpc.Core;
using Neo.Cryptography;
using Neo.IO;
using Neo.Wallets;
using NeoFS.API.v2.Cryptography;
using NeoFS.API.v2.Refs;
using NeoFS.API.v2.Session;
using System;
using System.Collections.Generic;

namespace Neo.FSNode.Services.Session.Storage
{
    public class TokenStore
    {
        private Dictionary<Key, PrivateToken> tokens;

        public TokenStore()
        {
            tokens = new Dictionary<Key, PrivateToken>();
        }

        // Get returns private token corresponding to the given identifiers.
        public PrivateToken Get(OwnerID ownerID, byte[] tokenID)
        {
            var b = ownerID.ToByteArray();
            var k = new Key(Base58.Encode(tokenID), Base58.Encode(b));
            return tokens[k];
        }

        public CreateResponse.Types.Body Create(ServerCallContext ctx, CreateRequest.Types.Body body)
        {
            var b = body.OwnerId.ToByteArray();
            Guid guid = Guid.NewGuid();
            var gb = guid.ToByteArray();
            var sk = new byte[64];

            var random = new Random();
            random.NextBytes(sk);

            var key = new Key(Base58.Encode(gb), Base58.Encode(b));
            tokens[key] = new PrivateToken(sk.LoadPrivateKey(), body.Expiration);

            var keyPair = new KeyPair(sk);
            return new CreateResponse.Types.Body() { Id = ByteString.CopyFrom(gb), SessionKey = ByteString.CopyFrom(keyPair.PublicKey.EncodePoint(true)) };
        }
    }
}