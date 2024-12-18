using System;
using System.Diagnostics;

namespace Noise
{
	/// <summary>
	/// A pair of <see href="https://noiseprotocol.org/noise.html#the-cipherstate-object">CipherState</see>
	/// objects for encrypting transport messages.
	/// </summary>
	public interface Transport : IDisposable
	{
		/// <summary>
		/// Gets a value indicating whether the <see cref="Transport"/> is one-way.
		/// </summary>
		/// <returns>True if the <see cref="Transport"/> is one-way, false otherwise.</returns>
		/// <exception cref="ObjectDisposedException">
		/// Thrown if the current instance has already been disposed.
		/// </exception>
		bool IsOneWay { get; }

		/// <summary>
		/// Encrypts the <paramref name="payload"/> and writes the result into <paramref name="messageBuffer"/>.
		/// </summary>
		/// <param name="payload">The payload to encrypt.</param>
		/// <param name="messageBuffer">The buffer for the encrypted message.</param>
		/// <returns>The ciphertext size in bytes.</returns>
		/// <exception cref="ObjectDisposedException">
		/// Thrown if the current instance has already been disposed.
		/// </exception>
		/// <exception cref="InvalidOperationException">
		/// Thrown if the responder has attempted to write a message to a one-way stream.
		/// </exception>
		/// <exception cref="ArgumentException">
		/// Thrown if the encrypted payload was greater than <see cref="Protocol.MaxMessageLength"/>
		/// bytes in length, or if the output buffer did not have enough space to hold the ciphertext.
		/// </exception>
		int WriteMessage(ReadOnlySpan<byte> payload, Span<byte> messageBuffer);

		/// <summary>
		/// Decrypts the <paramref name="message"/> and writes the result into <paramref name="payloadBuffer"/>.
		/// </summary>
		/// <param name="message">The message to decrypt.</param>
		/// <param name="payloadBuffer">The buffer for the decrypted payload.</param>
		/// <returns>The plaintext size in bytes.</returns>
		/// <exception cref="ObjectDisposedException">
		/// Thrown if the current instance has already been disposed.
		/// </exception>
		/// <exception cref="InvalidOperationException">
		/// Thrown if the initiator has attempted to read a message from a one-way stream.
		/// </exception>
		/// <exception cref="ArgumentException">
		/// Thrown if the message was greater than <see cref="Protocol.MaxMessageLength"/>
		/// bytes in length, or if the output buffer did not have enough space to hold the plaintext.
		/// </exception>
		/// <exception cref="System.Security.Cryptography.CryptographicException">
		/// Thrown if the decryption of the message has failed.
		/// </exception>
		int ReadMessage(ReadOnlySpan<byte> message, Span<byte> payloadBuffer);

		/// <summary>
		/// Updates the symmetric key used to encrypt transport messages from
		/// initiator to responder using a one-way function, so that a compromise
		/// of keys will not decrypt older messages.
		/// </summary>
		/// <exception cref="ObjectDisposedException">
		/// Thrown if the current instance has already been disposed.
		/// </exception>
		void RekeyInitiatorToResponder();

		/// <summary>
		/// Updates the symmetric key used to encrypt transport messages from
		/// responder to initiator using a one-way function, so that a compromise
		/// of keys will not decrypt older messages.
		/// </summary>
		/// <exception cref="ObjectDisposedException">
		/// Thrown if the current instance has already been disposed.
		/// </exception>
		/// <exception cref="InvalidOperationException">
		/// Thrown if the current instance is a one-way stream.
		/// </exception>
		void RekeyResponderToInitiator();
	}

	internal sealed class Transport<CipherType> : Transport where CipherType : Cipher, new()
	{
		private readonly bool initiator;
		private readonly CipherState<CipherType> c1;
		private readonly CipherState<CipherType> c2;
		private bool disposed;

		public Transport(bool initiator, CipherState<CipherType> c1, CipherState<CipherType> c2)
		{
			Exceptions.ThrowIfNull(c1, nameof(c1));

			this.initiator = initiator;
			this.c1 = c1;
			this.c2 = c2;
		}

		public bool IsOneWay
		{
			get
			{
				Exceptions.ThrowIfDisposed(disposed, nameof(Transport<CipherType>));
				return c2 == null;
			}
		}

		public int WriteMessage(ReadOnlySpan<byte> payload, Span<byte> messageBuffer)
		{
			Exceptions.ThrowIfDisposed(disposed, nameof(Transport<CipherType>));

			if (!initiator && IsOneWay)
			{
				throw new InvalidOperationException("Responder cannot write messages to a one-way stream.");
			}

			if (payload.Length + Aead.TagSize > Protocol.MaxMessageLength)
			{
				throw new ArgumentException($"Noise message must be less than or equal to {Protocol.MaxMessageLength} bytes in length.");
			}

			if (payload.Length + Aead.TagSize > messageBuffer.Length)
			{
				throw new ArgumentException("Message buffer does not have enough space to hold the ciphertext.");
			}

			var cipher = initiator ? c1 : c2;
			Debug.Assert(cipher.HasKey());

			return cipher.EncryptWithAd(null, payload, messageBuffer);
		}

		public int ReadMessage(ReadOnlySpan<byte> message, Span<byte> payloadBuffer)
		{
			Exceptions.ThrowIfDisposed(disposed, nameof(Transport<CipherType>));

			if (initiator && IsOneWay)
			{
				throw new InvalidOperationException("Initiator cannot read messages from a one-way stream.");
			}

			if (message.Length > Protocol.MaxMessageLength)
			{
				throw new ArgumentException($"Noise message must be less than or equal to {Protocol.MaxMessageLength} bytes in length.");
			}

			if (message.Length < Aead.TagSize)
			{
				throw new ArgumentException($"Noise message must be greater than or equal to {Aead.TagSize} bytes in length.");
			}

			if (message.Length - Aead.TagSize > payloadBuffer.Length)
			{
				throw new ArgumentException("Payload buffer does not have enough space to hold the plaintext.");
			}

			var cipher = initiator ? c2 : c1;
			Debug.Assert(cipher.HasKey());

			return cipher.DecryptWithAd(null, message, payloadBuffer);
		}

		public void RekeyInitiatorToResponder()
		{
			Exceptions.ThrowIfDisposed(disposed, nameof(Transport<CipherType>));

			c1.Rekey();
		}

		public void RekeyResponderToInitiator()
		{
			Exceptions.ThrowIfDisposed(disposed, nameof(Transport<CipherType>));

			if (IsOneWay)
			{
				throw new InvalidOperationException("Cannot rekey responder to initiator in a one-way stream.");
			}

			c2.Rekey();
		}

		public void Dispose()
		{
			if (!disposed)
			{
				c1.Dispose();
				c2?.Dispose();
				disposed = true;
			}
		}
	}
}
