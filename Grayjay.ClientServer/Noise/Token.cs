namespace Noise
{
	/// <summary>
	/// The smallest unit of the Noise handshake language.
	/// </summary>
	public enum Token
	{
		/// <summary>
		/// The sender generates a new ephemeral key pair and stores
		/// it in the e variable, writes the ephemeral public key as
		/// cleartext into the message buffer, and hashes the public
		/// key along with the old h to derive a new h.
		/// </summary>
		E,

		/// <summary>
		/// The sender writes its static public key from the s variable
		/// into the message buffer, encrypting it if k is non-empty,
		/// and hashes the output along with the old h to derive a new h.
		/// </summary>
		S,

		/// <summary>
		/// A DH is performed between the initiator's ephemeral private key and
		/// the responder's ephemeral public key. The result is hashed along
		/// with the old ck to derive a new ck and k, and n is set to zero.
		/// </summary>
		EE,

		/// <summary>
		/// A DH is performed between the initiator's static private key and
		/// the responder's ephemeral public key. The result is hashed along
		/// with the old ck to derive a new ck and k, and n is set to zero.
		/// </summary>
		SE,

		/// <summary>
		/// A DH is performed between the initiator's ephemeral private key and
		/// the responder's static public key. The result is hashed along
		/// with the old ck to derive a new ck and k, and n is set to zero.
		/// </summary>
		ES,

		/// <summary>
		/// A DH is performed between the initiator's static private key and
		/// the responder's static public key. The result is hashed along
		/// with the old ck to derive a new ck and k, and n is set to zero.
		/// </summary>
		SS,

		/// <summary>
		/// Noise provides a pre-shared symmetric key or PSK mode to support
		/// protocols where both parties have a 32-byte shared secret key.
		/// In a PSK handshake, a "psk" token is allowed to appear one or
		/// more times in a handshake pattern. This token can only appear
		/// in message patterns (not pre-message patterns).
		/// </summary>
		PSK
	}
}
