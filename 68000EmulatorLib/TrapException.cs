using System;

namespace PendleCodeMonkey.MC68000EmulatorLib
{
    /// <summary>
    /// Implementation of the <see cref="TrapException"/> class.
    /// </summary>
    public class TrapException : Exception
    {
        /// <summary>
        /// Gets the TRAP vector value.
        /// </summary>
        public ushort Vector { get; protected set; }

        /// <summary>
        /// Standard trap exception descriptions
        /// </summary>
        /// <param name="vector"></param>
        /// <returns></returns>
        public static string Description(ushort vector)
        {
            return vector switch
            {
                0 => "Reset SSP",
                1 => "Reset SSP",
                2 => "Reset SSP",
                3 => "Reset SSP",
                4 => "Reset SSP",
                5 => "Reset SSP",
                6 => "Reset SSP",
                7 => "Reset SSP",
                8 => "Reset SSP",
                9 => "Reset SSP",
                10 => "Reset SSP",
                11 => "Reset SSP",
                12 => "Reset SSP",
                _ => $"Unknown vector #{vector}"
            };
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="TrapException"/> class.
        /// </summary>
        protected TrapException() : base()
        { }

        /// <summary>
        /// Initializes a new instance of the <see cref="TrapException"/> class.
        /// </summary>
        /// <param name="vector">The TRAP vector value.</param>
        public TrapException(ushort vector) : base(Description(vector))
        {
            Vector = vector;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="TrapException"/> class.
        /// </summary>
        /// <param name="vector">The TRAP vector value.</param>
        /// <param name="message">The exception message.</param>
        public TrapException(ushort vector, string message) : base(message)
        {
            Vector = vector;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="TrapException"/> class.
        /// </summary>
        /// <param name="vector">The TRAP vector value.</param>
        /// <param name="message">The exception message.</param>
        /// <param name="innerException">Inner <see cref="Exception"/> object.</param>
        public TrapException(ushort vector, string message, Exception innerException) : base(message, innerException)
        {
            Vector = vector;
        }
    }
}
