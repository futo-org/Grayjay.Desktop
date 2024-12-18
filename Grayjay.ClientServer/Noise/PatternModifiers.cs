using System;

namespace Noise
{
	/// <summary>
	/// Pattern modifiers specify arbitrary extensions or modifications
	/// to the behavior specified by the handshake pattern.
	/// </summary>
	[Flags]
	public enum PatternModifiers
	{
		/// <summary>
		/// No pattern modifiers were selected.
		/// </summary>
		None = 0,

		/// <summary>
		/// The fallback modifier converts an Alice-initiated pattern to
		/// a Bob-initiated pattern by converting Alice's initial message
		/// to a pre-message that Bob must receive through some other
		/// means (e.g. as the initial field in an initial IK message from
		/// Alice). After this conversion, the rest of the handshake
		/// pattern is interpreted as a Bob-initiated handshake pattern.
		/// </summary>
		Fallback = 1,

		/// <summary>
		/// The modifier psk0 places a "psk" token at
		/// the beginning of the first handshake message.
		/// </summary>
		Psk0 = 2,

		/// <summary>
		/// The modifier psk1 places a "psk" token at
		/// the end of the first handshake message.
		/// </summary>
		Psk1 = 4,

		/// <summary>
		/// The modifier psk2 places a "psk" token at
		/// the end of the second handshake message.
		/// </summary>
		Psk2 = 8,

		/// <summary>
		/// The modifier psk0 places a "psk" token at
		/// the end of the third handshake message.
		/// </summary>
		Psk3 = 16
	}
}
