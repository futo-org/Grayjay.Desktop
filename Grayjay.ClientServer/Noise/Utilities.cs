using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;

namespace Noise
{
	/// <summary>
	/// Various utility functions.
	/// </summary>
	internal static class Utilities
	{
		private static readonly RandomNumberGenerator random = RandomNumberGenerator.Create();

		/// <summary>
		/// Alignes the pointer up to the nearest alignment boundary.
		/// </summary>
		public static IntPtr Align(IntPtr ptr, int alignment)
		{
			ulong mask = (ulong)alignment - 1;
			return (IntPtr)(((ulong)ptr + mask) & ~mask);
		}

		/// <summary>
		/// Generates a cryptographically strong pseudorandom sequence of n bytes.
		/// </summary>
		public static byte[] GetRandomBytes(int n)
		{
			Debug.Assert(n > 0);

			var bytes = new byte[n];
			random.GetBytes(bytes);

			return bytes;
		}

		// NoOptimize to prevent the optimizer from deciding this call is unnecessary.
		// NoInlining to prevent the inliner from forgetting that the method was NoOptimize.
		[MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
		public static void ZeroMemory(Span<byte> buffer)
		{
			buffer.Clear();
		}
	}
}
