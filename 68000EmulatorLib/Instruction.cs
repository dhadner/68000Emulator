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
            internal Instruction(ushort opcode, InstructionInfo info, OpSize? size = null, byte? srcAddrMode = null, ushort? srcExtWord1 = null, ushort? srcExtWord2 = null,
                                 byte? destAddrMode = null, ushort? destExtWord1 = null, ushort? destExtWord2 = null)
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
                AccessAddress = null;
                AccessAddressType = null;
            }

            /// <summary>
            /// Initializes an existing instance of the <see cref="Instruction"/> class.
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
            /// <returns>This instruction (convenience)</returns>
            internal Instruction SetInstruction(ushort opcode, InstructionInfo info, OpSize size = OpSize.Word, byte? srcAddrMode = null, ushort? srcExtWord1 = null, ushort? srcExtWord2 = null,
                                                byte? destAddrMode = null, ushort? destExtWord1 = null, ushort? destExtWord2 = null)
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
                AccessAddress = null;
                AccessAddressType = null;
                Clocks = 0;
                return this;
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

            private uint? _length;

            /// <summary>
            /// Instruction length in bytes.
            /// </summary>
            public uint Length
            {
                get
                {
                    if (_length.HasValue)
                    {
                        return _length.Value;
                    }
                    else
                    {
                        uint len = 2; // Minimum length is 2 bytes for the opcode itself
                        if (SourceExtWord1.HasValue) len += 2;
                        if (SourceExtWord2.HasValue) len += 2;
                        if (DestExtWord1.HasValue) len += 2;
                        if (DestExtWord2.HasValue) len += 2;
                        _length = len;
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
            public uint? AccessAddress { get; internal set; }

            /// <summary>
            /// Type of access to <see cref="AccessAddress"/>.  Required for 
            /// trap handling.
            /// </summary>
            public EAType? AccessAddressType { get; internal set; }

            /// <summary>
            /// The size of the operation [byte, word, or long] (if any).
            /// </summary>
            public OpSize? Size { get; internal set; }

            /// <summary>
            /// The value of the source addressing mode (if any).
            /// </summary>
            public byte? SourceAddrMode
            {
                get => field;
                internal set { field = value; _length = null; }
            }

            /// <summary>
            /// The value of the first source extension word (if any).
            /// </summary>
            public ushort? SourceExtWord1
            {
                get => field;
                internal set { field = value; _length = null; }
            }

            /// <summary>
            /// The value of the second source extension word (if any).
            /// </summary>
            public ushort? SourceExtWord2
            {
                get => field;
                internal set { field = value; _length = null; }
            }

            /// <summary>
            /// The value of the destination addressing mode (if any).
            /// </summary>
            public byte? DestAddrMode
            {
                get => field;
                internal set { field = value; _length = null; }
            }

            /// <summary>
            /// The value of the first destination extension word (if any).
            /// </summary>
            public ushort? DestExtWord1
            {
                get => field;
                internal set { field = value; _length = null; }
            }

            /// <summary>
            /// The value of the second destination extension word (if any).
            /// </summary>
            public ushort? DestExtWord2
            {
                get => field;
                internal set { field = value; _length = null; }
            }
        }
    }
}
