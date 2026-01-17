#define CHECK_STACK_POINTER
#define CHECK_PC_FOR_ZERO

using PendleCodeMonkey.MC68000EmulatorLib.Enumerations;
using System.Diagnostics;

namespace PendleCodeMonkey.MC68000EmulatorLib
{
    /// <summary>
    /// Partial implementation of the <see cref="Machine"/> class.
    /// </summary>
    public partial class Machine
    {
#pragma warning disable S2325 // Methods and properties that don't access instance data should be static
        /// <summary>
        /// Implementation of the <see cref="OpcodeExecutionHandler"/> class.
        /// </summary>
        internal class OpcodeExecutionHandler
        {
            delegate TrapException? OpHandler(Instruction instruction);

            private const string EXT_WORD_NOT_AVAILABLE = "Required extension word is not available";
            private readonly Dictionary<OpHandlerID, OpHandler> _handlers = [];

            private readonly uint[] _bit = [ 0x00000001, 0x00000002, 0x00000004, 0x00000008, 0x00000010, 0x00000020, 0x00000040, 0x00000080,
                                             0x00000100, 0x00000200, 0x00000400, 0x00000800, 0x00001000, 0x00002000, 0x00004000, 0x00008000,
                                             0x00010000, 0x00020000, 0x00040000, 0x00080000, 0x00100000, 0x00200000, 0x00400000, 0x00800000,
                                             0x01000000, 0x02000000, 0x04000000, 0x08000000, 0x10000000, 0x20000000, 0x40000000, 0x80000000 ];

            internal int CallDepth { get; set; } = 0;

            /// <summary>
            /// Initializes a new instance of the <see cref="OpcodeExecutionHandler"/> class.
            /// </summary>
            /// <param name="machine">The <see cref="Machine"/> instance for which this object is handling the execution of instructions.</param>
            internal OpcodeExecutionHandler(Machine machine)
            {
                Machine = machine ?? throw new ArgumentNullException(nameof(machine));
                DeferredAddressRegisterUpdate = new AddressRegisterChange(Machine.CPU);
                InitOpcodeHandlers();
            }

            /// <summary>
            /// Gets or sets the <see cref="Machine"/> instance for which this <see cref="OpcodeExecutionHandler"/> instance
            /// is handling the execution of instructions
            /// </summary>
            private Machine Machine { get; set; }

            /// <summary>
            /// Address register to update (post-in/pre-dec) if
            /// no error during execution (Address Error/Bus Error).
            /// </summary>
            public AddressRegisterChange DeferredAddressRegisterUpdate { get; private set; }

            /// <summary>
            /// Address register and new address for deferred post-inc/pre-dec 
            /// addressing modes. Tracks source and destination updates separately
            /// to handle cases like MOVE.B -(A4),-(A4) where the same register
            /// is decremented twice.
            /// </summary>
            public record AddressRegisterChange
            {
                /// <summary>
                /// Initializes a new instance of the <see cref="AddressRegisterChange"/> class.
                /// </summary>
                /// <param name="cpu">The CPU instance to update registers on.</param>
                public AddressRegisterChange(CPU cpu)
                {
                    CPU = cpu;
                }

                /// <summary>
                /// Clears all pending address register updates.
                /// </summary>
                public void Clear(EAType? eaType = null)
                {
                    if (eaType == null || eaType == EAType.Source)
                    {
                        SourceRegisterNumber = null;
                        SourceNewAddress = null;
                        SourcePreDec = null;
                    }
                    if (eaType == null || eaType == EAType.Destination)
                    {
                        DestRegisterNumber = null;
                        DestNewAddress = null;
                        DestPreDec = null;
                    }
                }

                /// <summary>
                /// Sets a deferred address register update for the specified EA type.
                /// </summary>
                /// <param name="eaType">Whether this is a source or destination EA.</param>
                /// <param name="regNum">The address register number (0-7).</param>
                /// <param name="newAddress">The new address value to set.</param>
                public void Set(EAType? eaType, int regNum, uint newAddress, bool preDec)
                {
                    if (eaType == null || eaType == EAType.Source)
                    {
                        SourceRegisterNumber = regNum;
                        SourceNewAddress = newAddress;
                        SourcePreDec = preDec;
                    }
                    if (eaType == null || eaType == EAType.Destination)
                    {
                        DestRegisterNumber = regNum;
                        DestNewAddress = newAddress;
                        DestPreDec = preDec;
                    }
                }

                /// <summary>
                /// True if this is a pre-decrement update for the specified EA type.
                /// </summary>
                /// <param name="eaType"></param>
                /// <returns></returns>
                public bool? IsPreDec(EAType eaType)
                {
                    if (eaType == EAType.Source)
                    {
                        return SourcePreDec;
                    }
                    if (eaType == EAType.Destination)
                    {
                        return DestPreDec;
                    }
                    return null;
                }

                /// <summary>
                /// Applies all pending address register updates.
                /// Source is updated first, then destination, matching 68000 behavior.
                /// </summary>
                public void Apply(EAType? eaType = null)
                {
                    // Update source first (per 68000 execution order)
                    if ((eaType == null || eaType == EAType.Source) && SourceRegisterNumber.HasValue && SourceNewAddress.HasValue)
                    {
                        CPU.WriteAddressRegister(SourceRegisterNumber.Value, SourceNewAddress.Value);
                    }

                    // Then update destination
                    if ((eaType == null || eaType == EAType.Destination) && DestRegisterNumber.HasValue && DestNewAddress.HasValue)
                    {
                        CPU.WriteAddressRegister(DestRegisterNumber.Value, DestNewAddress.Value);
                    }

                    Clear(eaType);
                }

                private CPU CPU { get; }

                /// <summary>
                /// Gets whether there are no pending updates.
                /// </summary>
                public bool IsEmpty(EAType? eaType = null)
                {
                    if (eaType == EAType.Source)
                    {
                        return SourceRegisterNumber == null;
                    }
                    if (eaType == EAType.Destination)
                    {
                        return DestRegisterNumber == null;
                    }
                    return SourceRegisterNumber == null && DestRegisterNumber == null;
                }

                private int? SourceRegisterNumber { get; set; }
                private uint? SourceNewAddress { get; set; }
                private bool? SourcePreDec { get; set; }
                private int? DestRegisterNumber { get; set; }
                private uint? DestNewAddress { get; set; }
                private bool? DestPreDec { get; set; }
            }

            /// <summary>
            /// Initialize the dictionary of Opcode handlers.
            /// </summary>
            /// <remarks>
            /// Maps an enumerated operation handler ID to an Action that performs the operation.
            /// </remarks>
            private void InitOpcodeHandlers()
            {
                _handlers.Add(OpHandlerID.ORItoCCR, ORItoCCR);
                _handlers.Add(OpHandlerID.ORItoSR, ORItoSR);
                _handlers.Add(OpHandlerID.ORI, ORI);
                _handlers.Add(OpHandlerID.ANDItoCCR, ANDItoCCR);
                _handlers.Add(OpHandlerID.ANDItoSR, ANDItoSR);
                _handlers.Add(OpHandlerID.ANDI, ANDI);
                _handlers.Add(OpHandlerID.SUBI, SUBI);
                _handlers.Add(OpHandlerID.ADDI, ADDI);
                _handlers.Add(OpHandlerID.EORItoCCR, EORItoCCR);
                _handlers.Add(OpHandlerID.EORItoSR, EORItoSR);
                _handlers.Add(OpHandlerID.EORI, EORI);
                _handlers.Add(OpHandlerID.CMPI, CMPI);
                _handlers.Add(OpHandlerID.MOVE, MOVE);
                _handlers.Add(OpHandlerID.MOVEA, MOVEA);
                _handlers.Add(OpHandlerID.MOVEfromSR, MOVEfromSR);
                _handlers.Add(OpHandlerID.MOVEtoCCR, MOVEtoCCR);
                _handlers.Add(OpHandlerID.MOVEtoSR, MOVEtoSR);
                _handlers.Add(OpHandlerID.NEGX, NEGX);
                _handlers.Add(OpHandlerID.CLR, CLR);
                _handlers.Add(OpHandlerID.NEG, NEG);
                _handlers.Add(OpHandlerID.NOT, NOT);
                _handlers.Add(OpHandlerID.EXT, EXT);
                _handlers.Add(OpHandlerID.SWAP, SWAP);
                _handlers.Add(OpHandlerID.PEA, PEA);
                _handlers.Add(OpHandlerID.ILLEGAL, ILLEGAL);
                _handlers.Add(OpHandlerID.TST, TST);
                _handlers.Add(OpHandlerID.TRAP, TRAP);
                _handlers.Add(OpHandlerID.MOVEUSP, MOVEUSP);
                _handlers.Add(OpHandlerID.RESET, RESET);
                _handlers.Add(OpHandlerID.NOP, NOP);
                _handlers.Add(OpHandlerID.RTE, RTE);
                _handlers.Add(OpHandlerID.RTS, RTS);
                _handlers.Add(OpHandlerID.TRAPV, TRAPV);
                _handlers.Add(OpHandlerID.RTR, RTR);
                _handlers.Add(OpHandlerID.JSR, JSR);
                _handlers.Add(OpHandlerID.JMP, JMP);
                _handlers.Add(OpHandlerID.LEA, LEA);
                _handlers.Add(OpHandlerID.CHK, CHK);
                _handlers.Add(OpHandlerID.ADDQ, ADDQ);
                _handlers.Add(OpHandlerID.SUBQ, SUBQ);
                _handlers.Add(OpHandlerID.Scc, Scc);
                _handlers.Add(OpHandlerID.DBcc, DBcc);
                _handlers.Add(OpHandlerID.BRA, BRA);
                _handlers.Add(OpHandlerID.BSR, BSR);
                _handlers.Add(OpHandlerID.Bcc, Bcc);
                _handlers.Add(OpHandlerID.MOVEQ, MOVEQ);
                _handlers.Add(OpHandlerID.DIVU, DIVU);
                _handlers.Add(OpHandlerID.DIVS, DIVS);
                _handlers.Add(OpHandlerID.OR, OR);
                _handlers.Add(OpHandlerID.SUB, SUB);
                _handlers.Add(OpHandlerID.SUBX, ADDX_SUBX);
                _handlers.Add(OpHandlerID.SUBA, SUBA);
                _handlers.Add(OpHandlerID.EOR, EOR);
                _handlers.Add(OpHandlerID.CMPM, CMPM);
                _handlers.Add(OpHandlerID.CMP, CMP);
                _handlers.Add(OpHandlerID.CMPA, CMPA);
                _handlers.Add(OpHandlerID.MULU, MULU);
                _handlers.Add(OpHandlerID.MULS, MULS);
                _handlers.Add(OpHandlerID.EXG, EXG);
                _handlers.Add(OpHandlerID.AND, AND);
                _handlers.Add(OpHandlerID.ADD, ADD);
                _handlers.Add(OpHandlerID.ADDX, ADDX_SUBX);
                _handlers.Add(OpHandlerID.ADDA, ADDA);
                _handlers.Add(OpHandlerID.ASL, ASL_ASR_LSL_LSR);
                _handlers.Add(OpHandlerID.ASR, ASL_ASR_LSL_LSR);
                _handlers.Add(OpHandlerID.LSL, ASL_ASR_LSL_LSR);
                _handlers.Add(OpHandlerID.LSR, ASL_ASR_LSL_LSR);
                _handlers.Add(OpHandlerID.ROL, ROL_ROR_ROXL_ROXR);
                _handlers.Add(OpHandlerID.ROR, ROL_ROR_ROXL_ROXR);
                _handlers.Add(OpHandlerID.ROXL, ROL_ROR_ROXL_ROXR);
                _handlers.Add(OpHandlerID.ROXR, ROL_ROR_ROXL_ROXR);
                _handlers.Add(OpHandlerID.BTST, BTST_BCHG_BCLR_BSET);
                _handlers.Add(OpHandlerID.BCHG, BTST_BCHG_BCLR_BSET);
                _handlers.Add(OpHandlerID.BCLR, BTST_BCHG_BCLR_BSET);
                _handlers.Add(OpHandlerID.BSET, BTST_BCHG_BCLR_BSET);
                _handlers.Add(OpHandlerID.LINK, LINK);
                _handlers.Add(OpHandlerID.UNLK, UNLK);
                _handlers.Add(OpHandlerID.STOP, STOP);
                _handlers.Add(OpHandlerID.TAS, TAS);
                _handlers.Add(OpHandlerID.ABCD, ABCD_SBCD);
                _handlers.Add(OpHandlerID.SBCD, ABCD_SBCD);
                _handlers.Add(OpHandlerID.NBCD, NBCD);
                _handlers.Add(OpHandlerID.MOVEP, MOVEP);
                _handlers.Add(OpHandlerID.MOVEM, MOVEM);
                _handlers.Add(OpHandlerID.LINEA, LINEA);
                _handlers.Add(OpHandlerID.LINEF, LINEF);
            }

            /// <summary>
            /// Get a sized operand data value.
            /// </summary>
            /// <param name="size">The size of the operand data (Word or Long).</param>
            /// <param name="ext1">The first extension word value (can be null).</param>
            /// <param name="ext2">The second extension word value (can be null).</param>
            /// <returns>A 32-bit value containing the operand data (or null if no value is available).</returns>
            internal static uint GetSizedOperandValue(OpSize size, ushort? ext1, ushort? ext2)
            {
                uint? value = null;
                if (ext1.HasValue)
                {
                    if (size == OpSize.Long)
                    {
                        if (ext2.HasValue)
                        {
                            value = (uint)((ext1.Value << 16) + ext2.Value);
                        }
                    }
                    else
                    {
                        value = ext1.Value;
                    }
                }

                if (value == null)
                {
                    throw Helpers.CreateTRAPException(TrapVector.IllegalInstruction);
                }
                return value.Value;
            }

