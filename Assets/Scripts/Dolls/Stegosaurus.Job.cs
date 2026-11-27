using System.Collections;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace Viva.Dolls
{
    public partial class Stegosaurus : MonoBehaviour
    {
        private struct UnpackLosslessDataJob : IJob
        {
            private readonly int payloadLength;
            public readonly NativeArray<byte> nativeMaskData;
            public readonly NativeArray<byte> nativeTargetByteArray;
            public NativeArray<byte> resultData;

            public UnpackLosslessDataJob(int _payloadLength, NativeArray<byte> _nativeMaskData, NativeArray<byte> _nativeTargetByteArray)
            {
                payloadLength = _payloadLength;
                nativeMaskData = _nativeMaskData;
                nativeTargetByteArray = _nativeTargetByteArray;
                resultData = new NativeArray<byte>(
                    payloadLength,
                    Allocator.Persistent,
                    NativeArrayOptions.UninitializedMemory
                );
            }

            public void Execute()
            {
                byte[] targetByteArray = new byte[nativeTargetByteArray.Length];
                for (int i = 0; i < targetByteArray.Length; i++)
                {
                    targetByteArray[i] = nativeTargetByteArray[i];
                }
                BitArray targetBitArray = new BitArray(targetByteArray);
                BitArray destBitArray = new BitArray((payloadLength + 3) * 8);

                int destBitIndex = 0;
                int targetBitIndex = 0;
                for (int i = 0; i < nativeMaskData.Length; i++)
                {
                    if (destBitIndex >= payloadLength * 8)
                    {
                        break;
                    }

                    if (!UsesCompression(nativeMaskData[i]))
                    {
                        for (int j = 0; j < 3; j++)
                        {
                            for (int b = 0; b < 8; b++)
                            {
                                destBitArray.Set(destBitIndex++, targetBitArray.Get(targetBitIndex++));
                            }
                        }
                    }
                    else
                    {
                        for (int j = 0; j < 3; j++)
                        {
                            destBitArray.Set(destBitIndex++, targetBitArray.Get(targetBitIndex++));
                            destBitArray.Set(destBitIndex++, targetBitArray.Get(targetBitIndex++));
                            destBitArray.Set(destBitIndex++, targetBitArray.Get(targetBitIndex++));
                            targetBitIndex += 5;
                        }
                    }
                }

                byte[] tempResultPadded = new byte[payloadLength + 3];
                destBitArray.CopyTo(tempResultPadded, 0);

                byte[] resultDataByte = new byte[payloadLength];
                System.Array.Copy(tempResultPadded, resultDataByte, payloadLength);
                resultData.CopyFrom(resultDataByte);

                if (destBitIndex - payloadLength * 8 > 24)
                {
                    Debug.LogError("[Stegosaurus] Unpack mismatch: wrote extra bits +" + (destBitIndex - payloadLength * 8));
                }
            }
        }
    }
}
