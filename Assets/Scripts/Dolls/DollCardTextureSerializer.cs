using UnityEngine;

namespace Viva.Dolls
{
    public class DollCardTextureSerializer
    {
        public class CardTextureFormat
        {
            public readonly int requiredSize;
            public readonly bool useAlpha;

            public CardTextureFormat(int _requiredSize, bool _useAlpha)
            {
                requiredSize = _requiredSize;
                useAlpha = _useAlpha;
            }
        }

        private readonly CardTextureFormat[] formats;
        public string error;

        public DollCardTextureSerializer(CardTextureFormat[] _formats)
        {
            formats = _formats;
        }

        public Texture2D[] Deserialize(byte[] data)
        {
            error = null;
            if (formats == null || data == null)
            {
                error = "[DollCardTextureSerializer] One or more parameters are null!";
                return null;
            }
            int byteIndex = 0;
            Texture2D[] textures = new Texture2D[data[byteIndex++]];
            if (formats.Length != textures.Length)
            {
                error = $"[DollCardTextureSerializer] Format count mismatch: expected {formats.Length}, got {textures.Length}";
                return null;
            }

            for (int i = 0; i < textures.Length; i++)
            {
                CardTextureFormat serializeFormat = formats[i];
                byte[] dataLengthBytes = new byte[]
                {
                    data[byteIndex++],
                    data[byteIndex++],
                    data[byteIndex++],
                    data[byteIndex++]
                };

                int dataLength = System.BitConverter.ToInt32(dataLengthBytes, 0);
                if (dataLength == 0)
                {
                    continue;
                }

                TextureFormat format = serializeFormat.useAlpha ? TextureFormat.DXT5 : TextureFormat.DXT1;
                Texture2D texture = new Texture2D(serializeFormat.requiredSize, serializeFormat.requiredSize, format, false, false);
                texture.wrapMode = TextureWrapMode.Clamp;

                byte[] textureData = new byte[dataLength];
                System.Array.Copy(data, byteIndex, textureData, 0, dataLength);
                byteIndex += dataLength;

                texture.LoadRawTextureData(textureData);
                texture.Apply();
                textures[i] = texture;
            }
            return textures;
        }
    }
}