            /// <summary>
            /// Sets the state of the flags for the specified instruction type based on supplied result, source, and destination values.
            /// </summary>
            /// <param name="opID">The type of the instruction.</param>
            /// <param name="size">The size of the result, source, and destination values.</param>
            /// <param name="result">The result of the operation performed by the instruction.</param>
            /// <param name="source">The source value supplied to the operation performed by the instruction.</param>
            /// <param name="dest">The destination value supplied to the operation performed by the instruction.</param>
            private void SetFlags(OpHandlerID opID, OpSize size, uint result, uint source = 0, uint dest = 0)
            {
                var msb = Helpers.SizeMSB(size);
                var mask = Helpers.SizeMask(size);
                bool srcMsbSet = (source & msb) != 0;
                bool destMsbSet = (dest & msb) != 0;
                bool resultMsbSet = (result & msb) != 0;
                switch (opID)
                {
                    case OpHandlerID.DIVU:
                    case OpHandlerID.DIVS:
                        Machine.CPU.NegativeFlag = resultMsbSet;
                        Machine.CPU.ZeroFlag = (result & mask) == 0;
                        break;
                    case OpHandlerID.ORI:
                    case OpHandlerID.ANDI:
                    case OpHandlerID.EORI:
                    case OpHandlerID.MOVEQ:
                    case OpHandlerID.OR:
                    case OpHandlerID.EOR:
                    case OpHandlerID.MULU:
                    case OpHandlerID.MULS:
                    case OpHandlerID.AND:
                        Machine.CPU.NegativeFlag = resultMsbSet;
                        Machine.CPU.ZeroFlag = (result & mask) == 0;
                        Machine.CPU.OverflowFlag = false;
                        Machine.CPU.CarryFlag = false;
                        break;
                    case OpHandlerID.SUBI:
                    case OpHandlerID.SUBQ:
                    case OpHandlerID.SUB:
                        Machine.CPU.NegativeFlag = resultMsbSet;
                        Machine.CPU.ZeroFlag = (result & mask) == 0;
                        Machine.CPU.OverflowFlag = (srcMsbSet == resultMsbSet) && (destMsbSet != resultMsbSet);
                        Machine.CPU.CarryFlag = (srcMsbSet && resultMsbSet) || (srcMsbSet && !destMsbSet) || (!destMsbSet && resultMsbSet);
                        Machine.CPU.ExtendFlag = Machine.CPU.CarryFlag;
                        break;
                    case OpHandlerID.SUBX:
                        Machine.CPU.NegativeFlag = resultMsbSet;
                        if ((result & mask) != 0) Machine.CPU.ZeroFlag = false;
                        Machine.CPU.OverflowFlag = (srcMsbSet == resultMsbSet) && (destMsbSet != resultMsbSet);
                        Machine.CPU.CarryFlag = (srcMsbSet && resultMsbSet) || (srcMsbSet && !destMsbSet) || (!destMsbSet && resultMsbSet);
                        Machine.CPU.ExtendFlag = Machine.CPU.CarryFlag;
                        break;
                    case OpHandlerID.ADDI:
                    case OpHandlerID.ADDQ:
                    case OpHandlerID.ADD:
                        Machine.CPU.NegativeFlag = resultMsbSet;
                        Machine.CPU.ZeroFlag = (result & mask) == 0;
                        Machine.CPU.OverflowFlag = (srcMsbSet == destMsbSet) && (srcMsbSet != resultMsbSet);
                        Machine.CPU.CarryFlag = (srcMsbSet && !resultMsbSet) || (srcMsbSet && destMsbSet) || (destMsbSet && !resultMsbSet);
                        Machine.CPU.ExtendFlag = Machine.CPU.CarryFlag;
                        break;
                    case OpHandlerID.ADDX:
                        Machine.CPU.NegativeFlag = resultMsbSet;
                        if ((result & mask) != 0) Machine.CPU.ZeroFlag = false;
                        Machine.CPU.OverflowFlag = (srcMsbSet == destMsbSet) && (srcMsbSet != resultMsbSet);
                        Machine.CPU.CarryFlag = (srcMsbSet && !resultMsbSet) || (srcMsbSet && destMsbSet) || (destMsbSet && !resultMsbSet);
                        Machine.CPU.ExtendFlag = Machine.CPU.CarryFlag;
                        break;
                    case OpHandlerID.CMPI:
                    case OpHandlerID.CMPM:
                    case OpHandlerID.CMP:
                    case OpHandlerID.CMPA:
                        Machine.CPU.NegativeFlag = resultMsbSet;
                        Machine.CPU.ZeroFlag = (result & mask) == 0;
                        Machine.CPU.OverflowFlag = (srcMsbSet == resultMsbSet) && (destMsbSet != resultMsbSet);
                        Machine.CPU.CarryFlag = (srcMsbSet && resultMsbSet) || (srcMsbSet && !destMsbSet) || (!destMsbSet && resultMsbSet);
                        break;
                    case OpHandlerID.NEGX:
                        Machine.CPU.NegativeFlag = resultMsbSet;
                        if ((result & mask) != 0) Machine.CPU.ZeroFlag = false;
                        Machine.CPU.OverflowFlag = srcMsbSet && resultMsbSet;
                        Machine.CPU.CarryFlag = Machine.CPU.ExtendFlag = srcMsbSet || resultMsbSet;
                        break;
                    case OpHandlerID.NEG:
                        Machine.CPU.NegativeFlag = resultMsbSet;
                        Machine.CPU.ZeroFlag = (result & mask) == 0;
                        Machine.CPU.OverflowFlag = srcMsbSet && resultMsbSet;
                        Machine.CPU.CarryFlag = Machine.CPU.ExtendFlag = (result & mask) != 0;
                        break;
                    case OpHandlerID.NOT:
                    case OpHandlerID.EXT:
                    case OpHandlerID.SWAP:
                    case OpHandlerID.TST:
                        Machine.CPU.NegativeFlag = resultMsbSet;
                        Machine.CPU.ZeroFlag = (result & mask) == 0;
                        Machine.CPU.OverflowFlag = Machine.CPU.CarryFlag = false;
                        break;
                    case OpHandlerID.ASL:
                    case OpHandlerID.ASR:
                    case OpHandlerID.LSL:
                    case OpHandlerID.LSR:
                        Machine.CPU.NegativeFlag = resultMsbSet;
                        Machine.CPU.ZeroFlag = (result & mask) == 0;
                        if (source != 0)
                        {
                            Machine.CPU.CarryFlag = Machine.CPU.ExtendFlag = dest != 0;
                        }
                        else
                        {
                            Machine.CPU.CarryFlag = false;
                        }
                        break;
                    case OpHandlerID.ROL:
                    case OpHandlerID.ROR:
                    case OpHandlerID.ROXL:
                    case OpHandlerID.ROXR:
                    case OpHandlerID.MOVE:
                    default:
                        // For these operations, the op handler is responsible for setting the flags as needed.
                        break;
                }
            }

            /// <summary>
            /// Perform a Binary Coded Decimal (BCD) operation (i.e. addition or subtraction).
            /// ABCD: dest = dest + src + X
            /// SBCD: dest = dest - src - X
            /// This implementation matches the Rust/Snow emulator's 68000 behavior.
            /// </summary>
            /// <param name="opType">The type of BCD operation (Addition or Subtraction).</param>
            /// <param name="src">The source value to be used in the calculation.</param>
            /// <param name="dest">The destination value to be used in the calculation.</param>
            /// <returns>The result of the BCD operation.</returns>
            private uint BCDCalculation(BCDOperation opType, uint src, uint dest)
            {
                uint x = Machine.CPU.ExtendFlag ? 1u : 0;
                uint a = src & 0xFF;
                uint b = dest & 0xFF;

                // The N and V flags are undefined for this operation.
                // We only set the Z, C, and X flags.

                if (opType == BCDOperation.Addition)
                {
                    // ABCD: dest + src + X (matches Rust alu_add_bcd)
                    uint oresult = a + b + x;
                    uint result = oresult;
                    bool carry = false;
                    bool overflow = false;

                    // Check if low nibble needs adjustment:
                    // Either half-carry occurred (bit 4 changed unexpectedly)
                    // OR the low nibble result is >= 10
                    if (((a ^ b ^ oresult) & 0x10) != 0 || (oresult & 0x0F) >= 0x0A)
                    {
                        result += 0x06;
                        overflow = overflow || (((~oresult & 0x80) & (result & 0x80)) != 0);
                    }

                    // Check if high nibble needs adjustment:
                    // Result (after low nibble correction) >= 0xA0
                    if (result >= 0xA0)
                    {
                        uint r = result;
                        result += 0x60;
                        carry = true;
                        overflow = overflow || (((~r & 0x80) & (result & 0x80)) != 0);
                    }

                    Machine.CPU.NegativeFlag = (result & 0x80) != 0;
                    Machine.CPU.OverflowFlag = overflow;
                    Machine.CPU.CarryFlag = Machine.CPU.ExtendFlag = carry;
                    if ((result & 0xFF) != 0)
                    {
                        Machine.CPU.ZeroFlag = false;
                    }
                    return result & 0xFF;
                }
                else
                {
                    // SBCD: dest - src - X (matches Rust alu_sub_bcd)
                    // Note: In Rust, a=dest (minuend), b=src (subtrahend)
                    // So we compute: dest - src - x = b - a - x
                    uint oresult = b - a - x;
                    uint result = oresult;
                    bool carry = false;
                    bool overflow = false;

                    // Check if low nibble needs adjustment (half-borrow occurred)
                    if (((b ^ a ^ oresult) & 0x10) != 0)
                    {
                        result -= 0x06;
                        carry = ((~oresult & 0x80) & (result & 0x80)) != 0;
                        overflow = overflow || ((oresult & 0x80) & (~result & 0x80)) != 0;
                    }

                    // Check if high nibble needs adjustment (borrow occurred)
                    if ((oresult & 0x100) != 0)
                    {
                        uint r = result;
                        result -= 0x60;
                        carry = true;
                        overflow = overflow || ((r & 0x80) & (~result & 0x80)) != 0;
                    }

                    Machine.CPU.OverflowFlag = overflow;
                    Machine.CPU.NegativeFlag = (result & 0x80) != 0;
                    Machine.CPU.CarryFlag = Machine.CPU.ExtendFlag = carry;
                    if ((result & 0xFF) != 0)
                    {
                        Machine.CPU.ZeroFlag = false;
                    }
                    return result & 0xFF;
                }
            }

            /// <summary>
            /// Memory to Register functionality for the MOVEM instruction.
            /// </summary>
            /// <param name="regMask">16-bit register mask.</param>
            /// <param name="address">Address at which to start reading values into the registers.</param>
            /// <param name="size">The size of the values to be transferred (Word or Long).</param>
            /// <returns>The address at which the transfer completed.</returns>
            private uint MOVEM_MemToReg(ushort regMask, uint address, OpSize size)
            {
                uint regSize = (uint)(size == OpSize.Long ? 4 : 2);
                for (int n = 0; n < 16; n++)
                {
                    if ((regMask & _bit[n]) != 0)
                    {
                        uint value = size == OpSize.Long ? Machine.Bus.ReadLong(address).Value : (uint)Helpers.SignExtendValue(Machine.Bus.ReadWord(address).Value);
                        if (n < 8)
                        {
                            Machine.CPU.WriteDataRegister(n, value, OpSize.Long);
                        }
                        else
                        {
                            Machine.CPU.WriteAddressRegister(n - 8, value, OpSize.Long);
                        }
                        address += regSize;
                    }
                }
                return address;
            }

            /// <summary>
            /// Register to Memory functionality for the MOVEM instruction (for all but pre-decrement addressing mode).
            /// </summary>
            /// <param name="regMask">16-bit register mask.</param>
            /// <param name="address">Address at which to start writing register values.</param>
            /// <param name="size">The size of the values to be transferred (Word or Long).</param>
            private void MOVEM_RegToMem(ushort regMask, CPU cpu, uint address, OpSize size)
            {
                uint regSize = (uint)(size == OpSize.Long ? 4 : 2);

                for (int n = 0; n < 16; n++)
                {
                    if ((regMask & _bit[n]) != 0)
                    {
                        if (n < 8)
                        {
                            if (size == OpSize.Long)
                            {
                                Machine.Bus.WriteLong(address, cpu.ReadDataRegister(n));
                            }
                            else
                            {
                                Machine.Bus.WriteWord(address, (ushort)(cpu.ReadDataRegister(n) & 0x0000FFFF));
                            }
                        }
                        else
                        {
                            if (size == OpSize.Long)
                            {
                                Machine.Bus.WriteLong(address, cpu.ReadAddressRegister(n - 8));
                            }
                            else
                            {
                                Machine.Bus.WriteWord(address, (ushort)(cpu.ReadAddressRegister(n - 8) & 0x0000FFFF));
                            }
                        }
                        address += regSize;
                    }
                }
            }

            /// <summary>
            /// Register to Memory functionality for the MOVEM instruction (for pre-decrement addressing mode).
            /// </summary>
            /// <param name="regMask">16-bit register mask.</param>
            /// <param name="address">Address at which to start writing register values.</param>
            /// <param name="size">The size of the values to be transferred (Word or Long).</param>
            /// <returns>The address at which the transfer completed.</returns>
            private uint MOVEM_RegToMemPreDec(ushort regMask, CPU cpu, uint address, OpSize size)
            {
                // Increment address because it has already been pre-decremented once prior to calling this method.
                address += (uint)(size == OpSize.Long ? 4 : 2);
                for (int n = 0; n < 16; n++)
                {
                    if ((regMask & _bit[n]) != 0)
                    {
                        address -= (uint)(size == OpSize.Long ? 4 : 2);
                        if (n < 8)
                        {
                            if (size == OpSize.Long)
                            {
                                Machine.Bus.WriteLong(address, cpu.ReadAddressRegister(7 - n));
                            }
                            else
                            {
                                Machine.Bus.WriteWord(address, (ushort)(cpu.ReadAddressRegister(7 - n) & 0x0000FFFF));
                            }
                        }
                        else
                        {
                            if (size == OpSize.Long)
                            {
                                Machine.Bus.WriteLong(address, cpu.ReadDataRegister(15 - n));
                            }
                            else
                            {
                                Machine.Bus.WriteWord(address, (ushort)(cpu.ReadDataRegister(15 - n) & 0x0000FFFF));
                            }
                        }
                    }
                }
                return address;
            }

            /// <summary>
            /// Execute the specified instruction.
            /// </summary>
            /// <param name="instruction">The <see cref="Instruction"/> instance of the instruction to be executed.</param>
            public TrapException? Execute(Instruction instruction)
            {
                if (_handlers.TryGetValue(instruction.Info.HandlerID, out OpHandler? value))
                {
#if CHECK_PC_FOR_ZERO
                    uint oldPC = Machine.CPU.CurrentPC;
#endif
                    TrapException? e;
                    try
                    {
                        DeferredAddressRegisterUpdate.Clear();
                        e = value?.Invoke(instruction);          // Call the handler Action.
                        if (e?.TrapDetails.Group != 0)
                        {
                            DeferredAddressRegisterUpdate.Apply();
                        }
                    }
                    catch (TrapException ex)
                    {
                        e = ex;
                    }
#if CHECK_STACK_POINTER
                    var sr = Machine.CPU.SR;
                    var sp = (sr & SRFlags.SupervisorMode) != 0 ? Machine.CPU.SSP : Machine.CPU.USP;
                    if (sp == 0)
                    {
                        Logger.Log(LogLevel.Critical, "STACK", $"Stack Pointer == 0: PC = {Machine.CPU.CurrentPC:x8}");
                    }
#endif
#if CHECK_PC_FOR_ZERO
                    if ((Machine.CPU.CurrentPC) < 0x100)
                    {
                        Logger.Log(LogLevel.Critical, "CPU", $"PC < 0x100: original PC = {oldPC:x8}, new PC = {Machine.CPU.CurrentPC:x8}");
                        Machine.IsEndOfExecution = true;
                    }
#endif
                    return e;
                }
                return Helpers.CreateTRAPException(TrapVector.IllegalInstruction);
            }

