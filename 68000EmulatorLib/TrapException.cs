using PendleCodeMonkey.MC68000EmulatorLib.Enumerations;
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
        /// Details for this exception vector.
        /// </summary>
        public ExceptionDetails TrapDetails { get; set; }

        /// <summary>
        /// Standard trap exception descriptions
        /// </summary>
        /// <param name="vector"></param>
        /// <returns></returns>
        public static string Description(ushort vector)
        {
            return vector switch
            {
                0 => "Reset Initial Interrupt Stack Pointer",
                1 => "Reset Initial Program Counter ",
                2 => "Access Fault",
                3 => "Address Error",
                4 => "Illegal Instruction",
                5 => "Integer Divide by Zero",
                6 => "CHK, CHK2 Instruction",
                7 => "FTRAPcc, TRAPcc, TRAPV Instructions",
                8 => "Privilege Violation",
                9 => "Trace",
                10 => "Line 1010 Emulator (Unimplemented A-Line Opcode)",
                11 => "Line 1111 Emulator (Unimplemented F-Line Opcode)",
                13 => "Coprocessor Protocol Violation",
                14 => "Format Error",
                15 => "Uninitialized Interrupt",
                24 => "Spurious Interrupt",
                25 => "Level 1 Interrupt Autovector",
                26 => "Level 2 Interrupt Autovector",
                27 => "Level 3 Interrupt Autovector",
                28 => "Level 4 Interrupt Autovector",
                29 => "Level 5 Interrupt Autovector",
                30 => "Level 6 Interrupt Autovector",
                31 => "Level 7 Interrupt Autovector",
                >= 32 and <= 47 => $"TRAP #0 D 15 Instruction Vectors (vector #{vector})",
                48 => "FP Branch or Set on Unordered Condition",
                49 => "FP Inexact Result",
                50 => "FP Divide by Zero",
                51 => "FP Underflow",
                52 => "FP Operand Error",
                53 => "FP Overflow",
                54 => "FP Signaling NaN",
                55 => "FP Unimplemented Data Type (Defined for MC68040)",
                56 => "MMU Configuration Error",
                57 => "MMU Illegal Operation Error",
                58 => "MMU Access Level Violation Error",
                >= 64 and <= 255 => $"User Defined Vector #{vector}",
                _ => $"(Unassigned, Reserved) vector #{vector}"
            };
        }

        protected void LoadTrapDetails()
        {
            TrapDetails = ExceptionDetails.FromVector((TrapVector)Vector);
            if (Vector > 255)
            {
                Logger.Log(LogLevel.Error, "CPU", () => $"Unknown Trap Exception vector: {Vector}");
            }
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
            LoadTrapDetails();
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="TrapException"/> class.
        /// </summary>
        /// <param name="vector">The TRAP vector value.</param>
        /// <param name="message">The exception message.</param>
        public TrapException(ushort vector, string message) : base(message)
        {
            Vector = vector;
            LoadTrapDetails();
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
            LoadTrapDetails();
        }
    }
}
