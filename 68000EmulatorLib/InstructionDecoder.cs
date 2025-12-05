using PendleCodeMonkey.MC68000EmulatorLib.Enumerations;
using System.Diagnostics;

namespace PendleCodeMonkey.MC68000EmulatorLib
{
    /// <summary>
    /// Partial implementation of the <see cref="Machine"/> class.
    /// </summary>
    public partial class Machine
    {
        /// <summary>
        /// Implementation of the <see cref="InstructionDecoder"/> class.
        /// </summary>
        public class InstructionDecoder
        {
            private readonly OpcodeDecoder _handler;

            /// <summary>
            /// Initializes a new instance of the <see cref="InstructionDecoder"/> class.
            /// </summary>
            /// <param name="machine">The <see cref="Machine"/> instance for which this object is handling the execution of instructions.</param>
            internal InstructionDecoder(Machine machine)
            {
                Machine = machine ?? throw new ArgumentNullException(nameof(machine));
                _handler = new OpcodeDecoder();
            }

            /// <summary>
            /// Gets or sets the <see cref="Machine"/> instance for which this <see cref="InstructionDecoder"/> instance
            /// is handling the decoding of instructions.
            /// </summary>
            private Machine Machine { get; set; }

            /// <summary>
            /// Fetch the instruction located at the current Program Counter address, incrementing the
            /// Program Counter accordingly.
            /// </summary>
            /// <returns>An <see cref="Instruction"/> instance containing details about the instruction that has been fetched.</returns>
            public Instruction? FetchInstruction()
            {
                // Read the next word (which contains the instruction opcode)
                uint pc = Machine.CPU.PC;
                var opcode = Machine.ReadNextPCWord(); // This increments the PC

                // Locate the instruction for this opcode.
                var inst = _handler.GetLegalInstruction(opcode);
                if (inst != null)
                {
                    // Fill in any immediate data and extension words for the instruction
                    ReadImmDataAndExtWords(opcode, inst); // May increment the PC further

                    inst.Address = pc;
                    Machine.CurrentInstruction = inst;
                }
                else
                {
                    // Illegal instruction - set current instruction to dummy instruction for
                    // address trace purposes.
                    Machine.CurrentInstruction = new(opcode, new(opcode,0xffff,"<illegal>", OpHandlerID.ILLEGAL));
                    Machine.CurrentInstruction.Address = pc;
                }

                // Return instruction or null if this is not a recognised opcode (i.e. an illegal instruction)
                return inst;
            }

            /// <summary>
            /// Read any immediate data and extension words for the specified instruction and fill
            /// in the appropriate fields in the <see cref="Instruction"/> instance.
            /// </summary>
            /// <param name="opcode">The 16-bit opcode value for the instruction.</param>
            /// <param name="instInfo">The <see cref="InstructionInfo"/> instance for the instruction.</param>
            /// <returns>An <see cref="Instruction"/> object containing details of the instruction.</returns>
            private void ReadImmDataAndExtWords(ushort opcode, Instruction inst)
            {
                OpSize? opSize = inst.Size;
                ushort? srcExt1 = null;
                ushort? srcExt2 = null;
                ushort? destExt1 = null;
                ushort? destExt2 = null;

                switch (inst.Info.HandlerID)
                {
                    case OpHandlerID.ORItoCCR:
                    case OpHandlerID.ANDItoCCR:
                    case OpHandlerID.EORItoCCR:
                    case OpHandlerID.ORItoSR:
                    case OpHandlerID.ANDItoSR:
                    case OpHandlerID.EORItoSR:
                    case OpHandlerID.ORI:
                    case OpHandlerID.ANDI:
                    case OpHandlerID.SUBI:
                    case OpHandlerID.ADDI:
                    case OpHandlerID.EORI:
                    case OpHandlerID.CMPI:
                        Debug.Assert(inst.SourceAddrMode == null);
                        (srcExt1, srcExt2) = ReadImmediateOperandData(opSize!.Value);
                        break;

                    case OpHandlerID.BCHG:
                    case OpHandlerID.BCLR:
                    case OpHandlerID.BSET:
                    case OpHandlerID.BTST:
                        if ((opcode & 0x0100) == 0)
                        {
                            // Static bit number
                            // Read the bit number in the extension word
                            Debug.Assert(inst.SourceAddrMode == null);
                            srcExt1 = Machine.ReadNextPCWord();
                        }
                        break;

                    case OpHandlerID.MOVEM:
                    case OpHandlerID.LINK:
                    case OpHandlerID.STOP:
                    case OpHandlerID.DBcc:
                    case OpHandlerID.MOVEP:
                        Debug.Assert(inst.SourceAddrMode == null);
                        // Read the displacement value (which is a word).
                        srcExt1 = Machine.ReadNextPCWord();
                        break;

                    case OpHandlerID.BRA:
                    case OpHandlerID.BSR:
                    case OpHandlerID.Bcc:
                        if (opSize == OpSize.Word)
                        {
                            Debug.Assert(inst.SourceAddrMode == null);
                            srcExt1 = Machine.ReadNextPCWord();
                        }
                        break;

                    default:
                        // All that don't need special handling.
                        break;
                }

                if (inst.SourceAddrMode.HasValue)
                {
                    (srcExt1, srcExt2) = ReadExtensionWordData(inst.SourceAddrMode.Value, opSize!.Value);
                }
                if (inst.DestAddrMode.HasValue)
                {
                    (destExt1, destExt2) = ReadExtensionWordData(inst.DestAddrMode.Value, opSize!.Value);
                }

                // Set the extension words if any
                inst.SourceExtWord1 = srcExt1;
                inst.SourceExtWord2 = srcExt2;
                inst.DestExtWord1 = destExt1;
                inst.DestExtWord2 = destExt2;
            }

