using Neo.IO;
using Neo.IO.Data.LevelDB;
using System.IO;
using System.Linq;
using static Neo.FileStorage.Utils.locode.Column;

namespace Neo.FileStorage.Utils.locode.db
{
    public static class AirportsDBHelper
    {
        private const byte PreAirports = 0x00;

        public static void InitAirports(this DB _db) {

        }

        public static void InitCountries(this DB _db)
        {

        }

        public static void ScanWords(this DB _db)
        {

        }


        public static (Key,Record) Get(this DB _db, LOCODE lc) {
            Key key = new Key(lc);
            Record record = _db.Get(ReadOptions.Default, key.ToArray())?.AsSerializable<Record>();
            return (key,record);
        }

        public static void Put(this DB _db, LOCODE lc,Record record) {
            //_db.Put(WriteOptions.Default, Key(PreLocode, new Key(lc)), record.ToArray());
        }

        private static byte[] Key(byte prefix, ISerializable key)
        {
            byte[] buffer = new byte[key.Size + 1];
            using (MemoryStream ms = new MemoryStream(buffer, true))
            using (BinaryWriter writer = new BinaryWriter(ms))
            {
                writer.Write(prefix);
                key.Serialize(writer);
            }
            return buffer;
        }
    }
}