using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace Noise
{
	/// <summary>
	/// A message pattern is some sequence of tokens from
	/// the set ("e", "s", "ee", "es", "se", "ss", "psk").
	/// </summary>
	public sealed class MessagePattern
	{
		internal MessagePattern(params Token[] tokens)
		{
			Debug.Assert(tokens != null);
			Debug.Assert(tokens.Length > 0);

			Tokens = tokens;
		}

		internal MessagePattern(IEnumerable<Token> tokens)
		{
			Debug.Assert(tokens != null);
			Debug.Assert(tokens.Any());

			Tokens = tokens;
		}

		/// <summary>
		/// Gets the tokens of the message pattern.
		/// </summary>
		public IEnumerable<Token> Tokens { get; }

		/// <summary>
		/// Prepends the PSK token to the pattern.
		/// </summary>
		internal MessagePattern PrependPsk()
		{
			return new MessagePattern(Prepend(Tokens, Token.PSK));
		}

		/// <summary>
		/// Appends the PSK token to the pattern.
		/// </summary>
		internal MessagePattern AppendPsk()
		{
			return new MessagePattern(Append(Tokens, Token.PSK));
		}

		/// <summary>
		/// Calculate the message overhead in bytes (i.e. the
		/// total size of all transmitted keys and AEAD tags).
		/// </summary>
		internal int Overhead(int dhLen, bool hasKey, bool isPsk)
		{
			int overhead = 0;

			foreach (var token in Tokens)
			{
				if (token == Token.E)
				{
					overhead += dhLen;
					hasKey |= isPsk;
				}
				else if (token == Token.S)
				{
					overhead += hasKey ? dhLen + Aead.TagSize : dhLen;
				}
				else
				{
					hasKey = true;
				}
			}

			return hasKey ? overhead + Aead.TagSize : overhead;
		}

		private static IEnumerable<T> Prepend<T>(IEnumerable<T> source, T element)
		{
			yield return element;

			foreach (var item in source)
			{
				yield return item;
			}
		}

		private static IEnumerable<T> Append<T>(IEnumerable<T> source, T element)
		{
			foreach (var item in source)
			{
				yield return item;
			}

			yield return element;
		}
	}
}
