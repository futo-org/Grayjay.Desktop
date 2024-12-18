using System;

namespace Noise
{
	/// <summary>
	/// Constants representing the available DH functions.
	/// </summary>
	public sealed class DhFunction
	{
		/// <summary>
		/// The Curve25519 DH function (aka "X25519" in
		/// <see href="https://tools.ietf.org/html/rfc7748">RFC 7748</see>).
		/// </summary>
		public static readonly DhFunction Curve25519 = new DhFunction("25519");

		private readonly string name;

		private DhFunction(string name) => this.name = name;

		/// <summary>
		/// Returns a string that represents the current object.
		/// </summary>
		/// <returns>The name of the current DH function.</returns>
		public override string ToString() => name;

		internal static DhFunction Parse(ReadOnlySpan<char> s)
		{
			switch (s)
			{
				case var _ when s.SequenceEqual(Curve25519.name.AsSpan()): return Curve25519;
				default: throw new ArgumentException("Unknown DH function.", nameof(s));
			}
		}
	}
}
