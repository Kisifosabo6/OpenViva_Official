using System.Collections;
using UnityEngine;

namespace Viva.Dolls
{
    /// <summary>
    /// Steganography encoder/decoder for Doll character and skin cards.
    /// Humorously named Stegosaurus.
    /// </summary>
    public partial class Stegosaurus : MonoBehaviour
    {
        public abstract class UnpackedCard
        {
            public enum CardType
            {
                // HARDCODED INTO PNGS DO NOT CHANGE ORDER
                CUSTOM_CLOTHING_TEXTURE,
                CUSTOM_DOLL_MODEL
            }

            private readonly byte version;
            private readonly CardType type;

            public byte GetVersion() => version;
            public CardType GetCardType() => type;

            protected UnpackedCard(byte _version, CardType _type)
            {
                version = _version;
                type = _type;
            }

            protected abstract void OnDestroyAssociated();

            public void DestroyAssociated() => OnDestroyAssociated();
        }

        public static Stegosaurus Main { get; private set; }
        public static readonly int PACK_SIZE = 1024;
        public static readonly int THUMBNAIL_SCALE = 8;
        public static readonly int CARD_HEIGHT = (int)(PACK_SIZE * 1.5f);

        private void Awake()
        {
            if (Main != null && Main != this)
            {
                Debug.LogWarning("[Stegosaurus] Multiple Stegosaurus instances detected; overwriting Main reference.");
            }
            Main = this;
        }

        private static bool IsValidCardFormat(Texture2D texture, TextureFormat[] formats, ref string error)
        {
            bool valid = true;
            if (texture == null)
            {
                error += "\n[Stegosaurus] Texture must not be null!";
                return false;
            }
            if (texture.width != PACK_SIZE || texture.height != CARD_HEIGHT)
            {
                error += $"\n[Stegosaurus] {texture.name} texture must be {PACK_SIZE}x{CARD_HEIGHT}, got {texture.width}x{texture.height}";
                valid = false;
            }
            if (formats != null)
            {
                bool validFormat = false;
                foreach (TextureFormat format in formats)
                {
                    if (texture.format == format)
                    {
                        validFormat = true;
                        break;
                    }
                }
                if (!validFormat)
                {
                    error += $"\n[Stegosaurus] {texture.name} wrong color format ({texture.format})!";
                    valid = false;
                }
            }
            if (texture.mipmapCount != 1)
            {
                error += $"\n[Stegosaurus] {texture.name} must not have mipmaps (mipmapCount={texture.mipmapCount})!";
                valid = false;
            }
            return valid;
        }

        private static bool UsesCompression(byte b) => b > 128;

        private static byte ZeroLast3Bits(byte b) => (byte)(b & 0xF8);

        private static byte Extract8Bits(BitArray ba, int bitIndex)
        {
            int val = System.Convert.ToInt32(ba.Get(bitIndex + 7)) << 7;
            val += System.Convert.ToInt32(ba.Get(bitIndex + 6)) << 6;
            val += System.Convert.ToInt32(ba.Get(bitIndex + 5)) << 5;
            val += System.Convert.ToInt32(ba.Get(bitIndex + 4)) << 4;
            val += System.Convert.ToInt32(ba.Get(bitIndex + 3)) << 3;
            val += System.Convert.ToInt32(ba.Get(bitIndex + 2)) << 2;
            val += System.Convert.ToInt32(ba.Get(bitIndex + 1)) << 1;
            val += System.Convert.ToInt32(ba.Get(bitIndex));
            return (byte)val;
        }

        private static byte Extract3Bits(BitArray ba, int bitIndex)
        {
            int val = System.Convert.ToInt32(ba.Get(bitIndex + 2)) << 2;
            val += System.Convert.ToInt32(ba.Get(bitIndex + 1)) << 1;
            val += System.Convert.ToInt32(ba.Get(bitIndex));
            return (byte)val;
        }

