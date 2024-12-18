using System;

namespace Noise
{
	/// <summary>
	/// Hash functions (and associated constants).
	/// </summary>
	internal interface Hash : IDisposable
	{
		/// <summary>
		/// A constant specifying the size in bytes of the hash output.
		/// </summary>
		int HashLen { get; }

		/// <summary>
		/// A constant specifying the size in bytes that the hash function
		/// uses internally to divide its input for iterative processing.
		/// </summary>
		int BlockLen { get; }

		/// <summary>
		/// Appends the specified data to the data already processed in the hash.
		/// </summary>
		void AppendData(ReadOnlySpan<byte> data);

		/// <summary>
		/// Retrieves the hash for the accumulated data into the hash parameter,
		/// and resets the object to its initial state.
		/// </summary>
		void GetHashAndReset(Span<byte> hash);
	}
}