            /// <summary>
            /// Read the specified effective address value for the supplied instruction.
            /// </summary>
            /// <param name="instruction">The <see cref="Instruction"/> instance.</param>
            /// <param name="eaType">The type of effective address data to be retrieved (Source or Destination).</param>
            /// <param name="suppressIncDec">True to suppress address register decrement or increment to prevent
            /// double inc/dec.  This is the case when there is only one effective address for an instruction but 
            /// it must be both read and written (such as NEGX).</param>
            /// <returns>32-bit value containing the effective address value (or null if no value available).</returns>
            private uint ReadEAValue(Instruction instruction, EAType eaType, bool suppressIncDec = false)
            {
                uint value;
                OpSize size = instruction.Size ?? OpSize.Word;
                var (dataRegNum, addrRegNum, address, immValue) = EvaluateEffectiveAddress(instruction, eaType, suppressIncDec);
                if (dataRegNum.HasValue)
                {
                    value = Machine.CPU.ReadDataRegister(dataRegNum.Value);
                }
                else if (addrRegNum.HasValue)
                {
                    value = Machine.CPU.ReadAddressRegister(addrRegNum.Value);
                }
                else if (address.HasValue)
                {
                    if (Machine.Debugger?.Debugging == true)
                    {
                        Machine.Debugger.DebugReadAccess(address.Value);
                    }

                    // Check for address error on odd address for word/long accesses.
                    // 
                    bool addressError = (address.Value & 1) != 0 && size != OpSize.Byte;
                    if (((!suppressIncDec || addressError) && DeferredAddressRegisterUpdate.IsPreDec(eaType) == true)
                        || addressError && size == OpSize.Word)
                    {
                        // Pre-apply the address register update for pre-dec addressing mode, or for address error on word access.
                        DeferredAddressRegisterUpdate.Apply(eaType);
                    }
                    // Access memory - may throw an Address Error or Bus Error trap exception
                    value = size switch
                    {
                        OpSize.Byte => Machine.Bus.ReadByte(address.Value).Value,
                        OpSize.Long => Machine.Bus.ReadLong(address.Value).Value,
                        _ => Machine.Bus.ReadWord(address.Value).Value,
                    };
                    if (!suppressIncDec)
                    {
                        // Apply the deferred address register update now that the bus transaction has succeeded.
                        DeferredAddressRegisterUpdate.Apply(eaType);
                    }
                }
                else if (immValue.HasValue)
                {
                    value = immValue.Value;
                }
                else
                {
                    throw Helpers.CreateTRAPException(TrapVector.IllegalInstruction);
                }

                return SizedValue(value, size);
            }

            /// <summary>
            /// Write the specified effective address value for the supplied instruction.
            /// </summary>
            /// <param name="instruction">The <see cref="Instruction"/> instance.</param>
            /// <param name="value">The data to be written.</param>
            /// <param name="eaType">The type of effective address data to be written (Source or Destination).</param>
            private void WriteEAValue(Instruction instruction, uint value, EAType eaType)
            {
                OpSize size = instruction.Size ?? OpSize.Word;
                var (dataRegNum, addrRegNum, address, immValue) = EvaluateEffectiveAddress(instruction, eaType);
                if (dataRegNum.HasValue)
                {
                    Machine.CPU.WriteDataRegister(dataRegNum.Value, value, size);
                }
                else if (addrRegNum.HasValue)
                {
                    Machine.CPU.WriteAddressRegister(addrRegNum.Value, value, size);
                }
                else if (address.HasValue)
                {
                    if (Machine.Debugger?.Debugging == true)
                    {
                        Machine.Debugger.DebugWriteAccess(address.Value);
                    }
                    if ((instruction.Info.HandlerID != OpHandlerID.MOVE || DeferredAddressRegisterUpdate.IsPreDec(eaType) == true) && eaType == EAType.Destination && size != OpSize.Long)
                    {
                        // Pre-dec always happens regardless of bus or address errors.
                        DeferredAddressRegisterUpdate.Apply(eaType);
                    }
                    // Access memory - may throw an Address Error or Bus Error trap exception
                    switch (size)
                    {
                        case OpSize.Byte:
                            Machine.Bus.WriteByte(address.Value, (byte)value);
                            break;
                        case OpSize.Word:
                            Machine.Bus.WriteWord(address.Value, (ushort)value);
                            break;
                        default:
                            Machine.Bus.WriteLong(address.Value, value);
                            break;
                    }
                    // Apply the deferred address register update now that the bus transaction has succeeded.
                    DeferredAddressRegisterUpdate.Apply(eaType);
                }
                else if (immValue.HasValue)
                {
                    Debug.Assert(false, "Immediate addressing mode cannot be used for Write operations.");
                }
            }

            /// <summary>
            /// Returns the supplied value to the specified data size. 
            /// </summary>
            /// <param name="value">The data value.</param>
            /// <param name="size">The size at which the value should be returned (Byte, Word, or Long).</param>
            /// <returns>The value after being restricted to the specified size.</returns>
            internal uint SizedValue(uint value, OpSize size)
            {
                return size switch
                {
                    OpSize.Byte => value & 0x000000FF,
                    OpSize.Word => value & 0x0000FFFF,
                    _ => value,
                };
            }

            /// <summary>
            /// Evaluate the specified effective address (EA).
            /// </summary>
            /// <param name="instruction">The <see cref="Instruction"/> instance.</param>
            /// <param name="eaType">The type of effective address to be evaluated (Source or Destination).</param>
            /// <param name="suppressIncDec">True to suppress address register decrement or increment to prevent
            /// double inc/dec.  This is the case when there is only one effective address for an instruction but 
            /// it must be both read and written (such as NEGX).</param>
            /// <returns>
            /// A tuple consisting of the following:
            ///		dataRegNum - The data register number (if the EA mode is Data Register Direct) or null if any other EA mode.
            ///		addrRegNum - The address register number (if the EA mode is Address Register Direct) or null if any other EA mode.
            ///		address - The memory address (if the EA mode is one of the indirect memory access modes) or null if any other EA mode.
            ///		immValue - The immediate operand data (if the EA mode is Immediate) or null if any other EA mode.
            /// </returns>
            internal (byte? dataRegNum, byte? addrRegNum, uint? address, uint? immValue) EvaluateEffectiveAddress(Instruction instruction, EAType eaType, bool suppressIncDec = false)
            {
                ushort? ea = eaType == EAType.Source ? instruction.SourceAddrMode : instruction.DestAddrMode;
                ushort? ext1 = eaType == EAType.Source ? instruction.SourceExtWord1 : instruction.DestExtWord1;
                ushort? ext2 = eaType == EAType.Source ? instruction.SourceExtWord2 : instruction.DestExtWord2;

                byte? dRegNum = null;
                byte? aRegNum = null;
                uint? address = null;
                uint? immVal = null;
                if (ea.HasValue)
                {
                    OpSize size = instruction.Size ?? OpSize.Word;
                    uint sizeInBytes = (uint)(size == OpSize.Byte ? 1 : size == OpSize.Long ? 4 : 2);

                    // Get register number (for addressing modes that use a register)
                    ushort regNum = (ushort)(ea & 0x0007);
                    switch (ea & 0x0038)
                    {
                        case (byte)AddrMode.DataRegister:
                            dRegNum = (byte)regNum;
                            break;
                        case (byte)AddrMode.AddressRegister:
                            aRegNum = (byte)regNum;
                            break;
                        case (byte)AddrMode.Address:
                            address = Machine.CPU.ReadAddressRegister(regNum);
                            break;
                        case (byte)AddrMode.AddressPostInc:
                            // Special case: If working with SP (i.e. A7) and a size of 1 byte then use a 2 byte increment (to keep SP address even).
                            if (regNum == 7 && sizeInBytes == 1)
                            {
                                sizeInBytes = 2;
                            }
                            address = Machine.CPU.ReadAddressRegister(regNum);
                            DeferredAddressRegisterUpdate.Set(eaType, regNum, address.Value + sizeInBytes, false);
                            break;
                        case (byte)AddrMode.AddressPreDec:
                            // Special case: If working with SP (i.e. A7) and a size of 1 byte then use a 2 byte decrement (to keep SP address even).
                            if (regNum == 7 && sizeInBytes == 1)
                            {
                                sizeInBytes = 2;
                            }
                            address = Machine.CPU.ReadAddressRegister(regNum) - sizeInBytes;
                            DeferredAddressRegisterUpdate.Set(eaType, regNum, address.Value, true);
                            break;
                        case (byte)AddrMode.AddressDisp:
                            Debug.Assert(ext1.HasValue, EXT_WORD_NOT_AVAILABLE);
                            address = (uint)((int)Machine.CPU.ReadAddressRegister(regNum) + (short)ext1.Value);
                            break;
                        case (byte)AddrMode.AddressIndex:
                            Debug.Assert(ext1.HasValue, EXT_WORD_NOT_AVAILABLE);
                            {
                                int disp = (sbyte)(byte)(ext1.Value & 0x00FF);
                                byte indexRegNum = (byte)((ext1.Value & 0x7000) >> 12);
                                OpSize indexSize = (ext1.Value & 0x0800) == 0 ? OpSize.Word : OpSize.Long;
                                bool indexIsAddressRegister = (ext1.Value & 0x8000) != 0;
                                uint indexValue = indexIsAddressRegister ?
                                    Machine.CPU.ReadAddressRegister(indexRegNum) :
                                    Machine.CPU.ReadDataRegister(indexRegNum);

                                if (indexSize == OpSize.Word)
                                {
                                    indexValue = (uint)(int)(short)(ushort)indexValue;
                                }
                                uint addressRegValue = Machine.CPU.ReadAddressRegister(regNum);
                                address = (uint)((int)addressRegValue + (int)indexValue + disp);
                            }
                            break;
                        case 0x0038:
                            switch (ea)
                            {
                                case (byte)AddrMode.AbsShort:
                                    Debug.Assert(ext1.HasValue, EXT_WORD_NOT_AVAILABLE);
                                    address = (uint)(int)(short)ext1.Value;
                                    break;
                                case (byte)AddrMode.AbsLong:
                                    Debug.Assert(ext1.HasValue && ext2.HasValue, EXT_WORD_NOT_AVAILABLE);
                                    address = (uint)((ext1.Value << 16) + ext2.Value);
                                    break;
                                case (byte)AddrMode.PCDisp:
                                    {
                                        Debug.Assert(ext1.HasValue, EXT_WORD_NOT_AVAILABLE);

                                        int pcDecrement = 2; // Assume source, PC just after ext1 or dest, PC just after ext1
                                        if (eaType == EAType.Source && instruction.DestExtWord1 != null)
                                        {
                                            pcDecrement += (instruction.DestExtWord2 == null) ? 2 : 4;
                                        }

                                        address = (uint)((int)Machine.CPU.CurrentPC - pcDecrement + (short)ext1.Value);
                                    }
                                    break;
                                case (byte)AddrMode.PCIndex:
                                    Debug.Assert(ext1.HasValue, EXT_WORD_NOT_AVAILABLE);
                                    {
                                        byte disp = (byte)(ext1.Value & 0x00FF);
                                        byte indexRegNum = (byte)((ext1.Value & 0x7000) >> 12);
                                        OpSize indexSize = (ext1.Value & 0x0800) == 0 ? OpSize.Word : OpSize.Long;
                                        bool indexIsAddress = (ext1.Value & 0x8000) != 0;
                                        uint indexValue = indexIsAddress ?
                                            Machine.CPU.ReadAddressRegister(indexRegNum) :
                                            Machine.CPU.ReadDataRegister(indexRegNum);
                                        if (indexSize == OpSize.Word)
                                        {
                                            indexValue = (uint)(int)(short)(ushort)indexValue;
                                        }
                                        // PC has been incremented past the extension word.  The definition of
                                        // PC displacement uses the value of the extension word address as the PC value.
                                        int pcDecrement = 2; // Assume source, PC just after ext1 or dest, PC just after ext1
                                        if (eaType == EAType.Source && instruction.DestExtWord1 != null)
                                        {
                                            pcDecrement += (instruction.DestExtWord2 == null) ? 2 : 4;
                                        }
                                        address = (uint)((int)Machine.CPU.CurrentPC - pcDecrement + (int)indexValue + (sbyte)disp);                                  
                                    }
                                    break;
                                case (byte)AddrMode.Immediate:
                                    Debug.Assert(ext1.HasValue, EXT_WORD_NOT_AVAILABLE);
                                    {
                                        if (size == OpSize.Long)
                                        {
                                            Debug.Assert(ext2.HasValue, EXT_WORD_NOT_AVAILABLE);
                                            immVal = (uint)((ext1.Value << 16) + ext2.Value);
                                        }
                                        else
                                        {
                                            immVal = ext1.Value;
                                        }
                                    }
                                    break;
                            }
                            break;
                    }
                }

                if (address.HasValue)
                {
                    // Support trap handling
                    instruction.AccessAddress = address.Value;
                    instruction.AccessAddressType = eaType;
                }
                Debug.Assert(dRegNum != null || aRegNum != null || address != null || immVal != null, "EA evaluation did not yield any result.");
                return (dRegNum, aRegNum, address, immVal);
            }

            private TrapException? MustBeSupervisor(SRFlags sr)
            {
                if ((sr & SRFlags.SupervisorMode) == 0)
                {
                    return Helpers.CreateTRAPException(TrapVector.PrivilegeViolation);
                }
                return null;
            }

            // *************************
            //
            // Operation handler methods
            //
            // *************************

            private TrapException? ORItoCCR(Instruction inst)
            {
                // SourceExtWord1 holds the immediate operand value.
                if (inst.SourceExtWord1.HasValue)
                {
                    ushort value = (ushort)Machine.CPU.SR;
                    value |= (ushort)(inst.SourceExtWord1.Value & 0x001F);
                    Machine.CPU.SR = (SRFlags)value;
                }
                return null;
            }

