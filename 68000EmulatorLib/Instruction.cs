using PendleCodeMonkey.MC68000EmulatorLib.Enumerations;

namespace PendleCodeMonkey.MC68000EmulatorLib
{
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
            /// <param name="opcode">The 8-bit opcode value for the instruction.</param>
            /// <param name="info">An <see cref="InstructionInfo"/> instance giving info about the instruction.</param>
            /// <param name="byteOperand">The value of the 8-bit operand (if any).</param>
            /// <param name="wordOperand">The value of the 16-bit operand (if any).</param>
            /// <param name="displacement">The value of the 8-bit displacement (if any).</param>
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
            }

            /// <summary>
            /// Initializes an existing instance of the <see cref="Instruction"/> class.
            /// </summary>
            /// <param name="opcode">The 8-bit opcode value for the instruction.</param>
            /// <param name="info">An <see cref="InstructionInfo"/> instance giving info about the instruction.</param>
            /// <param name="size"></param>
            /// <param name="srcAddrMode"></param>
            /// <param name="srcExtWord1"></param>
            /// <param name="srcExtWord2"></param>
            /// <param name="destAddrMode"></param>
            /// <param name="destExtWord1"></param>
            /// <param name="destExtWord2"></param>
            /// <returns>This instruction (convenience)</returns>
            internal Instruction SetInstruction(ushort opcode, InstructionInfo info, OpSize? size = null, byte? srcAddrMode = null, ushort? srcExtWord1 = null, ushort? srcExtWord2 = null,
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
                return this;
            }

            /// <summary>
            /// The 16-bit opcode value for this instruction.
            /// </summary>
            public ushort Opcode { get; internal set; }

            /// <summary>
            /// An <see cref="InstructionInfo"/> instance giving info about the instruction.
            /// </summary>
            internal InstructionInfo Info { get; set; }

            /// <summary>
            /// Address accessed by this instruction (if not immediate or register).
            /// Required for trap handling in <see cref="Machine"/> subclasses.
            /// </summary>
            public uint? AccessAddress { get; internal set; }

            /// <summary>
            /// Type of access to <see cref="AccessAddress"/>.  Required for 
            /// trap handling in <see cref="Machine"/> subclasses.
            /// </summary>
            public EAType? AccessAddressType { get; internal set; }

            /// <summary>
            /// The size of the operation [byte, word, or long] (if any).
            /// </summary>
            public OpSize? Size { get; internal set; }

            /// <summary>
            /// The value of the source addressing mode (if any).
            /// </summary>
            public byte? SourceAddrMode { get; internal set; }

            /// <summary>
            /// The value of the first source extension word (if any).
            /// </summary>
            public ushort? SourceExtWord1 { get; internal set; }

            /// <summary>
            /// The value of the second source extension word (if any).
            /// </summary>
            public ushort? SourceExtWord2 { get; internal set; }

            /// <summary>
            /// The value of the destination addressing mode (if any).
            /// </summary>
            public byte? DestAddrMode { get; internal set; }

            /// <summary>
            /// The value of the first destination extension word (if any).
            /// </summary>
            public ushort? DestExtWord1 { get; internal set; }

            /// <summary>
            /// The value of the second destination extension word (if any).
            /// </summary>
            public ushort? DestExtWord2 { get; internal set; }
        }
    }
}
