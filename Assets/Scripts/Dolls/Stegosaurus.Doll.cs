using System.Collections;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace Viva.Dolls
{
    public partial class Stegosaurus : MonoBehaviour
    {
        public class PackLosslessDataRequest
        {
            public string error;
            public readonly string name;
            public readonly byte[] targetData;
            public readonly Texture2D destination;
            public readonly bool useDollMask;
            public Texture2D result;

            public PackLosslessDataRequest(string _name, byte[] _targetData, Texture2D _destination, bool _useDollMask)
            {
                name = _name;
                targetData = _targetData;
                destination = _destination;
                useDollMask = _useDollMask;
            }
        }

        public class UnpackLosslessDataRequest
        {
            public readonly Texture2D cardTexture;
            public byte[] result;
            public string error = null;
            public readonly bool useDollMask;

            public UnpackLosslessDataRequest(Texture2D _cardTexture, bool _useDollMask)
            {
                cardTexture = _cardTexture;
                useDollMask = _useDollMask;
            }
        }

        [Header("Doll Card Masks")]
        [SerializeField] public Texture2D dollPackingMask;
        [SerializeField] public Texture2D dollCardBorderAlpha;
        [SerializeField] public Texture2D dollCardBorderGrey;

        [Header("Skin Card Masks")]
        [SerializeField] public Texture2D skinPackingMask;
        [SerializeField] public Texture2D skinCardBorderAlpha;
        [SerializeField] public Texture2D skinCardBorderGrey;

        private readonly int MAX_DOLL_CARD_BYTES = 2363271; // DO NOT CHANGE: (1256171*9 + 316693*24) / 8

        public IEnumerator ExecuteUnpackDoll(UnpackLosslessDataRequest request)
        {
            request.error = null;
            request.result = null;

            if (request.cardTexture == null)
            {
                request.error = "[Stegosaurus] Card texture cannot be null!";
                yield break;
            }

            Texture2D packingMask = request.useDollMask ? dollPackingMask : skinPackingMask;
            string error = null;
            bool valid = IsValidCardFormat(
                request.cardTexture,
                new TextureFormat[] { TextureFormat.ARGB32, TextureFormat.RGBA32, TextureFormat.RGB24 },
                ref error
            );
            valid &= IsValidCardFormat(packingMask, new TextureFormat[] { TextureFormat.Alpha8 }, ref error);

            if (!valid)
            {
                request.error = error;
                yield break;
            }

            byte[] targetData = request.cardTexture.GetRawTextureData();
            byte[] targetByteArray;
            if (request.cardTexture.format == TextureFormat.ARGB32)
            {
                byte[] rgbTargetData = new byte[(targetData.Length * 3) / 4];
                int rgbIndex = 0;
                int rgbaIndex = 0;
                while (rgbIndex < rgbTargetData.Length)
                {
                    rgbaIndex++; // skip alpha
                    rgbTargetData[rgbIndex++] = targetData[rgbaIndex++];
                    rgbTargetData[rgbIndex++] = targetData[rgbaIndex++];
                    rgbTargetData[rgbIndex++] = targetData[rgbaIndex++];
                }
                targetByteArray = rgbTargetData;
            }
            else if (request.cardTexture.format == TextureFormat.RGBA32)
            {
                byte[] rgbTargetData = new byte[(targetData.Length * 3) / 4];
                int rgbIndex = 0;
                int rgbaIndex = 0;
                while (rgbIndex < rgbTargetData.Length)
                {
                    rgbTargetData[rgbIndex++] = targetData[rgbaIndex++];
                    rgbTargetData[rgbIndex++] = targetData[rgbaIndex++];
                    rgbTargetData[rgbIndex++] = targetData[rgbaIndex++];
                    rgbaIndex++; // skip alpha
                }
                targetByteArray = rgbTargetData;
            }
            else
            {
                targetByteArray = targetData;
            }

            // Read last 4 bytes (before trailing 1 byte) as payload length metadata
            byte[] metadata = new byte[4];
            for (int i = 0; i < 4; i++)
            {
                metadata[i] = targetByteArray[targetByteArray.Length - 5 + i];
            }
            int payloadLength = System.BitConverter.ToInt32(metadata, 0);

            if (payloadLength <= 0 || payloadLength > MAX_DOLL_CARD_BYTES)
            {
                request.error = $"[Stegosaurus] Could not read doll card! Invalid payload length: {payloadLength}";
                yield break;
            }

            NativeArray<byte> nativeMaskData = ConvertToNativeArray(packingMask.GetRawTextureData());
            NativeArray<byte> nativeTargetArray = ConvertToNativeArray(targetByteArray);
            var unpackJob = new UnpackLosslessDataJob(payloadLength, nativeMaskData, nativeTargetArray);
            JobHandle jobHandle = unpackJob.Schedule();

            while (!jobHandle.IsCompleted)
            {
                yield return null;
            }
            jobHandle.Complete();

            byte[] resultData = new byte[payloadLength];
            unpackJob.resultData.CopyTo(resultData);

            nativeMaskData.Dispose();
            nativeTargetArray.Dispose();
            unpackJob.resultData.Dispose();

            request.result = resultData;
        }
    }
}
