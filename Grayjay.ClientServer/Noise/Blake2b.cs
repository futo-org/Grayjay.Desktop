using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Noise
{
	/// <summary>
	/// BLAKE2b from <see href="https://tools.ietf.org/html/rfc7693">RFC 7693</see>
	/// with digest length 64.
	/// </summary>
	internal sealed class Blake2b : Hash
	{
		private readonly IntPtr raw;
		private readonly IntPtr aligned;
		private bool disposed;

		public Blake2b()
		{
			// The crypto_generichash_state structure is packed and its length is
			// either 357 or 361 bytes. For this reason, padding must be added in
			// order to ensure proper alignment.

			int size = 361;
			int alignment = 64;

			raw = Marshal.AllocHGlobal(size + alignment - 1);
			aligned = Utilities.Align(raw, alignment);

			Reset();
		}

		public int HashLen => 64;
		public int BlockLen => 128;

		public void AppendData(ReadOnlySpan<byte> data)
		{
			if (!data.IsEmpty)
			{
				Libsodium.crypto_generichash_blake2b_update(
					aligned,
					ref MemoryMarshal.GetReference(data),
					(ulong)data.Length
				);
			}
		}

		public void GetHashAndReset(Span<byte> hash)
		{
			Debug.Assert(hash.Length == HashLen);

			Libsodium.crypto_generichash_blake2b_final(
				aligned,
				ref MemoryMarshal.GetReference(hash),
				(UIntPtr)hash.Length
			);

			Reset();
		}

		private void Reset()
		{
			Libsodium.crypto_generichash_blake2b_init(aligned, null, UIntPtr.Zero, (UIntPtr)HashLen);
		}

		public void Dispose()
		{
			if (!disposed)
			{
				Marshal.FreeHGlobal(raw);
				disposed = true;
			}
		}
	}
}