        private Texture2D CreateCard(byte[] rawData, string cardName)
        {
            Texture2D card = new Texture2D(PACK_SIZE, CARD_HEIGHT, TextureFormat.RGB24, false, true);
            card.name = cardName;
            card.LoadRawTextureData(rawData);
            card.Apply();
            card.filterMode = FilterMode.Point;
            card.wrapMode = TextureWrapMode.Clamp;
            return card;
        }

        public static string EnsureFolderExistence(string folder)
        {
            string directory = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Application.dataPath), folder);
            if (!System.IO.Directory.Exists(directory))
            {
                System.IO.Directory.CreateDirectory(directory);
                Debug.Log($"[Stegosaurus] Created directory: {directory}");
            }
            return directory;
        }

        public static void SaveTexture(Texture2D image, string folder)
        {
            string path = System.IO.Path.Combine(EnsureFolderExistence(folder), image.name + ".png");
            System.IO.File.WriteAllBytes(path, image.EncodeToPNG());
        }

        public static Texture2D AttemptSaveCardThumbnail(Texture2D target, string folder)
        {
            string error = null;
            if (!IsValidCardFormat(target, null, ref error))
            {
                Debug.LogError("[Stegosaurus] Could not save thumbnail! " + error);
                return null;
            }

            byte[] rgbData = new byte[PACK_SIZE * CARD_HEIGHT * 3];
            if (target.format == TextureFormat.ARGB32 || target.format == TextureFormat.RGBA32)
            {
                byte[] rgbaTargetData = target.GetRawTextureData();
                int rgbIndex = 0;
                int rgbaIndex = 0;
                bool isArgb = target.format == TextureFormat.ARGB32;
                while (rgbIndex < rgbData.Length)
                {
                    if (isArgb)
                    {
                        rgbaIndex++; // skip alpha first
                        rgbData[rgbIndex++] = rgbaTargetData[rgbaIndex++];
                        rgbData[rgbIndex++] = rgbaTargetData[rgbaIndex++];
                        rgbData[rgbIndex++] = rgbaTargetData[rgbaIndex++];
                    }
                    else
                    {
                        rgbData[rgbIndex++] = rgbaTargetData[rgbaIndex++];
                        rgbData[rgbIndex++] = rgbaTargetData[rgbaIndex++];
                        rgbData[rgbIndex++] = rgbaTargetData[rgbaIndex++];
                        rgbaIndex++; // skip alpha last
                    }
                }
            }
            else if (target.format == TextureFormat.RGB24)
            {
                rgbData = target.GetRawTextureData();
            }
            else
            {
                Debug.LogError($"[Stegosaurus] Texture format {target.format} is not valid for thumbnail generation.");
                return null;
            }

            int thumbnailWidth = PACK_SIZE / THUMBNAIL_SCALE;
            int thumbnailHeight = CARD_HEIGHT / THUMBNAIL_SCALE;
            byte[] thumbnailData = new byte[thumbnailWidth * thumbnailHeight * 3];
            int sourceByte = 0;
            int targetByte = 0;
            int columnCount = 0;

            for (int i = 0; i < thumbnailWidth * thumbnailHeight; i++)
            {
                thumbnailData[targetByte++] = rgbData[sourceByte++];
                thumbnailData[targetByte++] = rgbData[sourceByte++];
                thumbnailData[targetByte++] = rgbData[sourceByte++];
                sourceByte += 3 * (THUMBNAIL_SCALE - 1);
                if (++columnCount >= thumbnailWidth)
                {
                    columnCount = 0;
                    sourceByte += (THUMBNAIL_SCALE - 1) * (thumbnailWidth * THUMBNAIL_SCALE * 3);
                }
            }

            Texture2D thumbnail = new Texture2D(thumbnailWidth, thumbnailHeight, TextureFormat.RGB24, false, false);
            thumbnail.LoadRawTextureData(thumbnailData);
            thumbnail.Apply();
            thumbnail.name = target.name;
            SaveTexture(thumbnail, folder);
            return thumbnail;
        }
    }
}
