using PendleCodeMonkey.MC68000EmulatorLib.Enumerations;

namespace PendleCodeMonkey.MC68000EmulatorLib
{
    /// <summary>
    /// Partial implementation of the <see cref="Machine"/> class.
    /// </summary>
    public partial class Machine
    {
        /// <summary>
        /// Implementation of the <see cref="Instruction"/> class.
        /// </summary>
        public class Instruction
        {
            /// <summary>
            /// Initializes a new instance of the <see cref="Instruction"/> class.
            /// </summary>
            /// <param name="opcode">The 16-bit opcode value for the instruction.</param>
            /// <param name="info">An <see cref="InstructionInfo"/> instance giving info about the instruction.</param>
            /// <param name="size">Optional size of the instruction.</param>
            /// <param name="srcAddrMode">Optional Address Mode for the source operand.</param>
            /// <param name="srcExtWord1">Optional extension word 1 for the source operand.</param>
            /// <param name="srcExtWord2">Optional extension word 2 for the source operand.</param>
            /// <param name="destAddrMode">Optional Address Mode for the destination operand.</param>
            /// <param name="destExtWord1">Optional extension word 1 for the destination operand.</param>
            /// <param name="destExtWord2">Optional extension word 2 for the destination operand.</param>
            internal Instruction(ushort opcode, InstructionInfo info, Option<OpSize> size, Option<byte> srcAddrMode, Option<ushort> srcExtWord1, Option<ushort> srcExtWord2,
                                 Option<byte> destAddrMode, Option<ushort> destExtWord1, Option<ushort> destExtWord2)
            {
                Opcode = opcode;
                Info = info;
                Size = size;
                SourceAddrMode = srcAddrMode;
                SourceExtWord1 = srcExtWord1;
                SourceExtWord2 = srcExtWord2;
                DestAddrMode = destAddrMode;
                DestExtWord1 = destExtWord1;
                DestExtWord2 = destExtWord2;
                Address = 0;
                AccessAddress = None;
                AccessAddressType = None;
            }

            /// <summary>
            /// Constructor.  Initializes a new instance of the <see cref="Instruction"/> class with only opcode, info, size, and source addressing mode.
            /// </summary>
            /// <param name="opcode"></param>
            /// <param name="info"></param>
            /// <param name="size"></param>
            /// <param name="srcAddrMode"></param>
            internal Instruction(ushort opcode, InstructionInfo info, Option<OpSize> size, Option<byte> srcAddrMode)
                : this(opcode, info, size, srcAddrMode, None, None, None, None, None)
            {
            }

            /// <summary>
            /// The 16-bit opcode value for this instruction.
            /// </summary>
            public ushort Opcode { get; internal set; }

            /// <summary>
            /// Address of this instruction in memory.
            /// </summary>
            public uint Address { get; internal set; }

            /// <summary>
            /// Number of clocks for this instruction.
            /// </summary>
            public ulong Clocks { get; internal set; }

            private Option<uint> _length;

            /// <summary>
            /// Instruction length in bytes.
            /// </summary>
            public uint Length
            {
                get
                {
                    if (_length.IsSome)
                    {
                        return _length.Value;
                    }
                    else
                    {
                        uint len = 2; // Minimum length is 2 bytes for the opcode itself
                        if (SourceExtWord1.IsSome) len += 2;
                        if (SourceExtWord2.IsSome) len += 2;
                        if (DestExtWord1.IsSome) len += 2;
                        if (DestExtWord2.IsSome) len += 2;
                        _length = Some(len);
                        return len;
                    }
                }
            }

            /// <summary>
            /// An <see cref="InstructionInfo"/> instance giving info about the instruction.
            /// </summary>
            internal InstructionInfo Info { get; set; }

            /// <summary>
            /// Address accessed by this instruction (if not immediate or register).
            /// Required for trap handling.
            /// </summary>
            public Option<uint> AccessAddress { get; internal set; }

            /// <summary>
            /// Type of access to <see cref="AccessAddress"/>.  Required for 
            /// trap handling.
            /// </summary>
            public Option<EAType> AccessAddressType { get; internal set; }

            /// <summary>
            /// The size of the operation [byte, word, or long] (if any).
            /// </summary>
            public Option<OpSize> Size { get; internal set; }

            /// <summary>
            /// The value of the source addressing mode (if any).
            /// </summary>
            public Option<byte> SourceAddrMode
            {
                get => field;
                internal set { field = value; _length = None; }
            }

            /// <summary>
            /// The value of the first source extension word (if any).
            /// </summary>
            public Option<ushort> SourceExtWord1
            {
                get => field;
                internal set { field = value; _length = None; }
            }

            /// <summary>
            /// The value of the second source extension word (if any).
            /// </summary>
            public Option<ushort> SourceExtWord2
            {
                get => field;
                internal set { field = value; _length = None; }
            }

            /// <summary>
            /// The value of the destination addressing mode (if any).
            /// </summary>
            public Option<byte> DestAddrMode
            {
                get => field;
                internal set { field = value; _length = None; }
            }

            /// <summary>
            /// The value of the first destination extension word (if any).
            /// </summary>
            public Option<ushort> DestExtWord1
            {
                get => field;
                internal set { field = value; _length = None; }
            }

            /// <summary>
            /// The value of the second destination extension word (if any).
            /// </summary>
            public Option<ushort> DestExtWord2
            {
                get => field;
                internal set { field = value; _length = None; }
            }
        }
    }
}