            private TrapException? ORItoSR(Instruction inst)
            {
                var trap = MustBeSupervisor(Machine.CPU.SR);
                if (trap != null)
                {
                    return trap;
                }
                // SourceExtWord1 holds the immediate operand value.
                if (inst.SourceExtWord1.HasValue)
                {
                    ushort value = (ushort)Machine.CPU.SR;
                    value |= inst.SourceExtWord1.Value;
                    Machine.CPU.SR = (SRFlags)value;
                }
                return null;
            }

            private TrapException? ORI(Instruction inst)
            {
                OpSize opSize = inst.Size ?? OpSize.Word;
                uint value = GetSizedOperandValue(opSize, inst.SourceExtWord1, inst.SourceExtWord2);
                var destValue = ReadEAValue(inst, EAType.Destination, suppressIncDec: true);
                destValue |= value;
                WriteEAValue(inst, destValue, EAType.Destination);
                SetFlags(inst.Info.HandlerID, opSize, destValue);
                return null;
            }

            private TrapException? ANDItoCCR(Instruction inst)
            {
                ushort value = (ushort)Machine.CPU.SR;
                if (!inst.SourceExtWord1.HasValue)
                {
                    Helpers.RaiseTRAPException(TrapVector.IllegalInstruction);
                }
                value &= (ushort)((inst.SourceExtWord1!.Value & 0x001F) | 0xFFE0);
                Machine.CPU.SR = (SRFlags)value;
                return null;
            }

            private TrapException? ANDItoSR(Instruction inst)
            {
                var trap = MustBeSupervisor(Machine.CPU.SR);
                if (trap != null)
                {
                    return trap;
                }
                ushort value = (ushort)Machine.CPU.SR;
                if (!inst.SourceExtWord1.HasValue)
                {
                    Helpers.RaiseTRAPException(TrapVector.IllegalInstruction);
                }
                value &= inst.SourceExtWord1!.Value;
                Machine.CPU.SR = (SRFlags)value;
                return null;
            }

            private TrapException? ANDI(Instruction inst)
            {
                OpSize opSize = inst.Size ?? OpSize.Word;
                uint value = GetSizedOperandValue(opSize, inst.SourceExtWord1, inst.SourceExtWord2);
                var destValue = ReadEAValue(inst, EAType.Destination, suppressIncDec: true);
                destValue &= value;
                WriteEAValue(inst, destValue, EAType.Destination);
                SetFlags(inst.Info.HandlerID, opSize, destValue);
                return null;
            }

            private TrapException? SUBI(Instruction inst)
            {
                OpSize opSize = inst.Size ?? OpSize.Word;
                uint srcValue = GetSizedOperandValue(opSize, inst.SourceExtWord1, inst.SourceExtWord2);
                var destValue = ReadEAValue(inst, EAType.Destination, suppressIncDec: true);
                uint result = destValue - srcValue;
                WriteEAValue(inst, result, EAType.Destination);
                SetFlags(inst.Info.HandlerID, opSize, result, srcValue, destValue);
                return null;
            }

            private TrapException? ADDI(Instruction inst)
            {
                OpSize opSize = inst.Size ?? OpSize.Word;
                uint? srcValue = GetSizedOperandValue(opSize, inst.SourceExtWord1, inst.SourceExtWord2);
                if (srcValue.HasValue)
                {
                    var destValue = ReadEAValue(inst, EAType.Destination, suppressIncDec: true);
                    uint result = destValue + srcValue.Value;
                    WriteEAValue(inst, result, EAType.Destination);
                    SetFlags(inst.Info.HandlerID, opSize, result, srcValue.Value, destValue);
                }
                return null;
            }

            private TrapException? EORItoCCR(Instruction inst)
            {
                // SourceExtWord1 holds the immediate operand value.
                if (!inst.SourceExtWord1.HasValue)
                {
                    Helpers.RaiseTRAPException(TrapVector.IllegalInstruction);
                }
                ushort value = (ushort)Machine.CPU.SR;
                value ^= (ushort)(inst.SourceExtWord1!.Value & 0x001F);
                Machine.CPU.SR = (SRFlags)value;
                return null;
            }

            private TrapException? EORItoSR(Instruction inst)
            {
                var trap = MustBeSupervisor(Machine.CPU.SR);
                if (trap != null)
                {
                    return trap;
                }
                // SourceExtWord1 holds the immediate operand value.
                if (!inst.SourceExtWord1.HasValue)
                {
                    Helpers.RaiseTRAPException(TrapVector.IllegalInstruction);
                }
                ushort value = (ushort)Machine.CPU.SR;
                value ^= inst.SourceExtWord1!.Value;
                Machine.CPU.SR = (SRFlags)value;
                return null;
            }

            private TrapException? EORI(Instruction inst)
            {
                OpSize opSize = inst.Size ?? OpSize.Word;
                uint value = GetSizedOperandValue(opSize, inst.SourceExtWord1, inst.SourceExtWord2);
                var destValue = ReadEAValue(inst, EAType.Destination, suppressIncDec: true);
                destValue ^= value;
                WriteEAValue(inst, destValue, EAType.Destination);
                SetFlags(inst.Info.HandlerID, opSize, destValue);
                return null;
            }

            private TrapException? CMPI(Instruction inst)
            {
                OpSize opSize = inst.Size ?? OpSize.Word;
                uint srcValue = GetSizedOperandValue(opSize, inst.SourceExtWord1, inst.SourceExtWord2);
                var destValue = ReadEAValue(inst, EAType.Destination);
                uint result = destValue - srcValue;
                SetFlags(inst.Info.HandlerID, opSize, result, srcValue, destValue);
                return null;
            }

            private TrapException? MOVE(Instruction inst)
            {
                OpSize size = inst.Size ?? OpSize.Word;
                uint value = ReadEAValue(inst, EAType.Source);

                Machine.CPU.CarryFlag = false;
                Machine.CPU.OverflowFlag = false;
                if (size != OpSize.Long)
                {
                    Machine.CPU.ZeroFlag = value == 0;
                    Machine.CPU.NegativeFlag = (value & Helpers.SizeMSB(size)) != 0;
                }

                WriteEAValue(inst, value, EAType.Destination);

                if (size == OpSize.Long)
                {
                    // For long moves, flags are set after the write in case of address error.
                    Machine.CPU.ZeroFlag = value == 0;
                    Machine.CPU.NegativeFlag = (value & 0x80000000) != 0;
                }

                return null;
            }

            private TrapException? MOVEA(Instruction inst)
            {
                uint value = ReadEAValue(inst, EAType.Source);
                if (inst.Size == OpSize.Word)
                {
                    // Sign-extend
                    value = (uint)Helpers.SignExtendValue(value);
                }
                OpSize size = OpSize.Long; // MOVEA always moves full 32 bits for address register
                int regNum = (inst.Opcode & 0x0E00) >> 9;
                Machine.CPU.WriteAddressRegister(regNum, value, size);
                return null;
            }

            private TrapException? MOVEfromSR(Instruction inst)
            {
                WriteEAValue(inst, (uint)Machine.CPU.SR, EAType.Destination);
                return null;
            }

            private TrapException? MOVEtoCCR(Instruction inst)
            {
                var value = ReadEAValue(inst, EAType.Source);
                ushort srValue = (ushort)Machine.CPU.SR;
                srValue = (ushort)((srValue & 0xFF00) | ((ushort)value & 0x00FF));
                // Note: Setter masks out unimplemented bits.
                Machine.CPU.SR = (SRFlags)srValue;
                return null;
            }

            private TrapException? MOVEtoSR(Instruction inst)
            {
                var trap = MustBeSupervisor(Machine.CPU.SR);
                if (trap != null)
                {
                    return trap;
                }
                var value = ReadEAValue(inst, EAType.Source);
                // Note: Setter masks out unimplemented bits.
                Machine.CPU.SR = (SRFlags)((ushort)value);
                return null;
            }

            private TrapException? NEGX(Instruction inst)
            {
                var value = ReadEAValue(inst, EAType.Destination, suppressIncDec: true);
                OpSize size = inst.Size ?? OpSize.Word;
                int val = Helpers.SignExtendValue(value, size);
                int result = 0 - (val + (Machine.CPU.ExtendFlag ? 1 : 0));
                WriteEAValue(inst, (uint)result, EAType.Destination);
                SetFlags(inst.Info.HandlerID, size, (uint)result, value);
                return null;
            }

            private TrapException? CLR(Instruction inst)
            {
                ReadEAValue(inst, EAType.Destination, suppressIncDec: true);
                WriteEAValue(inst, 0, EAType.Destination);
                Machine.CPU.ZeroFlag = true;
                Machine.CPU.NegativeFlag = Machine.CPU.OverflowFlag = Machine.CPU.CarryFlag = false;
                return null;
            }

            private TrapException? NEG(Instruction inst)
            {
                var value = ReadEAValue(inst, EAType.Destination, suppressIncDec: true);
                OpSize size = inst.Size ?? OpSize.Word;
                int val = Helpers.SignExtendValue(value, size);
                int result = 0 - val;
                WriteEAValue(inst, (uint)result, EAType.Destination);
                SetFlags(inst.Info.HandlerID, size, (uint)result, value);
                return null;
            }

            private TrapException? NOT(Instruction inst)
            {
                var value = ReadEAValue(inst, EAType.Destination, suppressIncDec: true);
                OpSize size = inst.Size ?? OpSize.Word;
                int val = Helpers.SignExtendValue(value, size);
                int result = ~val;
                WriteEAValue(inst, (uint)result, EAType.Destination);
                SetFlags(inst.Info.HandlerID, size, (uint)result, value);
                return null;
            }

            private TrapException? EXT(Instruction inst)
            {
                byte regNum = (byte)(inst.Opcode & 0x0007);
                OpSize size = (inst.Opcode & 0x0040) == 0 ? OpSize.Word : OpSize.Long;
                uint value = Machine.CPU.ReadDataRegister(regNum);
                int extValue = Helpers.SignExtendValue(value, size == OpSize.Word ? OpSize.Byte : OpSize.Word);
                Machine.CPU.WriteDataRegister(regNum, (uint)extValue, size);
                SetFlags(inst.Info.HandlerID, size, (uint)extValue);
                return null;
            }

            private TrapException? SWAP(Instruction inst)
            {
                byte regNum = (byte)(inst.Opcode & 0x0007);
                uint value = Machine.CPU.ReadDataRegister(regNum);
                uint lowerWord = value & 0x0000FFFF;
                value = (value >> 16) | (lowerWord << 16);
                Machine.CPU.WriteDataRegister(regNum, value, OpSize.Long);
                SetFlags(inst.Info.HandlerID, OpSize.Long, value);
                return null;
            }

            private TrapException? PEA(Instruction inst)
            {
                var (_, _, address, _) = EvaluateEffectiveAddress(inst, EAType.Source);
                if (address.HasValue)
                {
                    Machine.PushLong(address.Value);
                }
                return null;
            }

            private TrapException? ILLEGAL(Instruction inst)
            {
                return Helpers.CreateTRAPException(TrapVector.IllegalInstruction);
            }

            private TrapException? LINEA(Instruction inst)
            {
                return Helpers.CreateTRAPException(TrapVector.LineAInstruction);
            }

            private TrapException? LINEF(Instruction inst)
            {
                return Helpers.CreateTRAPException(TrapVector.LineFInstruction);
            }

            private TrapException? TST(Instruction inst)
            {
                var value = ReadEAValue(inst, EAType.Destination);
                OpSize size = inst.Size ?? OpSize.Word;
                SetFlags(inst.Info.HandlerID, size, value);
                return null;
            }

            private TrapException? TRAP(Instruction inst)
            {
                ushort vector = (ushort)(inst.Opcode & 0x000F);
                return Helpers.CreateTRAPException((ushort)(vector + 32));
            }

            private TrapException? MOVEUSP(Instruction inst)
            {
                if (!Machine.CPU.SupervisorMode)
                {
                    return Helpers.CreateTRAPException(TrapVector.PrivilegeViolation);
                }
                byte regNum = (byte)(inst.Opcode & 0x0007);
                if ((inst.Opcode & 0x0008) == 0)
                {
                    Machine.CPU.USP = Machine.CPU.ReadAddressRegister(regNum);
                }
                else
                {
                    Machine.CPU.WriteAddressRegister(regNum, Machine.CPU.USP);
                }
                return null;
            }

            private TrapException? RESET(Instruction inst)
            {
                if (Machine.CPU.SupervisorMode)
                {
                    Machine.CPU.ResetExternalDevices();
                }
                else
                {
                    return Helpers.CreateTRAPException(TrapVector.PrivilegeViolation);
                }
                return null;
            }

            private TrapException? NOP(Instruction _)
            {
                // No operation to be performed for NOP (obviously!)
                return null;
            }

            private TrapException? RTE(Instruction inst)
            {
                TrapException? exception = Machine.CheckUnalignedStackAccess(EAType.Source);
                if (exception != null) return exception;

                if (Machine.CPU.SupervisorMode)
                {
                    ushort sr = Machine.PopWord();
                    uint address = Machine.PopLong();

                    Machine.CPU.SR = (SRFlags)sr;

                    Machine.SetPC(address);
                }
                else
                {
                    return Helpers.CreateTRAPException(TrapVector.PrivilegeViolation);
                }
                return null;
            }

            private TrapException? RTS(Instruction inst)
            {
                TrapException? exception = Machine.CheckUnalignedStackAccess(EAType.Source);
                if (exception != null) return exception;

                // if no JSR/BSR instruction has been executed then this RTS marks the termination of the code execution.
                if (CallDepth == 0)
                {
                    if (Machine.EndWhenCallDepthIsZero)
                    {
                        Machine.IsEndOfExecution = true;
                        return null;
                    }
                    else
                    {
                        // Keep call depth from going negative
                        CallDepth++;
                    }
                }
                uint address = Machine.PopLong();
                Machine.SetPC(address);
                CallDepth--;
                return null;
            }

            private TrapException? TRAPV(Instruction _)
            {
                if (Machine.CPU.OverflowFlag)
                {
                    return Helpers.CreateTRAPException(TrapVector.TRAPVInstruction);
                }
                return null;
            }

            private TrapException? RTR(Instruction inst)
            {
                TrapException? exception = Machine.CheckUnalignedStackAccess(EAType.Source);
                if (exception != null) return exception;

                ushort ccr = Machine.PopWord();
                ushort srValue = (ushort)Machine.CPU.SR;
                srValue = (ushort)((srValue & 0xFFE0) | (ccr & 0x001F));
                Machine.CPU.SR = (SRFlags)srValue;
                uint address = Machine.PopLong();

                Machine.SetPC(address);
                return null;
            }

