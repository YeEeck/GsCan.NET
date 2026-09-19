using System;

namespace GsCan
{
    public sealed class GsCanException : Exception
    {
        public GsCanException(string message)
            : base(message)
        {
        }

        public GsCanException(string message, Exception inner)
            : base(message, inner)
        {
        }
    }
}
