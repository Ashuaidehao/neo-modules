using Neo.FileStorage.API.Object;
using Neo.FileStorage.API.Refs;
using Neo.IO.Data.LevelDB;
using System;
using System.Collections.Generic;
using System.Linq;
using FSObject = Neo.FileStorage.API.Object.Object;
using static Neo.Utility;

namespace Neo.FileStorage.LocalObjectStorage.MetaBase
{
    public sealed partial class MB
    {
        private readonly byte[] InhumeGCMarkValue = StrictUTF8.GetBytes("GCMARK");

        public void Inhume(Address tomb, List<Address> target)
        {
            byte[] tomb_key = InhumeGCMarkValue;
            if (tomb is not null)
            {
                tomb_key = GraveYardKey(tomb);
                byte[] data = db.Get(ReadOptions.Default, tomb_key);
                if (data is not null && !data.SequenceEqual(InhumeGCMarkValue))
                {
                    db.Delete(WriteOptions.Default, tomb_key);
                }
            }
            foreach (Address address in target)
            {
                FSObject obj = Get(address, false, true);
                if (obj.ObjectType == ObjectType.Regular)
                {
                    ChangeContainerSize(obj.ContainerId, obj.PayloadSize, false);
                }
                byte[] target_key = GraveYardKey(address);
                if (tomb is not null)
                {
                    bool is_tomb = false;
                    try
                    {
                        Iterate(GraveYardPrefix, (k, v) =>
                        {
                            is_tomb = target_key.SequenceEqual(v);
                            if (is_tomb) throw new IterateBreakException();
                        });
                    }
                    catch (IterateBreakException)
                    {
                        continue;
                    }
                }
                db.Put(WriteOptions.Default, target_key, tomb_key);
            }
        }
    }
}