            private TrapException? JSR(Instruction inst)
            {
                var (_, _, address, _) = EvaluateEffectiveAddress(inst, EAType.Source);
                if (address.HasValue)
                {
                    uint pc = Machine.CPU.CurrentPC;
                    Machine.SetPC(address.Value);  // Throws address error if odd address
                    Machine.PushLong(pc);

                    CallDepth++;
                }
                return null;
            }

            private TrapException? JMP(Instruction inst)
            {
                var (_, _, address, _) = EvaluateEffectiveAddress(inst, EAType.Source);
                if (address.HasValue)
                {
                    Machine.SetPC(address.Value);
                }
                return null;
            }

            private TrapException? LEA(Instruction inst)
            {
                var (_, _, address, _) = EvaluateEffectiveAddress(inst, EAType.Source);
                if (address.HasValue)
                {
                    int regNum = (inst.Opcode & 0x0E00) >> 9;
                    Machine.CPU.WriteAddressRegister(regNum, address.Value, OpSize.Long);
                }
                return null;
            }

            private TrapException? CHK(Instruction inst)
            {
                var value = ReadEAValue(inst, EAType.Source);
                OpSize size = inst.Size ?? OpSize.Word;
                int regNum = (inst.Opcode & 0x0E00) >> 9;
                uint dRegValue = Machine.CPU.ReadDataRegister(regNum);
                int signedDVal = Helpers.SignExtendValue(dRegValue, size);
                int signedEAVal = Helpers.SignExtendValue(value, size);
                Machine.CPU.SR = Machine.CPU.SR & ~(SRFlags.Negative | SRFlags.Zero | SRFlags.Carry | SRFlags.Overflow);
                if (signedDVal < 0)
                {
                    Machine.CPU.NegativeFlag = true;
                    return Helpers.CreateTRAPException(TrapVector.CHKInstruction);
                }
                else if (signedDVal > signedEAVal)
                {
                    Machine.CPU.NegativeFlag = false;
                    return Helpers.CreateTRAPException(TrapVector.CHKInstruction);
                }
                return null;
            }

            private TrapException? ADDQ(Instruction inst)
            {
                int addVal = (inst.Opcode & 0x0E00) >> 9;
                if (addVal == 0)
                {
                    addVal = 8;
                }
                uint result;

                // When being applied to an address register, we work with the entire 32-bit value regardless
                // of the size that has been specified. This operation also doesn't affect the flags.
                if ((inst.Opcode & 0x0038) == (int)AddrMode.AddressRegister)
                {
                    int regNum = inst.Opcode & 0x0007;
                    uint aRegVal = Machine.CPU.ReadAddressRegister(regNum);
                    result = (uint)(aRegVal + addVal);
                    Machine.CPU.WriteAddressRegister(regNum, result);
                    return null;
                }

                var value = ReadEAValue(inst, EAType.Destination, suppressIncDec: true);
                OpSize size = inst.Size ?? OpSize.Word;
                result = (uint)(value + addVal);
                WriteEAValue(inst, result, EAType.Destination);
                SetFlags(inst.Info.HandlerID, size, result, value);
                return null;
            }

            private TrapException? SUBQ(Instruction inst)
            {
                int subVal = (inst.Opcode & 0x0E00) >> 9;
                if (subVal == 0)
                {
                    subVal = 8;
                }
                uint result;

                // When being applied to an address register, we work with the entire 32-bit value regardless
                // of the size that has been specified. This operation also doesn't affect the flags.
                if ((inst.Opcode & 0x0038) == (int)AddrMode.AddressRegister)
                {
                    int regNum = inst.Opcode & 0x0007;
                    uint aRegVal = Machine.CPU.ReadAddressRegister(regNum);
                    result = (uint)(aRegVal - subVal);
                    Machine.CPU.WriteAddressRegister(regNum, result);
                    return null;
                }

                var value = ReadEAValue(inst, EAType.Destination, suppressIncDec: true);
                OpSize size = inst.Size ?? OpSize.Word;
                result = (uint)(value - subVal);
                WriteEAValue(inst, result, EAType.Destination);
                SetFlags(inst.Info.HandlerID, size, result, (uint)subVal, value);
                return null;
            }

            private TrapException? Scc(Instruction inst)
            {
                ReadEAValue(inst, EAType.Destination, suppressIncDec: true);
                int condition = (inst.Opcode & 0x0F00) >> 8;
                if (Machine.CPU.EvaluateCondition((Condition)condition))
                {
                    WriteEAValue(inst, 0x000000FF, EAType.Destination);
                }
                else
                {
                    WriteEAValue(inst, 0x00000000, EAType.Destination);
                }
                return null;
            }

            private TrapException? DBcc(Instruction inst)
            {
                int condition = (inst.Opcode & 0x0F00) >> 8;
                if (!Machine.CPU.EvaluateCondition((Condition)condition))
                {
                    int dRegNum = inst.Opcode & 0x0007;
                    uint dRegVal = Machine.CPU.ReadDataRegister(dRegNum);
                    int newDRegVal = Helpers.SignExtendValue(dRegVal, OpSize.Word) - 1;
                    Machine.CPU.WriteDataRegister(dRegNum, (uint)newDRegVal, OpSize.Word);
                    if (newDRegVal != -1)
                    {
                        // Note: extra -2 to account for PC pointing at the next instruction, not on the extension word for the
                        // current instruction (as the displacement for DBcc instructions assumes)
                        if (inst.SourceExtWord1.HasValue)
                        {
                            int disp = Helpers.SignExtendValue(inst.SourceExtWord1.Value, OpSize.Word) - 2;
                            uint address = (uint)(Machine.CPU.CurrentPC + disp);
                            if ((address & 1) != 0)
                            {
                                // Restore the register
                                Machine.CPU.WriteDataRegister(dRegNum, (uint)(newDRegVal + 1), OpSize.Word);

                                Machine.CurrentInstruction.AccessAddress = address;
                                Machine.CurrentInstruction.AccessAddressType = EAType.Source;
                                Helpers.RaiseTRAPException(TrapVector.AddressError);
                            }
                            Machine.SetPC(address);
                        }
                    }
                }
                return null;
            }

            private TrapException? BRA(Instruction inst)
            {
                uint pc = Machine.CPU.CurrentPC;
                int disp = inst.Opcode & 0x00FF;
                if (disp == 0)
                {
                    if (inst.SourceExtWord1.HasValue)
                    {
                        // Byte displacement is zero so use the extension word value as a 16-bit displacement.
                        disp = Helpers.SignExtendValue(inst.SourceExtWord1.Value, OpSize.Word);

                        // Step PC back a word as it should be pointing immediately after the instruction opcode word
                        // for the displacement to be correct (whereas it will currently be pointing at the location immediately
                        // after the extension word)
                        pc -= 2;
                    }
                }
                else
                {
                    disp = Helpers.SignExtendValue((uint)disp, OpSize.Byte);
                }

                uint address = (uint)(pc + disp);
                Machine.SetPC(address);
                return null;
            }

            private TrapException? BSR(Instruction inst)
            {
                uint pc = Machine.CPU.CurrentPC;
                int disp = inst.Opcode & 0x00FF;
                if (disp == 0 && inst.SourceExtWord1.HasValue)
                {
                    // Byte displacement is zero so use the extension word value as a 16-bit displacement.
                    disp = Helpers.SignExtendValue(inst.SourceExtWord1.Value, OpSize.Word);

                    // Step PC back a word as it should be pointing immediately after the instruction opcode word
                    // for the displacement to be correct (whereas it will currently be pointing at the location immediately
                    // after the extension word)
                    pc -= 2;
                }
                else
                {
                    disp = Helpers.SignExtendValue((uint)disp, OpSize.Byte);
                }

                Machine.PushLong(Machine.CPU.CurrentPC);
                uint address = (uint)(pc + disp);
                if ((address & 1) != 0)
                {
                    Machine.CurrentInstruction.AccessAddress = address;
                    Machine.CurrentInstruction.AccessAddressType = EAType.Source;
                    Helpers.RaiseTRAPException(TrapVector.AddressError);
                }

                Machine.SetPC(address);
                CallDepth++;
                return null;
            }

            private TrapException? Bcc(Instruction inst)
            {
                int condition = (inst.Opcode & 0x0F00) >> 8;
                if (Machine.CPU.EvaluateCondition((Condition)condition))
                {
                    uint pc = Machine.CPU.CurrentPC;
                    int disp = inst.Opcode & 0x00FF;
                    if (disp == 0 && inst.SourceExtWord1.HasValue)
                    {
                        // Byte displacement is zero so use the extension word value as a 16-bit displacement.
                        disp = Helpers.SignExtendValue(inst.SourceExtWord1.Value, OpSize.Word);

                        // Step PC back a word as it should be pointing immediately after the instruction opcode word
                        // for the displacement to be correct (whereas it will currently be pointing at the location immediately
                        // after the extension word)
                        pc -= 2;
                    }
                    else
                    {
                        disp = Helpers.SignExtendValue((uint)disp, OpSize.Byte);
                    }
                    uint address = (uint)(pc + disp);
                    Machine.SetPC(address);
                }
                return null;
            }

            private TrapException? MOVEQ(Instruction inst)
            {
                int dRegNum = (inst.Opcode & 0x0E00) >> 9;
                int data = Helpers.SignExtendValue((uint)(inst.Opcode & 0x00FF), OpSize.Byte);
                Machine.CPU.WriteDataRegister(dRegNum, (uint)data);
                SetFlags(inst.Info.HandlerID, OpSize.Long, (uint)data);
                return null;
            }

            /// <summary>
            /// Operation: Destination ÷ Source → Destination 
            /// 
            /// Assembler Syntax:  DIVU.W  <ea>,Dn32/16 → 16r – 16q
            ///
            /// Description: Divides the unsigned destination operand by the unsigned source operand and
            /// stores the unsigned result in the destination. The instruction divides 
            /// a long word by a word.The result is a quotient in the lower word (least significant 
            /// 16 bits) and a remainder in the upper word(most significant 16 bits).
            /// 
            /// Two special conditions may arise during the operation: 
            /// 
            ///     1. Division by zero causes a trap. 
            /// 
            ///     2. Overflow may be detected and set before the instruction completes. If the instruction 
            ///     detects an overflow, it sets the overflow condition code, and the operands are unaffected.
            /// 
            /// Condition codes:
            /// 
            ///     X — Not affected. 
            ///     N — Set if the quotient is negative; cleared otherwise; undefined if overflow or divide
            ///         by zero occurs.
            ///     Z — Set if the quotient is zero; cleared otherwise; undefined if overflow or divide by
            ///         zero occurs.
            ///     V — Set if division overflow occurs; undefined if divide by zero occurs; cleared otherwise.
            ///     C — Always cleared. 
            /// </summary>
            /// <param name="inst"></param>
            /// <returns></returns>
            private TrapException? DIVU(Instruction inst)
            {
                int dRegNum = (inst.Opcode & 0x0E00) >> 9;
                uint dividend = Machine.CPU.ReadDataRegister(dRegNum);
                uint divisor = ReadEAValue(inst, EAType.Source);

                Machine.CPU.CarryFlag = false;
                Machine.CPU.ZeroFlag = false;
                Machine.CPU.OverflowFlag = false;
                Machine.CPU.NegativeFlag = false;

                if (divisor == 0)
                {
                    return Helpers.CreateTRAPException(TrapVector.DivideByZero);
                }

                uint quotient = dividend / divisor;
                if (quotient > ushort.MaxValue)
                {
                    Machine.CPU.OverflowFlag = true;
                    Machine.CPU.NegativeFlag = true;
                    return null;
                }

                uint remainder = dividend % divisor;
                uint result = ((quotient & 0xFFFF) | (remainder << 16));
                Machine.CPU.WriteDataRegister(dRegNum, result);
                SetFlags(inst.Info.HandlerID, OpSize.Word, quotient);
                return null;
            }

            /// <summary>
            /// Operation: Destination ÷ Source → Destination 
            /// 
            /// Assembler Syntax:  DIVS.W  <ea>,Dn32/16 → 16r – 16q
            ///
            /// Description: Divides the signed destination operand by the signed source operand and
            /// stores the signed result in the destination. The instruction divides 
            /// a long word by a word.The result is a quotient in the lower word (least significant 
            /// 16 bits) and a remainder in the upper word(most significant 16 bits). The sign of the 
            /// remainder is the same as the sign of the dividend.
            /// 
            /// Two special conditions may arise during the operation: 
            /// 
            ///     1. Division by zero causes a trap. 
            /// 
            ///     2. Overflow may be detected and set before the instruction completes. If the instruction 
            ///     detects an overflow, it sets the overflow condition code, and the operands are unaffected.
            /// 
            /// Condition codes:
            /// 
            ///     X — Not affected. 
            ///     N — Set if the quotient is negative; cleared otherwise; undefined if overflow or divide
            ///         by zero occurs.
            ///     Z — Set if the quotient is zero; cleared otherwise; undefined if overflow or divide by
            ///         zero occurs.
            ///     V — Set if division overflow occurs; undefined if divide by zero occurs; cleared otherwise.
            ///     C — Always cleared. 
            /// </summary>
            /// <param name="inst"></param>
            /// <returns></returns>
            private TrapException? DIVS(Instruction inst)
            {
                int dRegNum = (inst.Opcode & 0x0E00) >> 9;
                int dividend = (int)Machine.CPU.ReadDataRegister(dRegNum);
                int divisor = Helpers.SignExtendValue(ReadEAValue(inst, EAType.Source), OpSize.Word);

                Machine.CPU.CarryFlag = false;
                Machine.CPU.ZeroFlag = false;
                Machine.CPU.OverflowFlag = false;
                Machine.CPU.NegativeFlag = false;

                if (divisor == 0)
                {
                    return Helpers.CreateTRAPException(TrapVector.DivideByZero);
                }

                int quotient = dividend / divisor;
                if (quotient < short.MinValue || quotient > short.MaxValue)
                {
                    Machine.CPU.OverflowFlag = true;
                    Machine.CPU.NegativeFlag = true;
                    return null;
                }

                int remainder = dividend % divisor; // Sign of remainder = sign of dividend
                uint result = (uint)((quotient & 0xFFFF) | (remainder << 16));
                Machine.CPU.WriteDataRegister(dRegNum, result);
                SetFlags(inst.Info.HandlerID, OpSize.Word, (uint)quotient);
                return null;
            }

