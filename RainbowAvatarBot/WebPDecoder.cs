using System;
using Imazen.WebP.Extern;

namespace RainbowAvatarBot {
	public static class WebPDecoder {
		public static unsafe (int w, int h) GetWebPInfo(byte[] data) {
			fixed (byte* dataptr = data) {
				int w = 0, h = 0;
				if (NativeMethods.WebPGetInfo((IntPtr) dataptr, (UIntPtr) data.Length, ref w, ref h) == 0) {
					throw new Exception("Invalid WebP header detected");
				}

				return (w, h);
			}
		}
		
		public static unsafe byte[] DecodeFromBytes(byte[] data, int w, int h) {
			fixed (byte* dataptr = data) {
				return DecodeFromPointer((IntPtr) dataptr, data.Length, w, h);
			}
		}

		private static unsafe byte[] DecodeFromPointer(IntPtr data, long length, int w, int h) {
			byte[] output = new byte[w * h * 4];
			fixed (byte* outputptr = output) {
				var pointer = (IntPtr) outputptr;
				//Decode to surface
				IntPtr result = NativeMethods.WebPDecodeARGBInto(data, (UIntPtr) length, pointer, (UIntPtr) output.Length, w * 4);
				if ((IntPtr) outputptr != result) {
					throw new Exception("Failed to decode WebP image with error " + (long) result);
				}
			}

			return output;
		}
	}
}
