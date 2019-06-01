using System;

namespace GitPackager.Nuke.Tools.Exceptions
{
    public class NoTeamcityInstanceException : Exception
    {
        public NoTeamcityInstanceException(string message) : base(message) { }
    }
}