            private TrapException? OR(Instruction inst)
            {
                int dRegNum = (inst.Opcode & 0x0E00) >> 9;
                uint dRegVal = Machine.CPU.ReadDataRegister(dRegNum);

                // Is D(n) the destination?  If so we need to do any address incrementing on the read. I.e., do NOT suppress it.
                bool dnDest = (inst.Opcode & 0x0100) == 0;
                var value = ReadEAValue(inst, EAType.Destination, !dnDest);
                var result = dRegVal | value;
                OpSize size = inst.Size ?? OpSize.Word;
                if (dnDest)
                {
                    Machine.CPU.WriteDataRegister(dRegNum, result, size);
                }
                else
                {
                    WriteEAValue(inst, result, EAType.Destination);
                }
                SetFlags(inst.Info.HandlerID, size, result);
                return null;
            }

            private TrapException? SUB(Instruction inst)
            {
                OpSize size = inst.Size ?? OpSize.Word;
                int dRegNum = (inst.Opcode & 0x0E00) >> 9;
                int dRegVal = Helpers.SignExtendValue(Machine.CPU.ReadDataRegister(dRegNum), size);

                // Is D(n) the destination?  If so we need to do any address incrementing on the read. I.e., do NOT suppress it.
                bool dnDest = (inst.Opcode & 0x0100) == 0;
                var value = ReadEAValue(inst, EAType.Destination, !dnDest);
                int signedVal = Helpers.SignExtendValue(value, size);
                var result = dnDest ? (dRegVal - signedVal) : (signedVal - dRegVal);
                if (dnDest)
                {
                    Machine.CPU.WriteDataRegister(dRegNum, (uint)result, size);
                    SetFlags(inst.Info.HandlerID, size, (uint)result, value, (uint)dRegVal);
                }
                else
                {
                    WriteEAValue(inst, (uint)result, EAType.Destination);
                    SetFlags(inst.Info.HandlerID, size, (uint)result, (uint)dRegVal, value);
                }
                return null;
            }

            private TrapException? SUBA(Instruction inst)
            {
                var value = ReadEAValue(inst, EAType.Source);
                OpSize size = inst.Size ?? OpSize.Word;
                int signedVal = Helpers.SignExtendValue(value, size);
                int regNum = (inst.Opcode & 0x0E00) >> 9;
                uint val = Machine.CPU.ReadAddressRegister(regNum);
                val = (uint)(val - signedVal);
                Machine.CPU.WriteAddressRegister(regNum, val, OpSize.Long);
                return null;
            }

            private TrapException? EOR(Instruction inst)
            {
                int dRegNum = (inst.Opcode & 0x0E00) >> 9;
                uint dRegVal = Machine.CPU.ReadDataRegister(dRegNum);
                var value = ReadEAValue(inst, EAType.Destination, suppressIncDec: true);
                var result = value ^ dRegVal;
                OpSize size = inst.Size ?? OpSize.Word;
                WriteEAValue(inst, result, EAType.Destination);
                SetFlags(inst.Info.HandlerID, size, result);
                return null;
            }

            private TrapException? CMPM(Instruction inst)
            {
                OpSize size = inst.Size ?? OpSize.Word;
                byte rYSource = (byte)(inst.Opcode & 0x0007);         // Source
                byte rXDest = (byte)((inst.Opcode & 0x0E00) >> 9);  // Destination

                uint rYSourceAddr = Machine.CPU.ReadAddressRegister(rYSource);
                uint rXDestAddr;
                int rXDestVal;
                int rYSourceVal;

                bool srcAddressError = (rYSourceAddr & 1) != 0 && size != OpSize.Byte;
                if (srcAddressError && size == OpSize.Long)
                {
                    // Increment only the first word access
                    Machine.CPU.IncrementAddressRegister(rYSource, OpSize.Word);
                }
                else
                {
                    Machine.CPU.IncrementAddressRegister(rYSource, size);
                }
                switch (size)
                {
                    case OpSize.Byte:
                        rYSourceVal = Helpers.SignExtendValue(Machine.Bus.ReadByte(rYSourceAddr).Value, size);
                        rXDestAddr = Machine.CPU.ReadAddressRegister(rXDest);
                        rXDestVal = Helpers.SignExtendValue(Machine.Bus.ReadByte(rXDestAddr).Value, size);
                        break;
                    case OpSize.Long:
                        rYSourceVal = Helpers.SignExtendValue(Machine.Bus.ReadLong(rYSourceAddr).Value, size);
                        rXDestAddr = Machine.CPU.ReadAddressRegister(rXDest);
                        rXDestVal = Helpers.SignExtendValue(Machine.Bus.ReadLong(rXDestAddr).Value, size);
                        break;
                    default:
                        rYSourceVal = Helpers.SignExtendValue(Machine.Bus.ReadWord(rYSourceAddr).Value, size);
                        rXDestAddr = Machine.CPU.ReadAddressRegister(rXDest);
                        rXDestVal = Helpers.SignExtendValue(Machine.Bus.ReadWord(rXDestAddr).Value, size);
                        break;
                }

                // Now we've read the values from memory, post-increment destination address register.
                Machine.CPU.IncrementAddressRegister(rXDest, size);

                // Subtract to perform the comparison.
                int result = rXDestVal - rYSourceVal;
                SetFlags(inst.Info.HandlerID, size, (uint)result, (uint)rYSourceVal, (uint)rXDestVal);

                return null;
            }

            private TrapException? CMP(Instruction inst)
            {
                OpSize size = inst.Size ?? OpSize.Word;
                byte dRegNum = (byte)((inst.Opcode & 0x0E00) >> 9);
                int dRegVal = Helpers.SignExtendValue(Machine.CPU.ReadDataRegister(dRegNum), size);

                var value = ReadEAValue(inst, EAType.Destination);
                int signedVal = Helpers.SignExtendValue(value, size);
                var result = dRegVal - signedVal;

                SetFlags(inst.Info.HandlerID, size, (uint)result, value, (uint)dRegVal);
                return null;
            }

            private TrapException? CMPA(Instruction inst)
            {
                var source = ReadEAValue(inst, EAType.Source);
                OpSize size = inst.Size ?? OpSize.Word;
                int signedSource = Helpers.SignExtendValue(source, size);
                int regNum = (inst.Opcode & 0x0E00) >> 9;
                int dest = (int)Machine.CPU.ReadAddressRegister(regNum);
                var result = dest - signedSource;
                SetFlags(inst.Info.HandlerID, OpSize.Long, (uint)result, (uint)signedSource, (uint)dest);
                return null;
            }

            private TrapException? MULU(Instruction inst)
            {
                int dRegNum = (inst.Opcode & 0x0E00) >> 9;
                uint dRegVal = (Machine.CPU.ReadDataRegister(dRegNum) & 0x0000FFFF);
                var value = ReadEAValue(inst, EAType.Source);
                var res = value * dRegVal;
                Machine.CPU.WriteDataRegister(dRegNum, res, OpSize.Long);
                SetFlags(inst.Info.HandlerID, OpSize.Long, res);
                return null;
            }

            private TrapException? MULS(Instruction inst)
            {
                int dRegNum = (inst.Opcode & 0x0E00) >> 9;
                int dRegVal = Helpers.SignExtendValue(Machine.CPU.ReadDataRegister(dRegNum), OpSize.Word);
                var value = ReadEAValue(inst, EAType.Source);
                int signedValue = Helpers.SignExtendValue(value, OpSize.Word);
                var res = signedValue * dRegVal;
                Machine.CPU.WriteDataRegister(dRegNum, (uint)res, OpSize.Long);
                SetFlags(inst.Info.HandlerID, OpSize.Long, (uint)res);
                return null;
            }

            private TrapException? EXG(Instruction inst)
            {
                byte rX = (byte)(inst.Opcode & 0x0007);
                byte rY = (byte)((inst.Opcode & 0x0E00) >> 9);
                byte mode = (byte)((inst.Opcode & 0x00F8) >> 3);
                uint x;
                uint y;
                switch (mode)
                {
                    case 0x08:      // Data Register <-> Data Register
                        x = Machine.CPU.ReadDataRegister(rX);
                        y = Machine.CPU.ReadDataRegister(rY);
                        Machine.CPU.WriteDataRegister(rX, y);
                        Machine.CPU.WriteDataRegister(rY, x);
                        break;
                    case 0x09:      // Address Register <-> Address Register
                        x = Machine.CPU.ReadAddressRegister(rX);
                        y = Machine.CPU.ReadAddressRegister(rY);
                        Machine.CPU.WriteAddressRegister(rX, y);
                        Machine.CPU.WriteAddressRegister(rY, x);
                        break;
                    case 0x11:      // Data Register <-> Address Register
                        x = Machine.CPU.ReadAddressRegister(rX);
                        y = Machine.CPU.ReadDataRegister(rY);
                        Machine.CPU.WriteAddressRegister(rX, y);
                        Machine.CPU.WriteDataRegister(rY, x);
                        break;
                    default:
                        Debug.Assert(false, "Invalid operating mode for EXG instruction.");
                        break;
                }
                return null;
            }

            private TrapException? AND(Instruction inst)
            {
                int dRegNum = (inst.Opcode & 0x0E00) >> 9;
                uint dRegVal = Machine.CPU.ReadDataRegister(dRegNum);

                // Is D(n) the destination?  If so we need to do any address incrementing on the read. I.e., do NOT suppress it.
                bool dnDest = (inst.Opcode & 0x0100) == 0;
                var value = ReadEAValue(inst, EAType.Destination, !dnDest);
                var result = dRegVal & value;
                OpSize size = inst.Size ?? OpSize.Word;
                if (dnDest)
                {
                    Machine.CPU.WriteDataRegister(dRegNum, result, size);
                }
                else
                {
                    WriteEAValue(inst, result, EAType.Destination);
                }
                SetFlags(inst.Info.HandlerID, size, result);
                return null;
            }

            private TrapException? ADD(Instruction inst)
            {
                OpSize size = inst.Size ?? OpSize.Word;
                int dRegNum = (inst.Opcode & 0x0E00) >> 9;
                int dRegVal = Helpers.SignExtendValue(Machine.CPU.ReadDataRegister(dRegNum), size);

                // Is D(n) the destination?  If so we need to do any address incrementing on the read. I.e., do NOT suppress it.
                bool dnDest = (inst.Opcode & 0x0100) == 0;
                var value = ReadEAValue(inst, EAType.Destination, !dnDest);
                int signedVal = Helpers.SignExtendValue(value, size);
                var result = dRegVal + signedVal;
                if (dnDest)
                {
                    Machine.CPU.WriteDataRegister(dRegNum, (uint)result, size);
                    SetFlags(inst.Info.HandlerID, size, (uint)result, value, (uint)dRegVal);
                }
                else
                {
                    WriteEAValue(inst, (uint)result, EAType.Destination);
                    SetFlags(inst.Info.HandlerID, size, (uint)result, (uint)dRegVal, value);
                }
                return null;
            }

            /// <summary>
            /// ADDX: rY -> source, rX -> dest, dest = source + dest + X = rY + rX + X
            /// SUBX: rX -> source, rY -> dest, dest = dest - source - X = rY - rX - X
            /// </summary>
            /// <param name="inst"></param>
            /// <returns></returns>
            public TrapException? ADDX_SUBX(Instruction inst)
            {
                bool add = (inst.Opcode & 0x4000) != 0;

                OpSize size = inst.Size ?? OpSize.Word;
                byte rSource = (byte)(inst.Opcode & 0x0007);
                byte rDest = (byte)((inst.Opcode & 0x0E00) >> 9);
                bool usingDataReg = (inst.Opcode & 0x0008) == 0;
                int extend = Machine.CPU.ExtendFlag ? 1 : 0;
                int source;
                int dest;
                int result;
                if (usingDataReg)
                {
                    source = Helpers.SignExtendValue(Machine.CPU.ReadDataRegister(rSource), size);
                    dest = Helpers.SignExtendValue(Machine.CPU.ReadDataRegister(rDest), size);
                    result = add
                                ? dest + source + extend  // ADDX
                                : dest - source - extend; // SUBX
                    Machine.CPU.WriteDataRegister(rDest, (uint)result, size);
                }
                else
                {
                    uint address;
                    uint destAddress;
                    switch (size)
                    {
                        case OpSize.Long:
                            if ((Machine.CPU.ReadAddressRegister(rSource) & 1) != 0)
                            {
                                // Source address is unaligned
                                return Helpers.CreateTRAPException(TrapVector.AddressError);
                            }
                            address = Machine.CPU.DecrementAddressRegister(rSource, OpSize.Word);
                            source = Machine.Bus.ReadWord(address).Value;
                            address = Machine.CPU.DecrementAddressRegister(rSource, OpSize.Word);
                            source |= Machine.Bus.ReadWord(address).Value << 16;

                            if ((Machine.CPU.ReadAddressRegister(rDest) & 1) != 0)
                            {
                                // Destination address is unaligned
                                return Helpers.CreateTRAPException(TrapVector.AddressError);
                            }
                            address = Machine.CPU.DecrementAddressRegister(rDest, OpSize.Word);
                            dest = Machine.Bus.ReadWord(address).Value;
                            address = Machine.CPU.DecrementAddressRegister(rDest, OpSize.Word);
                            dest |= Machine.Bus.ReadWord(address).Value << 16;
                            destAddress = address;
                            break;
                        case OpSize.Byte:
                            address = Machine.CPU.DecrementAddressRegister(rSource, size);
                            source = Helpers.SignExtendValue(Machine.Bus.ReadByte(address).Value, size);

                            address = Machine.CPU.DecrementAddressRegister(rDest, size);
                            dest = Helpers.SignExtendValue(Machine.Bus.ReadByte(address).Value, size);
                            destAddress = address;
                            break;
                        default: // Word
                            address = Machine.CPU.DecrementAddressRegister(rSource, size);
                            source = Helpers.SignExtendValue(Machine.Bus.ReadWord(address).Value, size);

                            address = Machine.CPU.DecrementAddressRegister(rDest, size);
                            dest = Helpers.SignExtendValue(Machine.Bus.ReadWord(address).Value, size);
                            destAddress = address;
                            break;
                    }

                    result = add
                        ? dest + source + extend  // ADDX
                        : dest - source - extend; // SUBX

                    switch (size)
                    {
                        case OpSize.Byte:
                            Machine.Bus.WriteByte(destAddress, (byte)(result & 0x000000FF));
                            break;
                        case OpSize.Long:
                            Machine.Bus.WriteLong(destAddress, (uint)result);
                            break;
                        default:
                            Machine.Bus.WriteWord(destAddress, (ushort)(result & 0x0000FFFF));
                            break;
                    }
                }
                SetFlags(inst.Info.HandlerID, size, (uint)result, (uint)source, (uint)dest);
                return null;
            }

