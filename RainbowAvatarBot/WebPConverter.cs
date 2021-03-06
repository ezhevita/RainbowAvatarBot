using System;
using System.Buffers;
using System.Runtime.InteropServices;
using Imazen.WebP.Extern;

namespace RainbowAvatarBot {
	public static class WebPConverter {
		public static unsafe void DecodeFromBytes(byte[] data, uint dataLength, byte[] output, uint outputLength, int w) {
			fixed (byte* dataptr = data, outputptr = output) {
				DecodeFromPointer((IntPtr) dataptr, dataLength, (IntPtr) outputptr, outputLength, w);
			}
		}

		private static void DecodeFromPointer(IntPtr data, uint length, IntPtr output, uint outputLength, int w) {
			//Decode to surface
			IntPtr result = NativeMethods.WebPDecodeRGBAInto(data, (UIntPtr) length, output, (UIntPtr) outputLength, w * 4);
			if (output != result) {
				throw new Exception("Failed to decode WebP image with error " + (long) result);
			}
		}

		public static unsafe byte[] EncodeFromBytes(byte[] data, int w, int h, out uint length) {
			fixed (byte* dataptr = data) {
				IntPtr result = IntPtr.Zero;
				try {
					EncodeFromPointer((IntPtr) dataptr, w, h, ref result, out length);
					if (length == 0) {
						throw new Exception("WebP encode failed!");
					}

					byte[] output = ArrayPool<byte>.Shared.Rent((int) length);
					Marshal.Copy(result, output, 0, (int) length);

					return output;
				} finally {
					NativeMethods.WebPSafeFree(result);
				}
			}
		}

		private static void EncodeFromPointer(IntPtr data, int w, int h, ref IntPtr result, out uint length) {
			length = (uint) NativeMethods.WebPEncodeLosslessRGBA(data, w, h, w * 4, ref result);
		}

		public static unsafe (int w, int h) GetWebPInfo(byte[] data, uint length) {
			fixed (byte* dataptr = data) {
				int w = 0, h = 0;
				if (NativeMethods.WebPGetInfo((IntPtr) dataptr, (UIntPtr) length, ref w, ref h) == 0) {
					throw new Exception("Invalid WebP header detected");
				}

				return (w, h);
			}
		}
	}
}
