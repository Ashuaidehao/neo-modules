using Neo.FileStorage.API.Object;
using Neo.FileStorage.API.Refs;
using Neo.IO.Data.LevelDB;
using System.Collections.Generic;
using System.Linq;
using static Neo.FileStorage.LocalObjectStorage.MetaBase.Helper;
using FSObject = Neo.FileStorage.API.Object.Object;

namespace Neo.FileStorage.LocalObjectStorage.MetaBase
{
    public sealed partial class MB
    {
        private class ReferenceNumber
        {
            public int All;
            public int Current;
            public Address Address;
            public FSObject Object;
        }

        public void Delete(params Address[] addresses)
        {
            Dictionary<string, ReferenceNumber> ref_counter = new();
            foreach (Address addr in addresses)
            {
                Delete(addr, ref_counter);
            }
            foreach (ReferenceNumber rn in ref_counter.Values)
            {
                if (rn.Current == rn.All)
                {
                    DeleteObject(rn.Object, true);
                }
            }
        }

        private void Delete(Address address, Dictionary<string, ReferenceNumber> ref_counter)
        {
            FSObject obj = Get(address, false, true);
            if (obj.Parent is not null)
            {
                if (ref_counter.TryGetValue(obj.Parent.Address.String(), out ReferenceNumber rn))
                {
                    rn.Current++;
                }
                else
                {
                    ref_counter[obj.Parent.Address.String()] = new()
                    {
                        All = ParentLength(new() { ContainerId = address.ContainerId, ObjectId = obj.ParentId }),
                        Address = obj.Parent.Address,
                        Object = obj.Parent,
                    };
                }
            }
            DeleteObject(obj, false);
        }

        private void DeleteObject(FSObject obj, bool is_parent)
        {
            foreach (byte[] key in DeleteUniqueIndexes(obj, is_parent))
                db.Delete(WriteOptions.Default, key);

            foreach (var (key, oid) in ListIndexes(obj))
            {
                var data = db.Get(ReadOptions.Default, key);
                if (data is null) continue;
                var list = DecodeObjectIDList(data);
                list.Remove(oid);
                if (!list.Any())
                    db.Delete(WriteOptions.Default, key);
                else
                    db.Put(WriteOptions.Default, key, EncodeObjectIDList(list));
            }

            foreach (byte[] key in FakeBucketTreeIndexes(obj))
                db.Delete(WriteOptions.Default, key);
        }

        private List<byte[]> DeleteUniqueIndexes(FSObject obj, bool is_parent)
        {
            List<byte[]> keys = new();
            if (!is_parent)
            {
                switch (obj.ObjectType)
                {
                    case ObjectType.Regular:
                        keys.Add(Primarykey(obj.Address));
                        break;
                    case ObjectType.Tombstone:
                        keys.Add(TombstoneKey(obj.Address));
                        break;
                    case ObjectType.StorageGroup:
                        keys.Add(StorageGroupKey(obj.Address));
                        break;
                    default:
                        throw new UnknownObjectTypeException();
                }
            }
            else
            {
                keys.Add(ParentKey(obj.ContainerId, obj.ObjectId));
            }
            keys.Add(SmallKey(obj.Address));
            keys.Add(RootKey(obj.Address));
            keys.Add(GraveYardKey(obj.Address));
            keys.Add(ToMoveItKey(obj.Address));
            return keys;
        }

        private List<(byte[], ObjectID)> ListIndexes(FSObject obj)
        {
            List<(byte[], ObjectID)> indexes = new();
            indexes.Add((PayloadHashKey(obj.ContainerId, obj.PayloadChecksum), obj.ObjectId));
            if (obj.ParentId is not null)
                indexes.Add((ParentKey(obj.Address.ContainerId, obj.ParentId), obj.ObjectId));
            if (obj.SplitId is not null)
                indexes.Add((SplitKey(obj.ContainerId, obj.SplitId), obj.ObjectId));
            return indexes;
        }

        private List<byte[]> FakeBucketTreeIndexes(FSObject obj)
        {
            List<byte[]> keys = new();
            keys.Add(OwnerKey(obj.Address, obj.OwnerId));
            foreach (var attr in obj.Attributes)
            {
                keys.Add(AttributeKey(obj.Address, attr));
            }
            return keys;
        }

        private int ParentLength(Address address)
        {
            var data = db.Get(ReadOptions.Default, ParentKey(address.ContainerId, address.ObjectId));
            if (data is null) return 0;
            return DecodeObjectIDList(data).Count;
        }
    }
}