            private TrapException? ADDA(Instruction inst)
            {
                var value = ReadEAValue(inst, EAType.Source);
                OpSize size = inst.Size ?? OpSize.Word;
                int signedVal = Helpers.SignExtendValue(value, size);
                int regNum = (inst.Opcode & 0x0E00) >> 9;
                uint val = Machine.CPU.ReadAddressRegister(regNum);
                val = (uint)(val + signedVal);
                Machine.CPU.WriteAddressRegister(regNum, val, OpSize.Long);
                return null;
            }

            private TrapException? ASL_ASR_LSL_LSR(Instruction inst)
            {
                bool directionLeft = (inst.Opcode & 0x0100) != 0;       // Determine direction of shift (i.e. ASL or ASR).
                bool logicalShift;
                byte sizeBits = (byte)((inst.Opcode & 0x00C0) >> 6);
                if (sizeBits == 0x03)
                {
                    ushort result;

                    // Shift on memory (using Effective Address)
                    logicalShift = (inst.Opcode & 0x0200) != 0;        // Determine if logical shift (i.e. LSL or LSR).
                    ushort value = (ushort)ReadEAValue(inst, EAType.Source, suppressIncDec: true);
                    ushort msb = (ushort)(value & 0x8000);
                    ushort lastBitShiftedOut;
                    if (directionLeft)
                    {
                        lastBitShiftedOut = msb;
                        result = (ushort)(value << 1);
                    }
                    else // right
                    {
                        lastBitShiftedOut = (ushort)(value & 0x0001);
                        result = (ushort)((ushort)(value >> 1) | (ushort)(logicalShift ? 0 : msb));
                    }
                    Machine.CPU.ExtendFlag = lastBitShiftedOut != 0;
                    Machine.CPU.CarryFlag = Machine.CPU.ExtendFlag;
                    Machine.CPU.ZeroFlag = result == 0;
                    Machine.CPU.NegativeFlag = (result & 0x8000) != 0;
                    Machine.CPU.OverflowFlag = !(logicalShift || msb == (result & 0x8000));

                    WriteEAValue(inst, result, EAType.Source);
                }
                else
                {
                    logicalShift = (inst.Opcode & 0x0018) != 0;        // Determine if logical shift (i.e. LSL or LSR).
                    OpSize size = inst.Size ?? OpSize.Word;
                    uint sizeMask = Helpers.SizeMask(size);
                    uint signMask = Helpers.SizeMSB(size);
                    byte dRegNum = (byte)(inst.Opcode & 0x0007);
                    uint dRegVal = Machine.CPU.ReadDataRegister(dRegNum) & sizeMask;

                    // Determine if a data register holds the shift amount.
                    bool dRegShift = (inst.Opcode & 0x0020) != 0;
                    int shift = (inst.Opcode & 0x0E00) >> 9;
                    int shiftAmt;
                    if (dRegShift)
                    {
                        // The shift value holds the number of the data register that holds the number of bits to shift by.
                        shiftAmt = (int)(Machine.CPU.ReadDataRegister(shift) & 0x003F);
                    }
                    else
                    {
                        shiftAmt = shift != 0 ? shift : 8;
                    }
                    uint bitShiftedOut = 0;
                    bool msbChanged = false;
                    if (directionLeft)
                    {
                        for (int s = 0; s < shiftAmt; s++)
                        {
                            bitShiftedOut = dRegVal & signMask;
                            dRegVal <<= 1;
                            if ((dRegVal & signMask) != bitShiftedOut)
                            {
                                msbChanged = true;
                            }
                        }
                    }
                    else
                    {
                        var msb = dRegVal & signMask;
                        for (int s = 0; s < shiftAmt; s++)
                        {
                            bitShiftedOut = dRegVal & 0x00000001;
                            dRegVal >>= 1;
                            if (!logicalShift)
                            {
                                dRegVal |= msb;
                            }
                        }
                    }

                    dRegVal &= sizeMask;
                    Machine.CPU.WriteDataRegister(dRegNum, dRegVal, size);
                    SetFlags(inst.Info.HandlerID, size, dRegVal, (uint)shiftAmt, bitShiftedOut);
                    Machine.CPU.OverflowFlag = !logicalShift && msbChanged;
                }
                return null;
            }

            /// <summary>
            /// Rotate left (ROL) for byte operand.
            /// </summary>
            /// <param name="value">The byte value to rotate.</param>
            /// <param name="count">The number of positions to rotate.</param>
            /// <returns>The rotated byte value.</returns>
            private byte ROL_B(byte value, int count)
            {
                Machine.CPU.CarryFlag = false;

                for (int i = 0; i < count; i++)
                {
                    Machine.CPU.CarryFlag = (value & 0x80) != 0;
                    value <<= 1;
                    if (Machine.CPU.CarryFlag)
                    {
                        value |= 1;
                    }
                }

                Machine.CPU.ZeroFlag = value == 0;
                Machine.CPU.NegativeFlag = (value & 0x80) != 0;
                Machine.CPU.OverflowFlag = false;
                return value;
            }

            /// <summary>
            /// Rotate left (ROL) for word operand.
            /// </summary>
            /// <param name="value">The word value to rotate.</param>
            /// <param name="count">The number of positions to rotate.</param>
            /// <returns>The rotated word value.</returns>
            private ushort ROL_W(ushort value, int count)
            {
                Machine.CPU.CarryFlag = false;

                for (int i = 0; i < count; i++)
                {
                    Machine.CPU.CarryFlag = (value & 0x8000) != 0;
                    value <<= 1;
                    if (Machine.CPU.CarryFlag)
                    {
                        value |= 1;
                    }
                }

                Machine.CPU.ZeroFlag = value == 0;
                Machine.CPU.NegativeFlag = (value & 0x8000) != 0;
                Machine.CPU.OverflowFlag = false;
                return value;
            }

            /// <summary>
            /// Rotate left (ROL) for long operand.
            /// </summary>
            /// <param name="value">The long value to rotate.</param>
            /// <param name="count">The number of positions to rotate.</param>
            /// <returns>The rotated long value.</returns>
            private uint ROL_L(uint value, int count)
            {
                Machine.CPU.CarryFlag = false;

                for (int i = 0; i < count; i++)
                {
                    Machine.CPU.CarryFlag = (value & 0x80000000) != 0;
                    value <<= 1;
                    if (Machine.CPU.CarryFlag)
                    {
                        value |= 1;
                    }
                }

                Machine.CPU.ZeroFlag = value == 0;
                Machine.CPU.NegativeFlag = (value & 0x80000000) != 0;
                Machine.CPU.OverflowFlag = false;
                return value;
            }

            /// <summary>
            /// Rotate right (ROR) for byte operand.
            /// </summary>
            /// <param name="value">The byte value to rotate.</param>
            /// <param name="count">The number of positions to rotate.</param>
            /// <returns>The rotated byte value.</returns>
            private byte ROR_B(byte value, int count)
            {
                Machine.CPU.CarryFlag = false;

                for (int i = 0; i < count; i++)
                {
                    Machine.CPU.CarryFlag = (value & 0x01) != 0;
                    value >>= 1;
                    if (Machine.CPU.CarryFlag)
                    {
                        value |= 0x80;
                    }
                }

                Machine.CPU.ZeroFlag = value == 0;
                Machine.CPU.NegativeFlag = (value & 0x80) != 0;
                Machine.CPU.OverflowFlag = false;
                return value;
            }

            /// <summary>
            /// Rotate right (ROR) for word operand.
            /// </summary>
            /// <param name="value">The word value to rotate.</param>
            /// <param name="count">The number of positions to rotate.</param>
            /// <returns>The rotated word value.</returns>
            private ushort ROR_W(ushort value, int count)
            {
                Machine.CPU.CarryFlag = false;

                for (int i = 0; i < count; i++)
                {
                    Machine.CPU.CarryFlag = (value & 0x0001) != 0;
                    value >>= 1;
                    if (Machine.CPU.CarryFlag)
                    {
                        value |= 0x8000;
                    }
                }

                Machine.CPU.ZeroFlag = value == 0;
                Machine.CPU.NegativeFlag = (value & 0x8000) != 0;
                Machine.CPU.OverflowFlag = false;
                return value;
            }

            /// <summary>
            /// Rotate right (ROR) for long operand.
            /// </summary>
            /// <param name="value">The long value to rotate.</param>
            /// <param name="count">The number of positions to rotate.</param>
            /// <returns>The rotated long value.</returns>
            private uint ROR_L(uint value, int count)
            {
                Machine.CPU.CarryFlag = false;

                for (int i = 0; i < count; i++)
                {
                    Machine.CPU.CarryFlag = (value & 0x00000001) != 0;
                    value >>= 1;
                    if (Machine.CPU.CarryFlag)
                    {
                        value |= 0x80000000;
                    }
                }

                Machine.CPU.ZeroFlag = value == 0;
                Machine.CPU.NegativeFlag = (value & 0x80000000) != 0;
                Machine.CPU.OverflowFlag = false;
                return value;
            }

            /// <summary>
            /// Rotate left with extend (ROXL) for byte operand.
            /// </summary>
            /// <param name="value">The byte value to rotate.</param>
            /// <param name="count">The number of positions to rotate.</param>
            /// <returns>The rotated byte value.</returns>
            private byte ROXL_B(byte value, int count)
            {
                bool xFlag = Machine.CPU.ExtendFlag;

                for (int i = 0; i < count; i++)
                {
                    bool bitShiftedOut = (value & 0x80) != 0;
                    value <<= 1;
                    if (xFlag)
                    {
                        value |= 1;
                    }
                    xFlag = bitShiftedOut;
                }

                Machine.CPU.CarryFlag = xFlag;
                Machine.CPU.ExtendFlag = xFlag;
                Machine.CPU.ZeroFlag = value == 0;
                Machine.CPU.NegativeFlag = (value & 0x80) != 0;
                Machine.CPU.OverflowFlag = false;
                return value;
            }

            /// <summary>
            /// Rotate left with extend (ROXL) for word operand.
            /// </summary>
            /// <param name="value">The word value to rotate.</param>
            /// <param name="count">The number of positions to rotate.</param>
            /// <returns>The rotated word value.</returns>
            private ushort ROXL_W(ushort value, int count)
            {
                bool xFlag = Machine.CPU.ExtendFlag;

                for (int i = 0; i < count; i++)
                {
                    bool bitShiftedOut = (value & 0x8000) != 0;
                    value <<= 1;
                    if (xFlag)
                    {
                        value |= 1;
                    }
                    xFlag = bitShiftedOut;
                }

                Machine.CPU.CarryFlag = xFlag;
                Machine.CPU.ExtendFlag = xFlag;
                Machine.CPU.ZeroFlag = value == 0;
                Machine.CPU.NegativeFlag = (value & 0x8000) != 0;
                Machine.CPU.OverflowFlag = false;
                return value;
            }

            /// <summary>
            /// Rotate left with extend (ROXL) for long operand.
            /// </summary>
            /// <param name="value">The long value to rotate.</param>
            /// <param name="count">The number of positions to rotate.</param>
            /// <returns>The rotated long value.</returns>
            private uint ROXL_L(uint value, int count)
            {
                bool xFlag = Machine.CPU.ExtendFlag;

                for (int i = 0; i < count; i++)
                {
                    bool bitShiftedOut = (value & 0x80000000) != 0;
                    value <<= 1;
                    if (xFlag)
                    {
                        value |= 1;
                    }
                    xFlag = bitShiftedOut;
                }

                Machine.CPU.CarryFlag = xFlag;
                Machine.CPU.ExtendFlag = xFlag;
                Machine.CPU.ZeroFlag = value == 0;
                Machine.CPU.NegativeFlag = (value & 0x80000000) != 0;
                Machine.CPU.OverflowFlag = false;
                return value;
            }

            /// <summary>
            /// Rotate right with extend (ROXR) for byte operand.
            /// </summary>
            /// <param name="value">The byte value to rotate.</param>
            /// <param name="count">The number of positions to rotate.</param>
            /// <returns>The rotated byte value.</returns>
            private byte ROXR_B(byte value, int count)
            {
                bool xFlag = Machine.CPU.ExtendFlag;

                for (int i = 0; i < count; i++)
                {
                    bool bitShiftedOut = (value & 1) != 0;
                    value >>= 1;
                    if (xFlag)
                    {
                        value |= 0x80;
                    }
                    xFlag = bitShiftedOut;
                }

                Machine.CPU.CarryFlag = xFlag;
                Machine.CPU.ExtendFlag = xFlag;
                Machine.CPU.ZeroFlag = value == 0;
                Machine.CPU.NegativeFlag = (value & 0x80) != 0;
                Machine.CPU.OverflowFlag = false;
                return value;
            }

            /// <summary>
            /// Rotate right with extend (ROXR) for word operand.
            /// </summary>
            /// <param name="value">The word value to rotate.</param>
            /// <param name="count">The number of positions to rotate.</param>
            /// <returns>The rotated word value.</returns>
            private ushort ROXR_W(ushort value, int count)
            {
                bool xFlag = Machine.CPU.ExtendFlag;

                for (int i = 0; i < count; i++)
                {
                    bool bitShiftedOut = (value & 1) != 0;
                    value >>= 1;
                    if (xFlag)
                    {
                        value |= 0x8000;
                    }
                    xFlag = bitShiftedOut;
                }

                Machine.CPU.CarryFlag = xFlag;
                Machine.CPU.ExtendFlag = xFlag;
                Machine.CPU.ZeroFlag = value == 0;
                Machine.CPU.NegativeFlag = (value & 0x8000) != 0;
                Machine.CPU.OverflowFlag = false;
                return value;
            }

            /// <summary>
            /// Rotate right with extend (ROXR) for long operand.
            /// </summary>
            /// <param name="value">The long value to rotate.</param>
            /// <param name="count">The number of positions to rotate.</param>
            /// <returns>The rotated long value.</returns>
            private uint ROXR_L(uint value, int count)
            {
                bool xFlag = Machine.CPU.ExtendFlag;

                for (int i = 0; i < count; i++)
                {
                    bool bitShiftedOut = (value & 1) != 0;
                    value >>= 1;
                    if (xFlag)
                    {
                        value |= 0x80000000;
                    }
                    xFlag = bitShiftedOut;
                }

                Machine.CPU.CarryFlag = xFlag;
                Machine.CPU.ExtendFlag = xFlag;
                Machine.CPU.ZeroFlag = value == 0;
                Machine.CPU.NegativeFlag = (value & 0x80000000) != 0;
                Machine.CPU.OverflowFlag = false;
                return value;
            }

