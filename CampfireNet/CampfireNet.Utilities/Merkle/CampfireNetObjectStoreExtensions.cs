using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace CampfireNet.Utilities.Merkle {
   public static class CampfireNetObjectStoreExtensions {
      public static async Task<MerkleNode> ReadMerkleNodeAsync(this ICampfireNetObjectStore store, string ns, string hash) {
         var tryReadResult = await store.TryReadAsync(ns, hash);
         var tryReadSucceeded = tryReadResult.Item1;
         if (!tryReadSucceeded) {
            return null;
         }

         var objectData = tryReadResult.Item2;
         using (var ms = new MemoryStream(objectData))
         using (var reader = new BinaryReader(ms)) {
            var result = new MerkleNode();
            result.TypeTag = (MerkleNodeTypeTag)reader.ReadUInt32();
            result.LeftHash = reader.ReadSha256Base64();
            result.RightHash = reader.ReadSha256Base64();
            result.Descendents = reader.ReadUInt32();
            result.Contents = reader.ReadBytes((int)reader.ReadUInt32());
            return result;
         }
      }

      public static async Task<string> WriteMerkleNodeAsync(this ICampfireNetObjectStore store, string ns, MerkleNode node) {
         using (var ms = new MemoryStream()) {
            using (var writer = new BinaryWriter(ms, Encoding.UTF8, true)) {
               writer.Write((uint)node.TypeTag);
               writer.WriteSha256Base64(node.LeftHash);
               writer.WriteSha256Base64(node.RightHash);
               writer.Write((uint)node.Descendents);
               if (node.Contents == null) {
                  writer.Write((uint)0);
               } else {
                  writer.Write((uint)node.Contents.Length);
                  writer.Write(node.Contents, 0, node.Contents.Length);
               }
            }

            var objectData = ms.GetBuffer();
            var length = (int)ms.Position;
            var hash = CampfireNetHash.ComputeSha256Base64(objectData, 0, length);
            await store.WriteAsync(ns, hash, objectData);

            var copy = await ReadMerkleNodeAsync(store, ns, hash);
            if (copy.TypeTag != node.TypeTag || copy.LeftHash != node.LeftHash || copy.RightHash != node.RightHash || copy.Descendents != node.Descendents) {
               throw new InvalidStateException();
            }
            return hash;
         }
      }
   }
}