namespace Grayjay.ClientServer.Sabr
{
    public class SabrException : IOException
    {
        public SabrException(string message, Exception? cause = null) : base(message, cause) { }
    }

    public class SabrBlockedException : SabrException
    {
        public SabrBlockedException(string message) : base(message) { }
    }

    public class SabrReloadRequiredException : SabrException
    {
        public SabrReloadRequiredException(string message) : base(message) { }
    }

    public class SabrFormatSubstitutedException : SabrException
    {
        public SabrFormatSubstitutedException(string message) : base(message) { }
    }

    public class CastSupersededException : Exception
    {
        public CastSupersededException(string message) : base(message) { }
    }
}