            private TrapException? ROL_ROR_ROXL_ROXR(Instruction inst)
            {
                bool rotateLeft = (inst.Opcode & 0x0100) != 0;       // Determine direction of rotation (e.g. ROL(X) or ROR(X)).
                bool rotateMemoryWord = (inst.Opcode & 0x00C0) == 0x00C0;
                bool useExtend;
                if (rotateMemoryWord)
                {
                    useExtend = (inst.Opcode & 0x0E00) == 0x0400;
                    uint? value = ReadEAValue(inst, EAType.Source, suppressIncDec: true);
                    if (!value.HasValue) Helpers.RaiseTRAPException(TrapVector.BusError);
                    ushort word = (ushort)value!.Value;
                    if (rotateLeft)
                    {
                        word = useExtend ? ROXL_W(word, 1) : ROL_W(word, 1);
                    }
                    else
                    {
                        word = useExtend ? ROXR_W(word, 1) : ROR_W(word, 1);
                    }
                    WriteEAValue(inst, word, EAType.Source);
                }
                else
                {
                    // Rotate register
                    useExtend = (inst.Opcode & 0x0018) == 0x0010;
                    byte dRegNum = (byte)(inst.Opcode & 0x0007);

                    // Determine if a data register holds the rotation amount.
                    bool dRegRotate = (inst.Opcode & 0x0020) != 0;
                    int rotate = (inst.Opcode & 0x0E00) >> 9;
                    int count;
                    if (dRegRotate)
                    {
                        // The rotate value holds the number of the data register that holds the number of bits to rotate by.
                        count = (int)(Machine.CPU.ReadDataRegister(rotate) & 0x003F);
                    }
                    else
                    {
                        count = rotate != 0 ? rotate : 8;
                    }
                    uint value = Machine.CPU.ReadDataRegister(dRegNum);
                    switch (inst.Size)
                    {
                        case OpSize.Byte:
                            byte bValue = (byte)value;
                            bValue = rotateLeft
                                ? useExtend
                                    ? ROXL_B(bValue, count)
                                    : ROL_B(bValue, count)
                                : useExtend
                                    ? ROXR_B(bValue, count)
                                    : ROR_B(bValue, count);
                            value = bValue;
                            break;
                        case OpSize.Word:
                            ushort wValue = (ushort)value;
                            wValue = rotateLeft
                                ? useExtend
                                    ? ROXL_W(wValue, count)
                                    : ROL_W(wValue, count)
                                : useExtend
                                    ? ROXR_W(wValue, count)
                                    : ROR_W(wValue, count);
                            value = wValue;
                            break;
                        case OpSize.Long:
                            value = rotateLeft
                                ? useExtend
                                    ? ROXL_L(value, count)
                                    : ROL_L(value, count)
                                : useExtend
                                    ? ROXR_L(value, count)
                                    : ROR_L(value, count);
                            break;
                    }
                    Machine.CPU.WriteDataRegister(dRegNum, value, inst.Size);
                }
                return null;
            }

            private TrapException? BTST_BCHG_BCLR_BSET(Instruction inst)
            {
                // Determine which operation we're performing (BTST, BCHG, BCLR, or BSET)
                byte operation = (byte)((inst.Opcode & 0x00C0) >> 6);
                bool isBTST = operation == 0x00;
                uint bitNum;
                if ((inst.Opcode & 0x0100) != 0)       // Determine if dynamic (i.e. bit number specified in a register)
                {
                    int regNum = (inst.Opcode & 0x0E00) >> 9;
                    bitNum = Machine.CPU.ReadDataRegister(regNum);
                }
                else
                {
                    Helpers.AssertHasSourceExtWord1(inst);
                    bitNum = (uint)inst.SourceExtWord1!.Value;
                }

                // Determine if the destination is a memory address. If it is then we work with a single byte.
                // For BTST, the destination can be an immediate byte.
                var (_, _, address, immvalue) = EvaluateEffectiveAddress(inst, EAType.Destination, true);
                if (address.HasValue || immvalue.HasValue)
                {
                    bitNum &= 0x00000007;
                }
                else
                {
                    bitNum &= 0x0000001F;
                }

                var bit = _bit[bitNum];
                var value = ReadEAValue(inst, EAType.Destination, !isBTST);

                // Test the specified bit and set the Zero flag accordingly.
                Machine.CPU.ZeroFlag = (value & bit) == 0;

                // Modify the specified bit as necessary for the instruction being executed (but do nothing more for
                // BTST as we've already performed the test, which is all that is needed for this instruction)
                uint result = value;
                switch (operation)
                {
                    case 0x01:      // BCHG
                        result ^= bit;
                        break;
                    case 0x02:      // BCLR
                        result &= ~bit;
                        break;
                    case 0x03:      // BSET
                        result |= bit;
                        break;
                    default:
                        // BTST operation so nothing more to do.
                        break;
                }

                // For anything other than a BTST instruction we need to update the destination.
                if (!isBTST)
                {
                    WriteEAValue(inst, result, EAType.Destination);
                }
                return null;
            }

            private TrapException? LINK(Instruction inst)
            {
                TrapException? e;
                byte regNum = (byte)(inst.Opcode & 0x0007);
                uint regValue = Machine.CPU.ReadAddressRegister(regNum);
                e = Machine.PushLongCheck(regValue);
                if (e == null)
                {
                    uint sp = Machine.CPU.ReadAddressRegister(7);
                    Machine.CPU.WriteAddressRegister(regNum, sp);
                    int disp = Helpers.SignExtendValue(inst.SourceExtWord1!.Value, OpSize.Word);
                    Machine.CPU.WriteAddressRegister(7, (uint)((int)sp + disp));
                }
                return e;
            }

            private TrapException? UNLK(Instruction inst)
            {
                uint a7 = Machine.CPU.ReadAddressRegister(7);
                byte linkRegNum = (byte)(inst.Opcode & 0x0007);
                uint linkAddress = Machine.CPU.ReadAddressRegister(linkRegNum);
                Machine.CPU.WriteAddressRegister(7, linkAddress);
                (uint? tos, TrapException? e) = Machine.PopLongCheck();
                if (e != null)
                {
                    // Back out changes to A7
                    Machine.CPU.WriteAddressRegister(7, a7);
                }
                else
                {
                    Machine.CPU.WriteAddressRegister(linkRegNum, tos!.Value);
                }
                return e;
            }

            private TrapException? STOP(Instruction inst)
            {
                var data = inst.SourceExtWord1;
                if (!data.HasValue)
                {
                    Debug.Assert(data.HasValue, "Emulator logic error - should not happen");
                    throw Helpers.CreateTRAPException(TrapVector.IllegalInstruction);
                }

                // If not already in Supervisor mode then raise an exception.
                if (!Machine.CPU.SupervisorMode)
                {
                    return Helpers.CreateTRAPException(TrapVector.PrivilegeViolation);
                }
                SRFlags oldSR = Machine.CPU.SR;
                Machine.CPU.SR = (SRFlags)data.Value;
                if (!oldSR.HasFlag(SRFlags.TraceMode))
                {
                    Machine.StopExecution();
                }
                Machine.SetPC(Machine.CurrentInstructionAddress);
                return null;
            }

            private TrapException? TAS(Instruction inst)
            {
                var value = ReadEAValue(inst, EAType.Destination, suppressIncDec: true);
                Machine.CPU.NegativeFlag = (value & 0x00000080) != 0;
                Machine.CPU.ZeroFlag = (value & 0x000000FF) == 0;
                Machine.CPU.CarryFlag = Machine.CPU.OverflowFlag = false;
                var result = value | 0x00000080;
                WriteEAValue(inst, result, EAType.Destination);
                return null;
            }

            /// <summary>
            /// Add and subtract packed BCD values (bytes).
            /// </summary>
            /// <param name="inst"></param>
            /// <returns></returns>
            private TrapException? ABCD_SBCD(Instruction inst)
            {
                // Determine if we're handling an ABCD instruction or a SBCD instruction by analyzing the top 4 bits of the
                // opcode value.
                BCDOperation opType = ((inst.Opcode & 0xF000) == 0xC000) ? BCDOperation.Addition : BCDOperation.Subtraction;
                byte rSrc = (byte)(inst.Opcode & 0x0007);
                byte rDest = (byte)((inst.Opcode & 0x0E00) >> 9);

                if ((inst.Opcode & 0x0008) == 0)
                {
                    // Working with data registers
                    uint srcVal = Machine.CPU.ReadDataRegister(rSrc) & 0x000000FF;
                    uint destVal = Machine.CPU.ReadDataRegister(rDest) & 0x000000FF;
                    var result = BCDCalculation(opType, srcVal, destVal);
                    Machine.CPU.WriteDataRegister(rDest, result, OpSize.Byte);
                }
                else
                {
                    // Working with memory addresses, so pre-decrement both address registers by 1 byte.
                    var srcAddr = Machine.CPU.DecrementAddressRegister(rSrc, OpSize.Byte);
                    var destAddr = Machine.CPU.DecrementAddressRegister(rDest, OpSize.Byte);
                    uint srcVal = Machine.Bus.ReadByte(srcAddr).Value;
                    uint destVal = Machine.Bus.ReadByte(destAddr).Value;
                    var result = BCDCalculation(opType, srcVal, destVal);
                    Machine.Bus.WriteByte(destAddr, (byte)(result & 0x000000FF));
                }
                return null;
            }

            /// <summary>
            /// Subtracts the destination operand and the extend bit from zero. The operation
            /// is performed using binary-coded decimal arithmetic. The packed binary-coded decimal
            /// result is saved in the destination location. This instruction produces the tens
            /// complement of the destination if the extend bit is zero or the nines complement if the
            /// extend bit is one. This is a byte operation only.
            /// Computes: result = 0 - destination - X
            /// </summary>
            /// <param name="inst">The instruction to execute.</param>
            /// <returns>A TrapException if a trap occurred, otherwise null.</returns>
            private TrapException? NBCD(Instruction inst)
            {
                uint value = ReadEAValue(inst, EAType.Destination, suppressIncDec: true);
                uint result = BCDCalculation(BCDOperation.Subtraction, value & 0xff, 0);
                WriteEAValue(inst, result, EAType.Destination);
                return null;
            }

            private TrapException? MOVEP(Instruction inst)
            {
                byte aRegNum = (byte)(inst.Opcode & 0x0007);
                byte dRegNum = (byte)((inst.Opcode & 0x0E00) >> 9);
                OpSize size = (inst.Opcode & 0x0040) == 0 ? OpSize.Word : OpSize.Long;
                bool memToReg = (inst.Opcode & 0x0080) == 0;

                Helpers.AssertHasSourceExtWord1(inst);

                int disp = Helpers.SignExtendValue(inst.SourceExtWord1!.Value, OpSize.Word);
                uint address = (uint)((int)Machine.CPU.ReadAddressRegister(aRegNum) + disp);
                if (memToReg)
                {
                    if (size == OpSize.Word)
                    {
                        int val = Machine.Bus.ReadByte(address).Value << 8;
                        val |= Machine.Bus.ReadByte(address + 2).Value;
                        Machine.CPU.WriteDataRegister(dRegNum, (uint)val, size);
                    }
                    else
                    {
                        var val = Machine.Bus.ReadByte(address).Value << 24;
                        val |= Machine.Bus.ReadByte(address + 2).Value << 16;
                        val |= Machine.Bus.ReadByte(address + 4).Value << 8;
                        val |= Machine.Bus.ReadByte(address + 6).Value;
                        Machine.CPU.WriteDataRegister(dRegNum, (uint)val, size);
                    }
                }
                else
                {
                    uint val = Machine.CPU.ReadDataRegister(dRegNum);
                    if (size == OpSize.Word)
                    {
                        Machine.Bus.WriteByte(address, (byte)((val >> 8) & 0x000000FF));
                        Machine.Bus.WriteByte(address + 2, (byte)(val & 0x000000FF));
                    }
                    else
                    {
                        Machine.Bus.WriteByte(address, (byte)((val >> 24) & 0x000000FF));
                        Machine.Bus.WriteByte(address + 2, (byte)((val >> 16) & 0x000000FF));
                        Machine.Bus.WriteByte(address + 4, (byte)((val >> 8) & 0x000000FF));
                        Machine.Bus.WriteByte(address + 6, (byte)(val & 0x000000FF));
                    }
                }
                return null;
            }

            private TrapException? MOVEM(Instruction inst)
            {
                // Save the state of the CPU address registers since we may be
                // modifying one during the operation.
                CPU cpu = new();
                Machine.GetCPUState().ToCPU(cpu);
                int addressRegister;

                var (_, _, address, _) = EvaluateEffectiveAddress(inst, EAType.Destination, suppressIncDec: true);
                Debug.Assert(address.HasValue, "OpcodeDecoder and InstructionDecoder should have ensured that a MOVEM instruction has a valid effective address.");

                DeferredAddressRegisterUpdate.Clear(); // Not needed - handled below

                if ((address.Value & 1) != 0)
                {
                    // Address is unaligned
                    throw Helpers.CreateTRAPException(TrapVector.AddressError);
                }
                if (!inst.SourceExtWord1.HasValue)
                {
                    // MOVEM instruction must have a source extension word.
                    Debug.Assert(inst.SourceExtWord1.HasValue, "OpcodeDecoder and InstructionDecoder should have ensured that a MOVEM instruction has a source extension word.");
                    throw Helpers.CreateTRAPException(TrapVector.IllegalInstruction);
                }
                ushort regMask = inst.SourceExtWord1.Value;
                OpSize size = inst.Size ?? OpSize.Long;
                bool regToMem = (inst.Opcode & 0x0400) == 0;

                if (regToMem)
                {
                    if (((inst.Opcode >> 3) & 0x0007) == 0x0004)
                    {
                        // Pre-decrement addressing mode
                        addressRegister = inst.Opcode & 0x0007;
                        var newAddr = MOVEM_RegToMemPreDec(regMask, cpu, address.Value, size);
                        Machine.CPU.WriteAddressRegister(addressRegister, newAddr);
                    }
                    else
                    {
                        MOVEM_RegToMem(regMask, cpu, address.Value, size);
                    }
                }
                else
                {
                    var newAddr = MOVEM_MemToReg(regMask, address.Value, size);
                    // If post-increment addressing then update the address register.
                    if (((inst.Opcode >> 3) & 0x0007) == 0x0003)
                    {
                        addressRegister = inst.Opcode & 0x0007;
                        Machine.CPU.WriteAddressRegister(addressRegister, newAddr);
                    }
                }
                return null;
            }
        }
    }
}