            /// <summary>
            /// Read an immediate operand value from the current Program Counter address.
            /// </summary>
            /// <param name="size">The size of immediate operand data to be read (Word or Long).</param>
            /// <returns>
            /// A tuple containing two extension word values (both of which are optional).
            /// For Long data, both extension word values will be non-null and need to be combined to make the 32-bit operand value.
            /// </returns>
            private (ushort? extWord1, ushort? extWord2) ReadImmediateOperandData(OpSize size)
            {
                ushort ext1 = Machine.ReadNextPCWord();
                ushort? ext2 = null;
                if (size == OpSize.Long)
                {
                    ext2 = Machine.ReadNextPCWord();
                }
                return (ext1, ext2);
            }

            /// <summary>
            /// Read extension word operand data for the specified effective address mode.
            /// </summary>
            /// <param name="ea">The effective address mode value (see Enumerations.AddrMode).</param>
            /// <param name="size">The size of effective address operand data to be read (Word or Long).</param>
            /// <returns>
            /// A tuple containing two extension word values (both of which are optional).
            /// For Long data, both extension word values will be non-null and need to be combined to make the 32-bit operand value.
            /// </returns>
            private (ushort? extWord1, ushort? extWord2) ReadExtensionWordData(byte ea, OpSize size)
            {
                ushort? ext1 = null;
                ushort? ext2 = null;

                switch (ea & 0x38)
                {
                    case 0x28:          // Address Register Indirect with Displacement
                    case 0x30:          // Address Register Indirect with Index
                        ext1 = Machine.ReadNextPCWord();
                        break;
                    case 0x38:
                        switch (ea & 0x07)
                        {
                            case 0x00:  // Absolute Short
                            case 0x02:  // PC Relative with Displacement
                            case 0x03:  // PC Relative with Index
                                ext1 = Machine.ReadNextPCWord();
                                break;
                            case 0x01:  // Absolute Long
                                ext1 = Machine.ReadNextPCWord();
                                ext2 = Machine.ReadNextPCWord();
                                break;
                            case 0x04:  // Immediate
                                ext1 = Machine.ReadNextPCWord();
                                if (size == OpSize.Long)
                                {
                                    ext2 = Machine.ReadNextPCWord();
                                }
                                break;
                        }
                        break;
                    default:
                        // Must be one of: Data Register Direct, Address Register Direct, Address Register Indirect,
                        // Address Register Indirect with Postincrement, or Address Register Indirect with Predecrement.
                        // None of which require extension word data.
                        break;
                }
                return (ext1, ext2);
            }
        }
    }
}