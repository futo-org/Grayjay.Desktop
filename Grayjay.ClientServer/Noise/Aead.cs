namespace Noise
{
	/// <summary>
	/// AEAD constants.
	/// </summary>
	internal static class Aead
	{
		/// <summary>
		/// Secret key size in bytes.
		/// </summary>
		public const int KeySize = 32;

		/// <summary>
		/// Nonce size in bytes.
		/// </summary>
		public const int NonceSize = 12;

		/// <summary>
		/// Authentication tag size in bytes.
		/// </summary>
		public const int TagSize = 16;
	}
}
