using System;
using Imazen.WebP.Extern;

namespace RainbowAvatarBot {
	public static class WebPDecoder {
		public static unsafe void DecodeFromBytes(byte[] data, uint dataLength, byte[] output, uint outputLength, int w) {
			fixed (byte* dataptr = data)
			fixed (byte* outputptr = output) {
				DecodeFromPointer((IntPtr) dataptr, dataLength, (IntPtr) outputptr, outputLength, w);
			}
		}

		private static void DecodeFromPointer(IntPtr data, uint length, IntPtr output, uint outputLength, int w) {
			//Decode to surface
			IntPtr result = NativeMethods.WebPDecodeARGBInto(data, (UIntPtr) length, output, (UIntPtr) outputLength, w * 4);
			if (output != result) {
				throw new Exception("Failed to decode WebP image with error " + (long) result);
			}
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
