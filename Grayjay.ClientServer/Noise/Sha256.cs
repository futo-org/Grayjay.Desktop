using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Noise
{
	/// <summary>
	/// SHA-256 from <see href="https://nvlpubs.nist.gov/nistpubs/FIPS/NIST.FIPS.180-4.pdf">FIPS 180-4</see>.
	/// </summary>
	internal sealed class Sha256 : Hash
	{
		// typedef struct crypto_hash_sha256_state {
		//     uint32_t state[8];
		//     uint64_t count;
		//     uint8_t  buf[64];
		// } crypto_hash_sha256_state;

		private readonly IntPtr state = Marshal.AllocHGlobal(104);
		private bool disposed;

		public Sha256() => Reset();

		public int HashLen => 32;
		public int BlockLen => 64;

		public void AppendData(ReadOnlySpan<byte> data)
		{
			if (!data.IsEmpty)
			{
				Libsodium.crypto_hash_sha256_update(
					state,
					ref MemoryMarshal.GetReference(data),
					(ulong)data.Length
				);
			}
		}

		public void GetHashAndReset(Span<byte> hash)
		{
			Debug.Assert(hash.Length == HashLen);

			Libsodium.crypto_hash_sha256_final(
				state,
				ref MemoryMarshal.GetReference(hash)
			);

			Reset();
		}

		private void Reset()
		{
			Libsodium.crypto_hash_sha256_init(state);
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
