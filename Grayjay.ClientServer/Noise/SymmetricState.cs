using System;
using System.Diagnostics;

namespace Noise
{
	/// <summary>
	/// A SymmetricState object contains a CipherState plus ck (a chaining
	/// key of HashLen bytes) and h (a hash output of HashLen bytes).
	/// </summary>
	internal sealed class SymmetricState<CipherType, DhType, HashType> : IDisposable
		where CipherType : Cipher, new()
		where DhType : Dh, new()
		where HashType : Hash, new()
	{
		private readonly Cipher cipher = new CipherType();
		private readonly DhType dh = new DhType();
		private readonly Hash hash = new HashType();
		private readonly Hkdf<HashType> hkdf = new Hkdf<HashType>();
		private readonly CipherState<CipherType> state = new CipherState<CipherType>();
		private readonly byte[] ck;
		private readonly byte[] h;
		private bool disposed;

		/// <summary>
		/// Initializes a new SymmetricState with an
		/// arbitrary-length protocolName byte sequence.
		/// </summary>
		public SymmetricState(ReadOnlySpan<byte> protocolName)
		{
			int length = hash.HashLen;

			ck = new byte[length];
			h = new byte[length];

			if (protocolName.Length <= length)
			{
				protocolName.CopyTo(h);
			}
			else
			{
				hash.AppendData(protocolName);
				hash.GetHashAndReset(h);
			}

			Array.Copy(h, ck, length);
		}

		/// <summary>
		/// Sets ck, tempK = HKDF(ck, inputKeyMaterial, 2).
		/// If HashLen is 64, then truncates tempK to 32 bytes.
		/// Calls InitializeKey(tempK).
		/// </summary>
		public void MixKey(ReadOnlySpan<byte> inputKeyMaterial)
		{
			int length = inputKeyMaterial.Length;
			Debug.Assert(length == 0 || length == Aead.KeySize || length == dh.DhLen);

			Span<byte> output = stackalloc byte[2 * hash.HashLen];
			hkdf.ExtractAndExpand2(ck, inputKeyMaterial, output);

			output.Slice(0, hash.HashLen).CopyTo(ck);

			var tempK = output.Slice(hash.HashLen, Aead.KeySize);
			state.InitializeKey(tempK);
		}

		/// <summary>
		/// Sets h = HASH(h || data).
		/// </summary>
		public void MixHash(ReadOnlySpan<byte> data)
		{
			hash.AppendData(h);
			hash.AppendData(data);
			hash.GetHashAndReset(h);
		}

		/// <summary>
		/// Sets ck, tempH, tempK = HKDF(ck, inputKeyMaterial, 3).
		/// Calls MixHash(tempH).
		/// If HashLen is 64, then truncates tempK to 32 bytes.
		/// Calls InitializeKey(tempK).
		/// </summary>
		public void MixKeyAndHash(ReadOnlySpan<byte> inputKeyMaterial)
		{
			int length = inputKeyMaterial.Length;
			Debug.Assert(length == 0 || length == Aead.KeySize || length == dh.DhLen);

			Span<byte> output = stackalloc byte[3 * hash.HashLen];
			hkdf.ExtractAndExpand3(ck, inputKeyMaterial, output);

			output.Slice(0, hash.HashLen).CopyTo(ck);

			var tempH = output.Slice(hash.HashLen, hash.HashLen);
			var tempK = output.Slice(2 * hash.HashLen, Aead.KeySize);

			MixHash(tempH);
			state.InitializeKey(tempK);
		}

		/// <summary>
		/// Returns h. This function should only be called at the end of
		/// a handshake, i.e. after the Split() function has been called.
		/// </summary>
		public byte[] GetHandshakeHash()
		{
			return h;
		}

		/// <summary>
		/// Sets ciphertext = EncryptWithAd(h, plaintext),
		/// calls MixHash(ciphertext), and returns ciphertext.
		/// </summary>
		public int EncryptAndHash(ReadOnlySpan<byte> plaintext, Span<byte> ciphertext)
		{
			int bytesWritten = state.EncryptWithAd(h, plaintext, ciphertext);
			MixHash(ciphertext.Slice(0, bytesWritten));

			return bytesWritten;
		}

		/// <summary>
		/// Sets plaintext = DecryptWithAd(h, ciphertext),
		/// calls MixHash(ciphertext), and returns plaintext.
		/// </summary>
		public int DecryptAndHash(ReadOnlySpan<byte> ciphertext, Span<byte> plaintext)
		{
			var bytesRead = state.DecryptWithAd(h, ciphertext, plaintext);
			MixHash(ciphertext);

			return bytesRead;
		}

		/// <summary>
		/// Returns a pair of CipherState objects for encrypting transport messages.
		/// </summary>
		public (CipherState<CipherType> c1, CipherState<CipherType> c2) Split()
		{
			Span<byte> output = stackalloc byte[2 * hash.HashLen];
			hkdf.ExtractAndExpand2(ck, null, output);

			var tempK1 = output.Slice(0, Aead.KeySize);
			var tempK2 = output.Slice(hash.HashLen, Aead.KeySize);

			var c1 = new CipherState<CipherType>();
			var c2 = new CipherState<CipherType>();

			c1.InitializeKey(tempK1);
			c2.InitializeKey(tempK2);

			return (c1, c2);
		}

		/// <summary>
		/// Returns true if k is non-empty, false otherwise.
		/// </summary>
		public bool HasKey()
		{
			return state.HasKey();
		}

		public void Dispose()
		{
			if (!disposed)
			{
				hash.Dispose();
				hkdf.Dispose();
				state.Dispose();
				Utilities.ZeroMemory(ck);
				disposed = true;
			}
		}
	}
}
