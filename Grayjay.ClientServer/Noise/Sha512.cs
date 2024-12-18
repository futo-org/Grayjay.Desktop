using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Noise
{
	/// <summary>
	/// SHA-512 from <see href="https://nvlpubs.nist.gov/nistpubs/FIPS/NIST.FIPS.180-4.pdf">FIPS 180-4</see>.
	/// </summary>
	internal sealed class Sha512 : Hash
	{
		// typedef struct crypto_hash_sha512_state {
		//     uint64_t state[8];
		//     uint64_t count[2];
		//     uint8_t  buf[128];
		// } crypto_hash_sha512_state;

		private readonly IntPtr state = Marshal.AllocHGlobal(208);
		private bool disposed;

		public Sha512() => Reset();

		public int HashLen => 64;
		public int BlockLen => 128;

		public void AppendData(ReadOnlySpan<byte> data)
		{
			if (!data.IsEmpty)
			{
				Libsodium.crypto_hash_sha512_update(
					state,
					ref MemoryMarshal.GetReference(data),
					(ulong)data.Length
				);
			}
		}

		public void GetHashAndReset(Span<byte> hash)
		{
			Debug.Assert(hash.Length == HashLen);

			Libsodium.crypto_hash_sha512_final(
				state,
				ref MemoryMarshal.GetReference(hash)
			);

			Reset();
		}

		private void Reset()
		{
			Libsodium.crypto_hash_sha512_init(state);
		}

		public void Dispose()
		{
			if (!disposed)
			{
				Marshal.FreeHGlobal(state);
				disposed = true;
			}
		}
	}
}
