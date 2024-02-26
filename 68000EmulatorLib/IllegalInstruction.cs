using System;

namespace PendleCodeMonkey.MC68000EmulatorLib
{
    /// <summary>
    /// Exception thrown when an instruction is malformed or otherwise illegal.
    /// </summary>
    public class IllegalInstruction : Exception
    {
        /// <summary>
        /// Create an instance of an <see cref="IllegalInstruction"/> exception.
        /// </summary>
        /// <param name="message"></param>
        public IllegalInstruction(string message) : base(message)
        {
        }
    }
}
