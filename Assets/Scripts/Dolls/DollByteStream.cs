using System.IO;
using System.Text;
using UnityEngine;

namespace Viva.Dolls
{
    public static class DollUtilities
    {
        public static string UTF8ByteArrayToString(byte[] bytes, int index, int count)
        {
            return Encoding.UTF8.GetString(bytes, index, count);
        }

        public static byte[] UTF8ToByteArray(string str)
        {
            return Encoding.UTF8.GetBytes(str);
        }

        public static float EaseOutQuad(float t)
        {
            return 1f - (1f - t) * (1f - t);
        }

        public static Vector3 FlatForward(Vector3 forward)
        {
            forward.y = 0f;
            return forward.sqrMagnitude > 0.0001f ? forward.normalized : Vector3.forward;
        }

        public static Transform FindTransformRecursive(Transform parent, string targetName)
        {
            if (parent == null || string.IsNullOrEmpty(targetName)) return null;
            if (parent.name == targetName) return parent;
            for (int i = 0; i < parent.childCount; i++)
            {
                Transform found = FindTransformRecursive(parent.GetChild(i), targetName);
                if (found != null) return found;
            }
            return null;
        }

        public static T EnsureComponent<T>(GameObject go) where T : Component
        {
            if (go == null) return null;
            T comp = go.GetComponent<T>();
            if (comp == null) comp = go.AddComponent<T>();
            return comp;
        }
    }

    public class DollByteStreamReader
    {
        private readonly byte[] data;
        public int index = 0;

        public DollByteStreamReader(string filepath)
        {
            data = File.ReadAllBytes(filepath);
        }

        public DollByteStreamReader(byte[] _data)
        {
            data = _data ?? throw new System.Exception("DollByteStreamReader received a null array!");
        }

        public void Skip(int indices) => index += indices;

        public int ReadSigned4ByteInt()
        {
            int val = System.BitConverter.ToInt32(data, index);
            index += 4;
            return val;
        }

        public byte ReadUnsigned1ByteInt() => data[index++];

        public ushort ReadUnsigned2ByteInt()
        {
            ushort v = System.BitConverter.ToUInt16(data, index);
            index += 2;
            return v;
        }

        public float ReadFloat()
        {
            float v = System.BitConverter.ToSingle(data, index);
            index += 4;
            return v;
        }

        public float Read1ByteNormalFloat() => ((float)data[index++] / 255.0f) * 2.0f - 1.0f;

        public float ReadUnsigned1ByteNormalFloat() => (float)data[index++] / 255.0f;

        public string ReadUTF8String(int length)
        {
            string result = DollUtilities.UTF8ByteArrayToString(data, index, length);
            index += length;
            return result;
        }

        public byte[] ReadBytes(int length)
        {
            byte[] result = new byte[length];
            System.Array.Copy(data, index, result, 0, length);
            index += length;
            return result;
        }
    }

    public class DollByteStreamWriter
    {
        private byte[] data;
        public int index = 0;
        private readonly int growSize;

        public DollByteStreamWriter(int startSize, int _growSize)
        {
            data = new byte[startSize];
            growSize = _growSize;
        }

        private void Allocate(int amount)
        {
            if (index + amount >= data.Length)
            {
                byte[] newData = new byte[data.Length + growSize + amount];
                System.Array.Copy(data, newData, index);
                data = newData;
            }
        }

        public void WriteByte(byte b)
        {
            Allocate(1);
            data[index++] = b;
        }

        public void Write4ByteFloat(float f)
        {
            Allocate(4);
            byte[] floatBytes = System.BitConverter.GetBytes(f);
            System.Array.Copy(floatBytes, 0, data, index, 4);
            index += 4;
        }

        public void WriteSigned4ByteInt(int value)
        {
            Allocate(4);
            byte[] intBytes = System.BitConverter.GetBytes(value);
            System.Array.Copy(intBytes, 0, data, index, 4);
            index += 4;
        }

        public void WriteByteArray(byte[] array)
        {
            Allocate(array.Length + 4);
            byte[] lengthArray = System.BitConverter.GetBytes(array.Length);
            System.Array.Copy(lengthArray, 0, data, index, 4);
            index += 4;
            System.Array.Copy(array, 0, data, index, array.Length);
            index += array.Length;
        }

        public void WriteNormal1ByteFloat(float f)
        {
            WriteByte((byte)(Mathf.RoundToInt(f * 128.0f) + 127));
        }

        public void WriteUnsignedNormal1ByteFloat(float f)
        {
            WriteByte((byte)Mathf.RoundToInt(Mathf.Min(255, f * 256.0f)));
        }

        public void WriteUTF8String(string s)
        {
            if (s.Length > 255)
            {
                Debug.LogError("[DollByteStream] Cannot save string longer than 255 chars!");
                return;
            }
            byte[] sb = DollUtilities.UTF8ToByteArray(s);
            Allocate(sb.Length + 1);
            data[index++] = (byte)sb.Length;
            System.Array.Copy(sb, 0, data, index, sb.Length);
            index += sb.Length;
        }

        public byte[] ToArray()
        {
            byte[] final = new byte[index];
            System.Array.Copy(data, final, index);
            return final;
        }
    }
}
