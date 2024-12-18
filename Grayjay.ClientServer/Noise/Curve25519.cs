using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Noise
{
	/// <summary>
	/// The Curve25519 DH function (aka "X25519" in
	/// <see href="https://tools.ietf.org/html/rfc7748">RFC 7748</see>).
	/// </summary>
	internal sealed class Curve25519 : Dh
	{
		public int DhLen => Libsodium.crypto_scalarmult_curve25519_SCALARBYTES;

		public KeyPair GenerateKeyPair()
		{
			var privateKey = Utilities.GetRandomBytes(DhLen);
			var publicKey = new byte[DhLen];

			Libsodium.crypto_scalarmult_curve25519_base(publicKey, privateKey);

			return new KeyPair(privateKey, publicKey);
		}

		public KeyPair GenerateKeyPair(ReadOnlySpan<byte> privateKey)
		{
			Debug.Assert(privateKey.Length == DhLen);

			var privateKeyCopy = privateKey.ToArray();
			var publicKey = new byte[DhLen];

			Libsodium.crypto_scalarmult_curve25519_base(publicKey, privateKeyCopy);

			return new KeyPair(privateKeyCopy, publicKey);
		}

		public void Dh(KeyPair keyPair, ReadOnlySpan<byte> publicKey, Span<byte> sharedKey)
		{
			Debug.Assert(keyPair.PrivateKey != null && keyPair.PrivateKey.Length == DhLen);
			Debug.Assert(publicKey.Length == DhLen);
			Debug.Assert(sharedKey.Length == DhLen);

			Libsodium.crypto_scalarmult_curve25519(
				ref MemoryMarshal.GetReference(sharedKey),
				ref MemoryMarshal.GetReference(keyPair.PrivateKey.AsSpan()),
				ref MemoryMarshal.GetReference(publicKey)
			);
		}
	}
}
