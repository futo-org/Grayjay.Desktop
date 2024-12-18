using System.Collections.Generic;

namespace Noise
{
	/// <summary>
	/// A pre-message pattern is one of the following
	/// sequences of tokens: "e", "s", "e, s", or empty.
	/// </summary>
	public sealed class PreMessagePattern
	{
		/// <summary>
		/// The "e" pre-message pattern.
		/// </summary>
		public static readonly PreMessagePattern E = new PreMessagePattern(Token.E);

		/// <summary>
		/// The "s" pre-message pattern.
		/// </summary>
		public static readonly PreMessagePattern S = new PreMessagePattern(Token.S);

		/// <summary>
		/// The "e, s" pre-message pattern.
		/// </summary>
		public static readonly PreMessagePattern ES = new PreMessagePattern(Token.E, Token.S);

		/// <summary>
		/// The empty pre-message pattern.
		/// </summary>
		public static readonly PreMessagePattern Empty = new PreMessagePattern();

		private PreMessagePattern(params Token[] tokens)
		{
			Tokens = tokens;
		}

		/// <summary>
		/// Gets the tokens of the pre-message pattern.
		/// </summary>
		public IEnumerable<Token> Tokens { get; }
	}
}
