using PendleCodeMonkey.MC68000EmulatorLib.Enumerations;
using System.Diagnostics;
using System.Reflection.Metadata.Ecma335;
using System.Runtime.CompilerServices;
using System.Text;

namespace PendleCodeMonkey.MC68000EmulatorLib
{
    /// <summary>
    /// Partial implementation of the <see cref="Machine"/> class.
    /// </summary>
    public partial class Machine
    {

        /// <summary>
        /// Take an arbitrary 32-bit number and mask it to be a legal
        /// 24-bit address for the MC68000.
        /// </summary>
        /// <param name="address"></param>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint Make24BitAddress(uint address)
        {
            return address & CPU.LEGAL_ADDRESS_MASK;
        }

        /// <summary>
        /// Implementation of the <see cref="Disassembler"/> class.  It
        /// disassembles instructions and displays memory but never
        /// alters the state of the actual machine unless reading
        /// a location or I/O address would change the state
        /// of the (simulated) hardware.
        /// </summary>
        public partial class Disassembler
        {
            /// <summary>
            /// Converts a byte array to a lowercase hex string.
            /// </summary>
            /// <param name="bytes">The byte array to convert.</param>
            /// <returns>A hex string representation of the byte array.</returns>
            public static string ByteArrayToHexString(byte[] bytes)
            {
                return Convert.ToHexStringLower(bytes);
            }

            /// <summary>
            /// Record returned when disassembling a single instruction at an address.
            /// </summary>
            public record DisassemblyRecord
            {
                /// <summary>
                /// Create in instance of the <see cref="DisassemblyRecord"/> class.
                /// </summary>
                /// <param name="op">Directive or Operation</param>
                /// <param name="endOfData">Set to <c>true</c> if the disassembler
                /// ran out of bytes prior to completing disassembly of this instruction.</param>
                public DisassemblyRecord(DirectiveOrOperation op, bool endOfData = false)
                {
                    Op = op;
                    EndOfData = endOfData;
                }

                /// <summary>
                /// Directive or operation info.
                /// </summary>
                public DirectiveOrOperation Op { get; private set; }

                /// <summary>
                /// Set to <c>true</c> if the disassembler
                /// ran out of bytes prior to completing disassembly of this instruction
                /// </summary>
                public bool EndOfData { get; private set; }

                /// <summary>
                /// Address of this instruction or data area.
                /// </summary>
                public uint Address => Op.Address;

                /// <summary>
                /// Actual instruction or data bytes
                /// </summary>
                public byte[] MachineCode => Op.MachineCode;

                /// <summary>
                /// True if this is part of a Non-Executable Section.
                /// </summary>
                public bool IsNES => Op is Directive;

                /// <summary>
                /// Formatted disassembly string.
                /// </summary>
                /// <returns></returns>
                public override string? ToString()
                {
                    StringBuilder sb = new();
                    sb.Append($"${Op.Address:x8} ");
                    sb.Append(ByteArrayToHexString(Op.MachineCode).PadRight(MAX_INSTRUCTION_LENGTH + 2));               
                    sb.Append(Op.Assembly);
                    return sb.ToString();
                }
            }

            /// <summary>
            /// Wrapper around the machine's memory with its own CPU.PC and
            /// <see cref="IsEndOfData"/> and <see cref="IsEndOfExecution"/>logic to
            /// support the Decoder.
            /// </summary>
            public class DisassemblerMachine : Machine
            {
                public DisassemblerMachine(Machine machine) : base(machine.Memory)
                {
                    // Initialize registers from the actual machine.
                    SetCPUState(machine.GetCPUState());
                }

                /// <summary>
                /// End of data not reached until end of address space.
                /// </summary>
                public override bool IsEndOfData => CPU.CurrentPC >= 0xffffffff;

                /// <summary>
                /// For the purposes of disassembly, end of execution is the entire
                /// address space.
                /// </summary>
                public override bool IsEndOfExecution { get => IsEndOfData; protected set => _ = value; }
            }

            /// <summary>
            /// Thrown when disassembler reaches the end of data during disassembly.
            /// </summary>
            public class EndOfDataException : InvalidOperationException
            {
                public EndOfDataException(string message) : base(message) { }
            }

            /// <summary>
            /// Column where the effective address (source,dest) text starts,
            /// where the mnemonic (e.g., "DC.W", "MOVEM") starts in column 0.
            /// </summary>
            public const int EA_COLUMN = 8;

            /// <summary>
            /// Maximum instruction length in bytes.
            /// </summary>
            public const int MAX_INSTRUCTION_LENGTH = 14;

            /// <summary>
            /// Gets or sets the <see cref="Machine"/> instance for which this <see cref="Disassembler"/> instance
            /// is handling the disassembly of instructions.
            /// </summary>
            public DisassemblerMachine Machine { get; protected set; }

            /// <summary>
            /// Gets or sets the start effectiveAddress of the block of memory being disassembled.
            /// </summary>
            protected uint StartAddress { get; set; }

            /// <summary>
            /// Gets or sets the length of the block of memory being disassembled.
            /// </summary>
            protected uint Length { get; set; }

            /// <summary>
            /// Gets or sets the effectiveAddress of the current byte in the block of memory being disassembled.
            /// </summary>
            protected uint CurrentAddress { get; set; }

            /// <summary>
            /// Gets a value indicating if the disassembly has reached the end of the specified block of memory.
            /// </summary>
            protected bool IsEndOfData => CurrentAddress >= StartAddress + Length;


            public NonExecutableSections MachineNonExecutableSections { get; set; } = new();

            /// <summary>
            /// Return the disassembled operation at the CurrentAddress.
            /// </summary>
            /// <param name="inst"></param>
            /// <param name="sb"></param>
            /// <throws>NotSupportedException if it is an illegal instruction</throws>
            /// <returns>Disassembled Operation or null (or throws NotSupportedException) if illegal instruction</returns>
            protected delegate Operation? DisassemblyHandler(Instruction inst, StringBuilder sb);
            protected readonly Dictionary<OpHandlerID, DisassemblyHandler> _handlers = [];
            protected static readonly uint[] _bit = [ 0x00000001, 0x00000002, 0x00000004, 0x00000008, 0x00000010, 0x00000020, 0x00000040, 0x00000080,
                                                      0x00000100, 0x00000200, 0x00000400, 0x00000800, 0x00001000, 0x00002000, 0x00004000, 0x00008000,
                                                      0x00010000, 0x00020000, 0x00040000, 0x00080000, 0x00100000, 0x00200000, 0x00400000, 0x00800000,
                                                      0x01000000, 0x02000000, 0x04000000, 0x08000000, 0x10000000, 0x20000000, 0x40000000, 0x80000000 ];
            protected static readonly uint[] _rbit = [ 0x80000000, 0x40000000, 0x20000000, 0x10000000, 0x08000000, 0x04000000, 0x02000000, 0x01000000,
                                                       0x00800000, 0x00400000, 0x00200000, 0x00100000, 0x00090000, 0x00040000, 0x00020000, 0x00010000,
                                                       0x00008000, 0x00004000, 0x00002000, 0x00001000, 0x00000800, 0x00000400, 0x00000200, 0x00000100,
                                                       0x00000080, 0x00000040, 0x00000020, 0x00000010, 0x00000008, 0x00000004, 0x00000002, 0x00000001 ];
            protected static readonly string[] _reg = [ "D0","D1","D2","D3","D4","D5","D6","D7",
                                                        "A0","A1","A2","A3","A4","A5","A6","A7" ];

            /// <summary>
            /// Latest disassembler instance on top of stack.
            /// </summary>
            protected static Stack<Disassembler> Disassemblers { get; set; } = new();

            /// <summary>
            /// Need a way to get the disassembler instance from non-Disassembler methods.
            /// This is not thread-safe and not suitable for multiple disassemblers.
            /// </summary>
            protected static Disassembler? CurrentDisassembler => Disassemblers.Peek();

            /// <summary>
            /// Initialize the Opcode handlers.
            /// </summary>
            /// <remarks>
            /// Maps an enumerated operation handler ID to a DisassemblyHandler that generates disassembly text.
            /// </remarks>
            protected void InitOpcodeHandlers()
            {
                _handlers.Add(OpHandlerID.NONE, NONE);
                _handlers.Add(OpHandlerID.ORItoCCR, IMMEDtoCCR);
                _handlers.Add(OpHandlerID.ORItoSR, IMMEDtoSR);
                _handlers.Add(OpHandlerID.ORI, IMMED_OP);
                _handlers.Add(OpHandlerID.ANDItoCCR, IMMEDtoCCR);
                _handlers.Add(OpHandlerID.ANDItoSR, IMMEDtoSR);
                _handlers.Add(OpHandlerID.ANDI, IMMED_OP);
                _handlers.Add(OpHandlerID.SUBI, IMMED_OP);
                _handlers.Add(OpHandlerID.ADDI, IMMED_OP);
                _handlers.Add(OpHandlerID.EORItoCCR, IMMEDtoCCR);
                _handlers.Add(OpHandlerID.EORItoSR, IMMEDtoSR);
                _handlers.Add(OpHandlerID.EORI, IMMED_OP);
                _handlers.Add(OpHandlerID.CMPI, IMMED_OP);
                _handlers.Add(OpHandlerID.MOVE, MOVE);
                _handlers.Add(OpHandlerID.MOVEA, MOVEA);
                _handlers.Add(OpHandlerID.MOVEfromSR, MOVEfromSR);
                _handlers.Add(OpHandlerID.MOVEtoCCR, MOVEtoCCR);
                _handlers.Add(OpHandlerID.MOVEtoSR, MOVEtoSR);
                _handlers.Add(OpHandlerID.NEGX, DST);
                _handlers.Add(OpHandlerID.CLR, DST);
                _handlers.Add(OpHandlerID.NEG, DST);
                _handlers.Add(OpHandlerID.NOT, DST);
                _handlers.Add(OpHandlerID.EXT, EXT);
                _handlers.Add(OpHandlerID.SWAP, SWAP);
                _handlers.Add(OpHandlerID.PEA, PEA);
                _handlers.Add(OpHandlerID.ILLEGAL, NOOPERANDS);
                _handlers.Add(OpHandlerID.TST, DST);
                _handlers.Add(OpHandlerID.TRAP, TRAP);
                _handlers.Add(OpHandlerID.MOVEUSP, MOVEUSP);
                _handlers.Add(OpHandlerID.RESET, NOOPERANDS);
                _handlers.Add(OpHandlerID.NOP, NOOPERANDS);
                _handlers.Add(OpHandlerID.RTE, NOOPERANDS);
                _handlers.Add(OpHandlerID.RTS, NOOPERANDS);
                _handlers.Add(OpHandlerID.TRAPV, NOOPERANDS);
                _handlers.Add(OpHandlerID.RTR, NOOPERANDS);
                _handlers.Add(OpHandlerID.JSR, JMP_JSR);
                _handlers.Add(OpHandlerID.JMP, JMP_JSR);
                _handlers.Add(OpHandlerID.LEA, LEA);
                _handlers.Add(OpHandlerID.CHK, CHK);
                _handlers.Add(OpHandlerID.ADDQ, ADDQ_SUBQ);
                _handlers.Add(OpHandlerID.SUBQ, ADDQ_SUBQ);
                _handlers.Add(OpHandlerID.Scc, Scc);
                _handlers.Add(OpHandlerID.DBcc, DBcc);
                _handlers.Add(OpHandlerID.BRA, BRA_BSR);
                _handlers.Add(OpHandlerID.BSR, BRA_BSR);
                _handlers.Add(OpHandlerID.Bcc, Bcc);
                _handlers.Add(OpHandlerID.MOVEQ, MOVEQ);
                _handlers.Add(OpHandlerID.DIVU, MULS_MULU_DIVU_DIVS);
                _handlers.Add(OpHandlerID.DIVS, MULS_MULU_DIVU_DIVS);
                _handlers.Add(OpHandlerID.OR, ADD_SUB_OR_AND_EOR_CMP);
                _handlers.Add(OpHandlerID.SUB, ADD_SUB_OR_AND_EOR_CMP);
                _handlers.Add(OpHandlerID.SUBX, SUBX);
                _handlers.Add(OpHandlerID.SUBA, ADDA_SUBA_CMPA);
                _handlers.Add(OpHandlerID.EOR, ADD_SUB_OR_AND_EOR_CMP);
                _handlers.Add(OpHandlerID.CMPM, CMPM);
                _handlers.Add(OpHandlerID.CMP, ADD_SUB_OR_AND_EOR_CMP);
                _handlers.Add(OpHandlerID.CMPA, ADDA_SUBA_CMPA);
                _handlers.Add(OpHandlerID.MULU, MULS_MULU_DIVU_DIVS);
                _handlers.Add(OpHandlerID.MULS, MULS_MULU_DIVU_DIVS);
                _handlers.Add(OpHandlerID.EXG, EXG);
                _handlers.Add(OpHandlerID.AND, ADD_SUB_OR_AND_EOR_CMP);
                _handlers.Add(OpHandlerID.ADD, ADD_SUB_OR_AND_EOR_CMP);
                _handlers.Add(OpHandlerID.ADDX, ADDX);
                _handlers.Add(OpHandlerID.ADDA, ADDA_SUBA_CMPA);
                _handlers.Add(OpHandlerID.ASL, ASL_ASR_LSL_LSR_ROL_ROR_ROXL_ROXR);
                _handlers.Add(OpHandlerID.ASR, ASL_ASR_LSL_LSR_ROL_ROR_ROXL_ROXR);
                _handlers.Add(OpHandlerID.LSL, ASL_ASR_LSL_LSR_ROL_ROR_ROXL_ROXR);
                _handlers.Add(OpHandlerID.LSR, ASL_ASR_LSL_LSR_ROL_ROR_ROXL_ROXR);
                _handlers.Add(OpHandlerID.ROL, ASL_ASR_LSL_LSR_ROL_ROR_ROXL_ROXR);
                _handlers.Add(OpHandlerID.ROR, ASL_ASR_LSL_LSR_ROL_ROR_ROXL_ROXR);
                _handlers.Add(OpHandlerID.ROXL, ASL_ASR_LSL_LSR_ROL_ROR_ROXL_ROXR);
                _handlers.Add(OpHandlerID.ROXR, ASL_ASR_LSL_LSR_ROL_ROR_ROXL_ROXR);
                _handlers.Add(OpHandlerID.BTST, BTST_BCHG_BCLR_BSET);
                _handlers.Add(OpHandlerID.BCHG, BTST_BCHG_BCLR_BSET);
                _handlers.Add(OpHandlerID.BCLR, BTST_BCHG_BCLR_BSET);
                _handlers.Add(OpHandlerID.BSET, BTST_BCHG_BCLR_BSET);
                _handlers.Add(OpHandlerID.LINK, LINK);
                _handlers.Add(OpHandlerID.UNLK, UNLK);
                _handlers.Add(OpHandlerID.STOP, STOP);
                _handlers.Add(OpHandlerID.TAS, DST);
                _handlers.Add(OpHandlerID.ABCD, ABCD_SBCD);
                _handlers.Add(OpHandlerID.SBCD, ABCD_SBCD);
                _handlers.Add(OpHandlerID.NBCD, DST);
                _handlers.Add(OpHandlerID.MOVEP, MOVEP);
                _handlers.Add(OpHandlerID.MOVEM, MOVEM);
                _handlers.Add(OpHandlerID.LINEA, LINEA);
                _handlers.Add(OpHandlerID.LINEF, LINEF);
            }

            /// <summary>
            /// Initializes a new instance of the <see cref="Disassembler"/> class.
            /// </summary>
            /// <param name="machine">The <see cref="Machine"/> instance for which this object is handling the disassembly of instructions.</param>
            public Disassembler(Machine machine)
            {
                ArgumentNullException.ThrowIfNull(machine);
                Disassemblers.Push(this);

                if (machine is DisassemblerMachine disassemblerMachine)
                {
                    // Allow for subclasses to provide their own machine.
                    Machine = disassemblerMachine;
                }
                else
                {
                    // Same memory, different CPU state (especially PC location) for disassembler
                    Machine = new DisassemblerMachine(machine);
                }

                InitOpcodeHandlers();
            }

            ~Disassembler()
            {
                Disassemblers.Pop();
            }

            /// <summary>
            /// Return the size, in bytes, for this OpSize since
            /// the enum values start at 0.
            /// </summary>
            /// <param name="size"></param>
            /// <returns></returns>
            public static uint OpSizeToLength(OpSize size)
            {
                return size switch
                {
                    OpSize.Byte => 1,
                    OpSize.Word => 2,
                    OpSize.Long => 4,
                    _ => 1
                };
            }

            /// <summary>
            /// Return an OpSize compatible with this length.
            /// </summary>
            /// <param name="byteCount"></param>
            /// <returns></returns>
            public static OpSize LengthToOpSize(uint length)
            {
                if (length % 4 == 0) return OpSize.Long;
                if (length % 2 == 0) return OpSize.Word;
                return OpSize.Byte;
            }

            /// <summary>
            /// Disassemble an arbitrary array of bytes.
            /// </summary>
            /// <param name="address"></param>
            /// <param name="code">Continguous array of bytes starting at <see cref="address"/></param>
            /// <returns></returns>
            public List<DisassemblyRecord> DisassembleBytes(uint address, byte[] code)
            {
                uint length = (uint)code.Length;

                // Save existing memory contents
                byte[] oldCode = new byte[length];
                for (uint codeOffset = 0; codeOffset < length; codeOffset++)
                {
                    oldCode[codeOffset] = Machine.Memory.ReadByte(address + codeOffset);
                }

                // Load the code into memory at the specified address.
                for (uint codeOffset = 0; codeOffset < length; codeOffset++)
                {
                    Machine.Memory.WriteByte(address + codeOffset, code[codeOffset]);
                }

                // Disassemble
                var list = Disassemble(address, length);

                // Restore original memory contents
                for (uint codeOffset = 0; codeOffset < length; codeOffset++)
                {
                    Machine.Memory.WriteByte(address + codeOffset, oldCode[codeOffset]);
                }

                return list;    
            }

            /// <summary>
            /// Perform a full disassembly of the specified block of memory.
            ///
            /// In the case where a non-executable section is in the list, there may
            /// be many records for a single section.  In that case, account for the
            /// fact that the first record may not have been on an alignment boundary
            /// from the start of that section and adjust it accordingly so that the
            /// remaining records are aligned correctly.  If the final record is
            /// truncated by "length", then adjust the length of the record to be
            /// consistent with the length.
            /// </summary>
            /// <description>
            /// DisassemblyRecord output is compatible with vasm using the following options:
            ///     vasm.exe -m68000 -Fsrec -exec -o output.h68 -L output.lis output.a68
            /// </description>
            /// <param name="startAddress">The start effectiveAddress of the block of memory being disassembled.</param>
            /// <param name="length">The length (in bytes) of the block of memory being disassembled.</param>
            /// <param name="maxRecords">Maximum number of instructions or nonexecutable sections to disassemble.</param>
            /// <returns>A list of <see cref="DisassemblyRecord"/>.
            /// </returns>
            public List<DisassemblyRecord> Disassemble(uint startAddress, uint length, int maxRecords = int.MaxValue)
            {
                try
                {
                    uint legalAddress = GetClosestLowerLegalAddress(startAddress);
                    length += startAddress - legalAddress;

                    // Set machine parameters for this disassembler machine
                    Disassembling = true;
                    Machine.SetPC(startAddress);
                    Machine.SetExecutionLimits(startAddress, length);

                    StartAddress = startAddress;
                    Length = length;
                    CurrentAddress = StartAddress;

                    List<DisassemblyRecord> result = [];

                    // When Length is exceeded, loop exits because IsEndOfData goes true.
                    int count = 0;
                    while (!IsEndOfData && count++ < maxRecords)
                    {
                        NonExecutableSection? section = MachineNonExecutableSections.GetSectionIncluding(CurrentAddress);
                        DisassemblyRecord? record = null;
                        bool oddAddress = (CurrentAddress & 1) == 1;

                        if (section == null)
                        {
                            // Not in a non-executable section - disassemble instruction if not at odd address
                            if (!oddAddress)
                            {
                                record = DisassembleInstruction(); // increments CurrentAddress if record returned
                            }
                            if (record == null)
                            {
                                // Failed to disassemble or odd address - restore address and create minimal non-exec section
                                // This section will be either 1 or 2 bytes depending on whether the address is odd
                                // and if there is enough room for a two-byte section if the address is even.
                                uint len = Math.Min(2u, Length - (CurrentAddress - StartAddress));
                                OpSize opSize = len == 1u || oddAddress ? OpSize.Byte : OpSize.Word;
                                len = opSize == OpSize.Byte ? 1u : 2u;

                                // Create a minimal 1 or 2 byte section
                                section = new NonExecutableSection(CurrentAddress, len, opSize);

                                record = GetNonExecutableSectionRecord(CurrentAddress, len, section);
                            }
                        }
                        else
                        {
                            // In non-executable section - create record for part of section
                            uint itemSize = OpSizeToLength(section!.ItemOpSize);

                            // Three constraints on record length
                            uint sectionRemaining = section.Length - (CurrentAddress - section.Address);
                            uint maxLenForOneLine = itemSize * section.ItemsPerLine;
                            uint disassemblyRemaining = Length - (CurrentAddress - StartAddress);

                            // Take minimum of all three
                            uint len = Math.Min(sectionRemaining, Math.Min(maxLenForOneLine, disassemblyRemaining));

                            record = GetNonExecutableSectionRecord(CurrentAddress, len, section);
                        }

                        result.Add(record);
                    }

                    // Logging: record result count at exit
#if DEBUG_HIDE
                    System.Diagnostics.Debug.WriteLine($"Disassemble completed: startAddress=0x{startAddress:X6}, length=0x{length:X}, maxCount={maxCount}, recordCount={result.Count}");
                    Logger.Log(LogLevel.Trace, "DISASSEMBLER", () => $"Disassemble completed: startAddress=0x{startAddress:X6}, length=0x{length:X}, maxCount={maxCount}, recordCount={result.Count}");
#endif

                    return result;
                }
                catch (Exception e)
                {
                    Logger.Log(LogLevel.Critical, "DISASSEMBLER", () => $"Disassemble: {e.Message}");
                    throw;
                }
                finally
                {
                    Disassembling = false;
                }
            }

            /// <summary>
            /// Return the closest legal address less than or equal to the given address in the section.
            /// </summary>
            /// <param name="address"></param>
            /// <param name="section"></param>
            /// <returns></returns>
            public uint GetClosestLowerLegalAddress(uint address)
            {
                return GetClosestLowerLegalAddress(address, MachineNonExecutableSections.GetSectionIncluding(address));
            }

            /// <summary>
            /// Return the closest legal address less than or equal to the given address in the section.
            /// </summary>
            /// <param name="address"></param>
            /// <param name="section"></param>
            /// <returns></returns>
            public static uint GetClosestLowerLegalAddress(uint address, NonExecutableSection? section)
            {
                address = Make24BitAddress(address);
                if (section != null)
                {
                    uint itemOpSize = OpSizeToLength(section.ItemOpSize);
                    uint startIntoSection = address - section.Address;
                    uint remainder = startIntoSection % itemOpSize;
                    if (remainder != 0)
                    {
                        // The address is somewhere in the section where the alignment doesn't
                        // match the opsize.  Round down to the next lowest address that is
                        // on an alignment boundary.
                        address -= remainder;
                    }
                    return address;
                }
                // Not in a non-executable section, so address must be even.
                if ((address & 1) != 0)
                {
                    address--;
                }
                return address;
            }

            /// <summary>
            /// Return a Disassembly record for the section that starts at <see cref="address"/>
            /// and has the requested <see cref="length"/>.
            ///
            /// Note that the actual section may start at a much lower address and continue on past the
            /// requested length so handle appropriately.  Also, the requested section may
            /// end prior to the length passed in, so also handle that appropriately.
            ///
            /// In the case where a non-executable section is large, there may
            /// be many records for a single section.  In that case, account for the
            /// fact that the first record may not have been on an alignment boundary
            /// from the start of that section and return an assembly record that starts
            /// on an alignment boundary.
            ///
            /// If the final record is truncated by "length", then adjust the length of the
            /// record to be consistent with the length.
            /// </summary>
            /// <param name="address">starting address of this disassembly record</param>
            /// <param name="length">max length of disassembly record in bytes</param>
            /// <param name="section">non-executable section that contains the address.</param>
            /// <returns></returns>
            protected DisassemblyRecord GetNonExecutableSectionRecord(uint address, uint length, NonExecutableSection section)
            {
                try
                {
                    Machine.Memory.Disassembling = true;
                    address = GetClosestLowerLegalAddress(address, section);

                    uint itemOpSize = OpSizeToLength(section.ItemOpSize);

                    Directive dir = new(address, "DC", section.ItemOpSize);

                    uint itemsPerLine = section.ItemsPerLine;

                    // Shrink the OpSize if needed.  The
                    if (itemOpSize > length)
                    {
                        if (length == 2)
                        {
                            dir.Size = OpSize.Word;
                        }
                        else
                        {
                            dir.Size = OpSize.Byte;
                        }
                    }
                    itemOpSize = OpSizeToLength(dir.Size!.Value);

                    length = Math.Min(length, itemOpSize * itemsPerLine);

                    // Length of NES that is contained in this record.
                    uint nesRecordLength = section.Length - (address - section.Address);

                    uint recordLength = Math.Min(length, nesRecordLength);
                    dir.MachineCode = new byte[recordLength];
                    for (uint i = 0; i < recordLength; i++)
                    {
                        // Can't use ReadNextByte() because NonExecutableDataDisassembly(...)
                        // will call it below and calling it here would result in double
                        // incrementing CurrentAddress.  Note that Machine.Memory can be
                        // overridden in derived classes to access memory-mapped I/O as well
                        // (also applies to ReadNextByte() since it calls Machine.Memory.ReadByte(...),
                        // - so I/O could be read twice).
                        dir.MachineCode[i] = Machine.Memory.ReadByte(address + i);
                    }

                    NonExecutableDataDisassembly(dir, length, address, section.DisplayRadix);
                    var record = new DisassemblyRecord(dir);
                    return record;
                }
                finally
                {
                    Machine.Memory.Disassembling = false;
                }
            }

            /// <summary>
            /// Return the byte located at the current effectiveAddress, and then increment the current effectiveAddress value.
            /// </summary>
            /// <returns>The byte located at the current effectiveAddress.</returns>
            protected byte ReadNextByte()
            {
                if (IsEndOfData)
                {
                    throw new EndOfDataException("Disassembly has run past the end of the loaded data.");
                }
                byte value = Machine.Memory.ReadByte(CurrentAddress);
                CurrentAddress++;
                return value;
            }

            static readonly byte[] _bytes = new byte[NonExecutableSections.MAX_NES_BYTES_PER_RECORD];
            static readonly StringBuilder _asciiBuilder = new();

            /// <summary>
            /// Return a string of the bytes in the array as ASCII characters.
            /// </summary>
            /// <param name="array"></param>
            /// <param name="length"></param>
            /// <returns></returns>
            static string GetBytesAsString(byte[] array, uint length)
            {
                _asciiBuilder.Clear();
                for (int i = 0; i < length && i < array.Length; i++)
                {
                    char ch = Encoding.ASCII.GetString(array, i, 1)[0];
                    if (Char.IsLetterOrDigit(ch) || Char.IsPunctuation(ch) || Char.IsSymbol(ch) || (ch == ' '))
                    {
                        _asciiBuilder.Append(ch);
                    }
                    else
                    {
                        _asciiBuilder.Append(' ');
                    }
                }
                return _asciiBuilder.ToString();
            }

            /// <summary>
            /// Generate a line of disassembly for (part of) a non-executable section.
            /// </summary>
            /// <param name="dir">Directive that specifies the name, size, and number of operands</param>
            /// <param name="length"></param>
            /// <param name="startAddress">Address of first operand</param>
            /// <param name="radix">radix used to display values of operands</param>
            /// <returns></returns>
            protected string? NonExecutableDataDisassembly(Directive dir, uint length, uint startAddress, uint radix)
            {
                string? error = null;
                StringBuilder sb = new();
                if (dir.Size != OpSize.Byte && dir.Size != OpSize.Word && dir.Size != OpSize.Long)
                {
                    dir.Assembly = $"[ERROR] NonExecutableDataDisassembly called with incompatible size: {dir.Size}";
                    return $"[ERROR] NonExecutableDataDisassembly called with incompatible size: {dir.Size}";
                }
                uint itemSize = dir.Size switch { OpSize.Byte => 1, OpSize.Word => 2, OpSize.Long => 4, _ => 1 };
                string? format;
                if (radix == 2)
                {
                    format = itemSize == 1 ? "%{0:B8}" : itemSize == 2 ? "%{0:B16}" : "%{0:B32}";
                }
                else if (radix == 10)
                {
                    format = "{0}";
                }
                else
                {
                    format = itemSize == 1 ? "${0:x2}" : itemSize == 2 ? "${0:x4}" : "${0:x8}";
                }
                uint items = Math.Max(1, length / itemSize);
                uint remainder = length % itemSize;
                OpSize dirSize = dir.Size ?? OpSize.Word;
                if (remainder != 0)
                {
                    error = $"[ERROR] NonExecutableDataDisassembly called with incompatible length for {dir.Size}: {length}";
                    dirSize = OpSize.Byte;
                }
                string dc;
                if (dirSize == OpSize.Long)
                {
                    dc = "DC.L";
                }
                else if (dirSize == OpSize.Word)
                {
                    dc = "DC.W";
                }
                else  // (dirSize == OpSize.Byte)
                {
                    dc = "DC.B";
                }
                sb.Append(dc);
                sb.AppendTab(EA_COLUMN);
                Array.Clear(_bytes);

                for (int i = 0; i < items; i++)
                {
                    if (i > 0)
                    {
                        sb.Append(',');
                    }
                    uint val = 0;
                    for (int j = 0; j < itemSize; j++)
                    {
                        if (IsEndOfData) { break; }
                        byte value = ReadNextByte();
                        _bytes[i * itemSize + j] = value;
                        val = (val << 8) | value;
                    }

                    ImmediateOperand op = dirSize switch
                    {
                        OpSize.Byte => new ImmediateOperand((byte)val, format),
                        OpSize.Word => new ImmediateOperand((ushort)val, format),
                        OpSize.Long => new ImmediateOperand(val, format),
                        _ => new ImmediateOperand(val, format),
                    };
                    dir.Operands.Add(op);
                    sb.Append(op);
                }

                dir.Assembly = sb.ToString();
                dir.PostOperandAnnotation = $"    '{GetBytesAsString(_bytes, length)}'";

                return error;
            }

            /// <summary>
            /// Flag used to indicate that the disassembler is currently disassembling
            /// for the purpose of disabling memory alignment checks and I/O operations.
            /// </summary>
            protected bool Disassembling
            {
                get
                {
                    return Machine.Memory.Disassembling;
                }
                set
                {
                    Machine.Memory.Disassembling = value;
                }
            }

            /// <summary>
            /// Disassemble one instruction at the current address.
            /// If the instruction is not legal, return null since this is
            /// either for a different processor or is just data.
            /// Leaves CurrentAddress unchanged on failure.
            /// </summary>
            /// <returns></returns>
            protected DisassemblyRecord? DisassembleInstruction()
            {
                DisassemblyRecord? record = null;
                uint oldAddress = CurrentAddress;
                try
                {
                    do
                    {
                        Disassembling = true;
                        Operation? op;

                        // Decoder fetches the instruction at the current PC, so set it to
                        // where we want to disassembler.
                        Machine.SetPC(CurrentAddress);

                        Instruction? inst = Machine.Decoder.FetchInstruction();
                        if (inst == null)
                        {
                            break;
                        }
                        // PC has been incremented to point to the next instruction.
                        int length = (int)inst.Length;

                        List<byte> codeBytes = [];

                        // Show the actual instruction bytes
                        int i;
                        for (i = 0; (i < length) && !IsEndOfData; i++)
                        {
                            byte value = ReadNextByte();
                            codeBytes.Add(value);
                        }
                        if (IsEndOfData && i < length)
                        {
                            // End of memory block before we finished
                            break;
                        }
                        else
                        {
                            StringBuilder sb = new();
                            if (_handlers.TryGetValue(inst.Info.HandlerID, out DisassemblyHandler? instructionDisassembler))
                            {
                                op = instructionDisassembler(inst, sb);
                                if (op == null)
                                {
                                    // Handler could not generate disassembly (probably illegal addressing mode)
                                    break;
                                }
                            }
                            else
                            {
                                // No handler
                                break;
                            }
                            byte[] machineCode = [.. codeBytes];
                            op!.MachineCode = machineCode;
                            op.Assembly = sb.ToString();
                            record = new DisassemblyRecord(op, IsEndOfData);
                        }
                    } while (false);
                }
                catch (Exception e)
                {
                    Logger.Log(LogLevel.Error, "DISASSEMBLER", () => $"DisassembleInstruction: {e.Message}");
                }
                finally
                {
                    if (record == null)
                    {
                        // Restore address on failure
                        CurrentAddress = oldAddress;
                    }
                    else
                    {
                        CurrentAddress = oldAddress + (uint)record.MachineCode.Length;
                    }
                    Machine.SetPC(CurrentAddress);

                    Disassembling = false;
                }
                return record;
            }

            /// <summary>
            /// Append spaces up to the tab stop.  Guaranteed at least
            /// one space.
            /// </summary>
            /// <param name="tabStop"></param>
            /// <param name="sb"></param>
            public static void AppendTab(int tabStop, StringBuilder sb)
            {
                do
                {
                    sb.Append(' ');
                } while (sb.Length < tabStop);
            }

            /// <summary>
            /// Append the instruction size and tab.
            /// </summary>
            /// <param name="inst"></param>
            /// <param name="sb"></param>
            protected OpSize AppendSizeAndTab(Instruction inst, StringBuilder sb)
            {
                OpSize size = inst.Size ?? OpSize.Word;
                AppendSizeAndTab(size, sb);
                return size;
            }

            /// <summary>
            /// Append the instruction size and tab.
            /// </summary>
            /// <param name="size"></param>
            /// <param name="sb"></param>
            public static OpSize AppendSizeAndTab(OpSize? size, StringBuilder sb)
            {
                string sSize = size switch
                {
                    OpSize.Byte => ".B",
                    OpSize.Long => ".L",
                    _ => ".W"
                };
                if (size != null)
                {
                    sb.Append(sSize);
                }
                sb.AppendTab(EA_COLUMN);
                return size ?? OpSize.Word;
            }

            /// <summary>
            /// Append a condition code.
            /// </summary>
            /// <param name="cond"></param>
            /// <param name="sb"></param>
            /// <returns>Condition code string, e.g., "LE", "GT", etc.</returns>
            protected static string AppendCondition(Condition cond, StringBuilder sb)
            {
                string sCond = cond.ToString();
                sb.Append(sCond);
                return sCond;
            }

            /// <summary>
            /// Return true if the effective address is a memory reference.
            /// </summary>
            /// <param name="instruction"></param>
            /// <param name="eaType"></param>
            /// <returns></returns>
            protected bool EffectiveAddressIsMemory(Instruction instruction, EAType eaType)
            {
                Operand op = EffectiveAddressOp(instruction, eaType);
                return op.IsMemory;
            }

            /// <summary>
            /// Subclasses can override and return a label for this address.
            ///
            /// The disassembly will use this label rather than the absolute
            /// address passed in.  If the subclass returns <c>null</c>, the
            /// disassembly will show the absolute address instead.
            /// </summary>
            /// <param name="address"></param>
            /// <param name="refAddress">(optional) Address from which this label is referenced</param>
            /// <returns></returns>
            protected virtual string? GetLabelName(uint address, uint? refAddress)
            {
                return null;
            }

            /// <summary>
            /// Subclasses can override this to return a symbolic expression for
            /// the expression at this address and operand position.
            ///
            /// Operand position:
            ///     0 = source
            ///     1 = dest
            ///     more if a directive like <c>DC.B  $23,$45,$ea,$8f</c>,
            ///                               which has 4 operands numbered 0-3
            ///
            /// An expression is a (possibly symbolic) string that is legal in
            /// assembler and that resolves to the constant value in the op code
            /// operand (other than register references).
            ///
            /// For example, in the assembly line
            ///   <c>MOVE.B  $e8,$08(A0,D2.W)</c>
            ///
            /// the operation has two operands: <c>$e8</c> and <c>$08(A0,D2.W)</c>.
            /// The source operand has the expression <c>$e8</c> that can be replaced
            /// by this function with a symbolic expression.  For example, if
            /// the following EQU is in the code,
            ///
            /// <c>MouseOffset  EQU  $08+$e0</c>
            ///
            /// then, if the above MOVE.B operation is at address <c>$00400234</c>, the
            /// subclass might return the expression <c>MouseOffset</c> in response to the
            /// call:
            ///
            /// <c>string? expression = GetExpression($00400234, 0); // Address = $00400234, </c>
            /// <c>                                                  // operand position = 0 (source)</c>
            ///
            /// The disassembly will now use <c>MouseOffset</c> rather than <c>$e8</c> to make for
            /// easier understanding.
            ///
            /// </summary>
            /// <param name="address"></param>
            /// <param name="operandPos"></param>
            /// <returns></returns>
            protected virtual string? GetExpression(uint address, int operandPos)
            {
                return null;
            }

            /// <summary>
            /// Evaluate the specified effective effectiveAddress (EA).
            /// </summary>
            /// <param name="instruction">The <see cref="Instruction"/> instance.</param>
            /// <param name="eaType">The type of effective effectiveAddress to be evaluated (Source or Destination).</param>
            /// <returns>Operand</returns>
            protected Operand EffectiveAddressOp(Instruction instruction, EAType eaType)
            {
                ushort? ea = eaType == EAType.Source ? instruction.SourceAddrMode : instruction.DestAddrMode;
                ushort? ext1 = eaType == EAType.Source ? instruction.SourceExtWord1 : instruction.DestExtWord1;
                ushort? ext2 = eaType == EAType.Source ? instruction.SourceExtWord2 : instruction.DestExtWord2;

                uint? address;
                uint? immVal;
                bool isMemory = true;
                OpSize? size = null;
                Operand? operand = null;
                if (ea.HasValue)
                {
                    OpSize opSize = instruction.Size ?? OpSize.Word;

                    // Get register number (for addressing modes that use a register)
                    ushort regNum = (ushort)(ea & 0x0007);
                    switch (ea & 0x0038)
                    {
                        case (byte)AddrMode.DataRegister:
                            operand = new DataRegisterOperand(regNum, opSize);
                            isMemory = false;
                            break;
                        case (byte)AddrMode.AddressRegister:
                            operand = new AddressRegisterOperand(regNum);
                            isMemory = false;
                            break;
                        case (byte)AddrMode.Address:
                            operand = new AddressOperand(regNum);
                            break;
                        case (byte)AddrMode.AddressPostInc:
                            operand = new AddressPostIncOperand(regNum);
                            break;
                        case (byte)AddrMode.AddressPreDec:
                            operand = new AddressPreDecOperand(regNum);
                            break;
                        case (byte)AddrMode.AddressDisp:
                            operand = new AddressDispOperand(regNum, (short)ext1!.Value);
                            break;
                        case (byte)AddrMode.AddressIndex:
                            {
                                sbyte disp = (sbyte)(ext1!.Value & 0x00FF);
                                int indexRegNum = (ext1!.Value & 0x7000) >> 12;
                                OpSize sz = (ext1!.Value & 0x0800) == 0 ? OpSize.Word : OpSize.Long;
                                bool indexIsAddressRegister = (ext1!.Value & 0x8000) != 0;
                                operand = new AddressIndexOperand(AddressRegisters[regNum], indexRegNum, indexIsAddressRegister,  sz, disp);
                            }
                            break;
                        case 0x0038:
                            switch (ea)
                            {
                                case (byte)AddrMode.AbsShort:
                                    address = ext1!.Value | ((ext1!.Value & 0x8000) == 0 ? 0x0 : 0xFFFF0000);
                                    operand = new LabelOperand(address.Value, AddrMode.AbsShort);
                                    break;
                                case (byte)AddrMode.AbsLong:
                                    address = (uint)((ext1!.Value << 16) + ext2!.Value);
                                    operand = new LabelOperand(address.Value, AddrMode.AbsLong);
                                    size = OpSize.Long;
                                    break;
                                case (byte)AddrMode.PCDisp:
                                    {
                                        int pcDecrement = 2; // Assume source, PC just after ext1 or dest, PC just after ext1
                                        if (eaType == EAType.Source && instruction.DestExtWord1 != null)
                                        {
                                            pcDecrement += (instruction.DestExtWord2 == null) ? 2 : 4;
                                        }

                                        address = (uint)((int)Machine.CPU.CurrentPC - pcDecrement + (short)ext1!.Value);
                                        operand = new LabelOperand(address.Value, AddrMode.PCDisp);
                                    }
                                    break;
                                case (byte)AddrMode.PCIndex:
                                    {
                                        byte disp = (byte)(ext1!.Value & 0x00FF);
                                        byte indexRegNum = (byte)((ext1!.Value & 0x7000) >> 12);
                                        OpSize sz = (ext1.Value & 0x0800) == 0 ? OpSize.Word : OpSize.Long;
                                        bool indexIsAddressRegister = (ext1.Value & 0x8000) != 0;

                                        // PC has been incremented past the extension word.  The definition of
                                        // PC displacement uses the value of the extension word address as the PC value.
                                        int pcDecrement = 2; // Assume source, PC just after ext1 or dest, PC just after ext1
                                        if (eaType == EAType.Source && instruction.DestExtWord1 != null)
                                        {
                                            pcDecrement += (instruction.DestExtWord2 == null) ? 2 : 4;
                                        }
                                        uint baseAddress = (uint)((int)Machine.CPU.CurrentPC - pcDecrement + (sbyte)disp);

                                        operand = new PCIndexOperand(indexRegNum, indexIsAddressRegister, baseAddress, sz);
                                    }
                                    break;
                                case (byte)AddrMode.Immediate:
                                    if (opSize == OpSize.Long)
                                    {
                                        immVal = (uint)((ext1!.Value << 16) + ext2!.Value);
                                        operand = new ImmediateOperand(immVal!.Value);
                                        size = OpSize.Long;
                                    }
                                    else if (opSize == OpSize.Word)
                                    {
                                        immVal = ext1!.Value;
                                        operand = new ImmediateOperand((ushort)immVal!.Value);
                                    }
                                    else if (opSize == OpSize.Byte)
                                    {
                                        immVal = ext1!.Value;
                                        operand = new ImmediateOperand((byte)immVal!.Value);
                                    }
                                    else
                                    {
                                        throw new NotSupportedException("Unsupported opSize in Immediate addressing mode");
                                    }
                                    isMemory = false;
                                    break;
                            }
                            break;
                    }
                }

                if (operand == null)
                {
                    throw new NotSupportedException("Unsupported effective address");
                }
                else
                {
                    Debug.Assert(operand.AddressMode != null);

                    operand.IsMemory = isMemory;
                    operand.Size = size;
                }
                return operand;
            }

            /// <summary>
            /// Append the instruction mnemonic.
            /// </summary>
            /// <param name="inst"></param>
            /// <param name="sb"></param>
            protected Operation AppendMnemonic(Instruction inst, StringBuilder sb)
            {
                Operation op = new(Machine.CurrentInstructionAddress, inst.Info.Mnemonic);
                sb.Append(inst.Info.Mnemonic);
                return op;
            }

            /// <summary>
            /// If SourceExtWord1 is missing, append an error message to the
            /// StringBuilder and return false, else return true;
            /// </summary>
            /// <param name="inst"></param>
            /// <param name="sb"></param>
            /// <returns></returns>
            protected static bool HasSourceExtWord1(Instruction inst, StringBuilder sb)
            {
                if (inst.SourceExtWord1.HasValue)
                {
                    return true;
                }
                sb.Append("[SourceExtWord1 missing]");
                return false;
            }

            // ***************************
            //
            // Instruction handler methods
            //
            // ***************************

            protected Operation? PEA(Instruction inst, StringBuilder sb)
            {
                Operation op = AppendMnemonic(inst, sb);
                op.Size = OpSize.Long;
                sb.AppendTab(EA_COLUMN);

                op.Operands.Add(EffectiveAddressOp(inst, EAType.Source));

                sb.Append(op.Operands);
                return op;
            }

            protected Operation? DST(Instruction inst, StringBuilder sb)
            {
                Operation op = AppendMnemonic(inst, sb);
                op.Size = AppendSizeAndTab(inst, sb);

                op.Operands.Add(EffectiveAddressOp(inst, EAType.Destination));

                sb.Append(op.Operands);
                return op;
            }

            protected Operation? IMMEDtoCCR(Instruction inst, StringBuilder sb)
            {
                string mnemonic = inst.Info.Mnemonic;
                mnemonic = mnemonic[..^"toCCR".Length];
                Operation op = new(Machine.CurrentInstructionAddress, mnemonic);
                sb.Append(mnemonic);
                sb.AppendTab(EA_COLUMN);

                // SourceExtWord1 holds the immediate operand value.
                if (HasSourceExtWord1(inst, sb))
                {
                    ushort value = (ushort)(inst.SourceExtWord1!.Value & 0x001F);

                    op.Operands.Add(new ImmediateOperand((byte)value));
                    op.Operands.Add(new CCROperand());

                    sb.Append(op.Operands);
                }
                else
                {
                    return null; // "Expecting SourceExtWord1"
                }
                return op;
            }

            protected Operation? IMMEDtoSR(Instruction inst, StringBuilder sb)
            {
                string mnemonic = inst.Info.Mnemonic;
                mnemonic = mnemonic[..^"toSR".Length];
                Operation op = new(Machine.CurrentInstructionAddress, mnemonic);
                sb.Append(mnemonic);
                sb.AppendTab(EA_COLUMN);

                // SourceExtWord1 holds the immediate operand value.
                if (inst.SourceExtWord1.HasValue)
                {
                    ushort value = inst.SourceExtWord1.Value;

                    op.Operands.Add(new ImmediateOperand(value));
                    op.Operands.Add(new SROperand());

                    sb.Append(op.Operands);
                }
                else
                {
                    return null; // "Expecting SourceExtWord1"
                }
                return op;
            }

            protected Operation? MOVEtoSR(Instruction inst, StringBuilder sb)
            {
                Operation op = new(Machine.CurrentInstructionAddress, "MOVE");
                sb.Append("MOVE");
                sb.AppendTab(EA_COLUMN);

                op.Operands.Add(EffectiveAddressOp(inst, EAType.Source));
                op.Operands.Add(new SROperand());

                sb.Append(op.Operands);
                return op;
            }

            protected Operation? MOVEtoCCR(Instruction inst, StringBuilder sb)
            {
                Operation op = new(Machine.CurrentInstructionAddress, "MOVE");
                sb.Append("MOVE");
                sb.AppendTab(EA_COLUMN);

                op.Operands.Add(EffectiveAddressOp(inst, EAType.Source));
                op.Operands.Add(new CCROperand());

                sb.Append(op.Operands);
                return op;
            }

            protected Operation? MOVEfromSR(Instruction inst, StringBuilder sb)
            {
                Operation op = new(Machine.CurrentInstructionAddress, "MOVE");
                sb.Append("MOVE");
                sb.AppendTab(EA_COLUMN);

                op.Operands.Add(new SROperand());
                op.Operands.Add(EffectiveAddressOp(inst, EAType.Destination));

                sb.Append(op.Operands);
                return op;
            }

            protected Operation? IMMED_OP(Instruction inst, StringBuilder sb)
            {
                Operation op = AppendMnemonic(inst, sb);
                OpSize opSize = AppendSizeAndTab(inst, sb);
                op.Size = opSize;
                uint value = OpcodeExecutionHandler.GetSizedOperandValue(opSize, inst.SourceExtWord1, inst.SourceExtWord2);
                Operand operand;
                switch (opSize) 
                {
                    case OpSize.Byte:
                        operand = new ImmediateOperand((byte)value);
                        break;
                    case OpSize.Word:
                        operand = new ImmediateOperand((short)value);
                        break;
                    case OpSize.Long:
                        operand = new ImmediateOperand((uint)value);
                        break;
                    default:
                        return null; // "Operation size not supported"                    
                }
                op.Operands.Add(operand);
                op.Operands.Add(EffectiveAddressOp(inst, EAType.Destination));

                sb.Append(op.Operands);
                return op;
            }

            protected Operation? MULS_MULU_DIVU_DIVS(Instruction inst, StringBuilder sb)
            {
                Operation op = AppendMnemonic(inst, sb);
                op.Size = OpSize.Word;
                sb.Append(".W");
                sb.AppendTab(EA_COLUMN);

                int dRegNum = (inst.Opcode & 0x0E00) >> 9;

                op.Operands.Add(EffectiveAddressOp(inst, EAType.Source));
                op.Operands.Add(new DataRegisterOperand(dRegNum));

                sb.Append(op.Operands);
                return op;
            }

            protected Operation? SUBX(Instruction inst, StringBuilder sb)
            {
                Operation op = AppendMnemonic(inst, sb);
                op.Size = AppendSizeAndTab(inst, sb);
                bool isAddressPreDecrement = (inst.Opcode & 0x0008) != 0;
                int srcReg = inst.Opcode & 0x0007;
                int dstReg = (inst.Opcode & 0x0E00) >> 9;
                if (isAddressPreDecrement)
                {
                    op.Operands.Add(new AddressPreDecOperand(srcReg));
                    op.Operands.Add(new AddressPreDecOperand(dstReg));
                }
                else
                {
                    op.Operands.Add(new DataRegisterOperand(srcReg));
                    op.Operands.Add(new DataRegisterOperand(dstReg));
                }
                sb.Append(op.Operands);
                return op;
            }

            protected Operation? ADD_SUB_OR_AND_EOR_CMP(Instruction inst, StringBuilder sb)
            {
                Operation op = AppendMnemonic(inst, sb);
                op.Size = AppendSizeAndTab(inst, sb);
                int dRegNum = (inst.Opcode & 0x0E00) >> 9;
                bool dnDest = (inst.Opcode & 0x0100) == 0;
                EAType eaType = inst.SourceAddrMode.HasValue ? EAType.Source : EAType.Destination;
                if (dnDest)
                {
                    op.Operands.Add(EffectiveAddressOp(inst, eaType));
                    op.Operands.Add(new DataRegisterOperand(dRegNum));
                }
                else
                {
                    op.Operands.Add(new DataRegisterOperand(dRegNum));
                    op.Operands.Add(EffectiveAddressOp(inst, eaType));
                }
                sb.Append(op.Operands);
                return op;
            }

            protected Operation? CMPM(Instruction inst, StringBuilder sb)
            {
                Operation op = AppendMnemonic(inst, sb);
                op.Size = AppendSizeAndTab(inst, sb);

                byte aDstRegNum = (byte)((inst.Opcode & 0x0E00) >> 9);
                byte aSrcRegNum = (byte)(inst.Opcode & 0x0007);

                op.Operands.Add(new AddressPostIncOperand(aSrcRegNum));
                op.Operands.Add(new AddressPostIncOperand(aDstRegNum));

                sb.Append(op.Operands);
                return op;
            }

            protected Operation? MOVE(Instruction inst, StringBuilder sb)
            {
                Operation op = AppendMnemonic(inst, sb);
                op.Size = AppendSizeAndTab(inst, sb);

                Operand src = EffectiveAddressOp(inst, EAType.Source);
                op.Operands.Add(src);
                Operand dst = EffectiveAddressOp(inst, EAType.Destination);
                if (dst.AddressMode == AddrMode.AddressRegister ||
                    dst.AddressMode == AddrMode.Immediate ||
                    dst.AddressMode == AddrMode.PCIndex ||
                    dst.AddressMode == AddrMode.PCDisp)
                {
                    return null;
                }
                op.Operands.Add(dst);

                sb.Append(op.Operands);
                return op;
            }

            protected Operation? MOVEA(Instruction inst, StringBuilder sb)
            {
                Operation op = AppendMnemonic(inst, sb);
                op.Size = AppendSizeAndTab(inst, sb);
                int regNum = (inst.Opcode & 0x0E00) >> 9;

                op.Operands.Add(EffectiveAddressOp(inst, EAType.Source));
                op.Operands.Add(new AddressRegisterOperand(regNum));

                sb.Append(op.Operands);
                return op;
            }

            protected Operation? MOVEP(Instruction inst, StringBuilder sb)
            {
                Operation op = AppendMnemonic(inst, sb);
                OpSize size = (inst.Opcode & 0x0040) == 0 ? OpSize.Word : OpSize.Long;
                op.Size = size;
                string sz = size == OpSize.Word ? ".W" : ".L";
                sb.Append(sz);
                sb.AppendTab(EA_COLUMN);

                byte aRegNum = (byte)(inst.Opcode & 0x0007);
                byte dRegNum = (byte)((inst.Opcode & 0x0E00) >> 9);
                bool memToReg = (inst.Opcode & 0x0080) == 0;
                if (inst.SourceExtWord1.HasValue)
                {
                    int disp = Helpers.SignExtendValue((uint)inst.SourceExtWord1, OpSize.Word);

                    if (memToReg)
                    {
                        op.Operands.Add(new AddressDispOperand(aRegNum, (short)disp));
                        op.Operands.Add(new DataRegisterOperand(dRegNum));
                    }
                    else
                    {
                        op.Operands.Add(new DataRegisterOperand(dRegNum));
                        op.Operands.Add(new AddressDispOperand(aRegNum, (short)disp));
                    }
                }
                else
                {
                    return null; // "Expecting SourceExtWord1"
                }

                sb.Append(op.Operands);
                return op;
            }

            protected Operation? MOVEM(Instruction inst, StringBuilder sb)
            {
                Operation op = AppendMnemonic(inst, sb);
                OpSize size = inst.Size ?? OpSize.Long;
                op.Size = AppendSizeAndTab(size, sb);
                if (inst.SourceExtWord1.HasValue)
                {
                    ushort regMask = inst.SourceExtWord1.Value;
                    bool regToMem = (inst.Opcode & 0x0400) == 0;

                    if (regToMem)
                    {
                        // Source is reg(s), dest is EA
                        if (((inst.Opcode >> 3) & 0x0007) == 0x0004)
                        {
                            // Predecrement mode
                            op.Operands.Add(new RegListOperand(regMask, preDec: true));
                        }
                        else
                        {
                            op.Operands.Add(new RegListOperand(regMask, preDec: false));
                        }
                        op.Operands.Add(EffectiveAddressOp(inst, EAType.Destination));
                    }
                    else
                    {
                        // Source is mem, dest is reg (but EA is in dest field)
                        op.Operands.Add(EffectiveAddressOp(inst, EAType.Destination));
                        op.Operands.Add(new RegListOperand(regMask, preDec: false));
                    }
                }
                else
                {
                    return null; // "Expecting SourceExtWord1"
                }

                sb.Append(op.Operands);
                return op;
            }

            protected Operation? MOVEQ(Instruction inst, StringBuilder sb)
            {
                Operation op = AppendMnemonic(inst, sb);
                op.Size = OpSize.Long;
                sb.Append(".L");
                sb.AppendTab(EA_COLUMN);

                int dRegNum = (inst.Opcode & 0x0E00) >> 9;
                int data = Helpers.SignExtendValue((uint)(inst.Opcode & 0x00FF), OpSize.Byte);

                op.Operands.Add(new QuickDataOperand(data));
                op.Operands.Add(new DataRegisterOperand(dRegNum));

                sb.Append(op.Operands);
                return op;
            }

            protected Operation? ADDQ_SUBQ(Instruction inst, StringBuilder sb)
            {
                Operation op = AppendMnemonic(inst, sb);

                sbyte addVal = (sbyte)((inst.Opcode & 0x0E00) >> 9);
                if (addVal == 0)
                {
                    addVal = 8;
                }

                int size = (inst.Opcode & 0x00E0) >> 6;
                OpSize? opSize;
                string sz;
                switch (size)
                {
                    case 0:
                        sz = ".B";
                        opSize = OpSize.Byte;
                        break;
                    case 1:
                        sz = ".W";
                        opSize = OpSize.Word;
                        break;
                    case 2:
                        sz = ".L";
                        opSize = OpSize.Long;
                        break;
                    default:
                        return null;
                }
                op.Size = opSize;
                sb.Append(sz);
                sb.AppendTab(EA_COLUMN);

                op.Operands.Add(new QuickDataOperand(addVal));

                // When being applied to an effectiveAddress register, we work with the entire 32-bit value regardless
                // of the size that has been specified. This operation also doesn't affect the flags.
                if ((inst.Opcode & 0x0038) == (int)AddrMode.AddressRegister)
                {
                    if (size == 0 || size == 3)
                    {
                        // Incompatible size
                        return null;
                    }
                    int regNum = inst.Opcode & 0x0007;
                    op.Operands.Add(new AddressRegisterOperand(regNum));
                }
                else
                {
                    op.Operands.Add(EffectiveAddressOp(inst, EAType.Destination));
                }

                sb.Append(op.Operands);
                return op;
            }

            protected Operation LINK(Instruction inst, StringBuilder sb)
            {
                Operation op = AppendMnemonic(inst, sb);
                sb.AppendTab(EA_COLUMN);

                if (inst.SourceExtWord1.HasValue)
                {
                    byte regNum = (byte)(inst.Opcode & 0x0007);
                    int disp = Helpers.SignExtendValue((uint)inst.SourceExtWord1, OpSize.Word);

                    op.Operands.Add(new AddressRegisterOperand(regNum));
                    op.Operands.Add(new ImmediateOperand(disp));

                    sb.Append(op.Operands);
                }
                else
                {
                    throw new NotSupportedException("Expecting SourceExtWord1");
                }

                return op;
            }

            protected Operation UNLK(Instruction inst, StringBuilder sb)
            {
                Operation op = AppendMnemonic(inst, sb);
                sb.AppendTab(EA_COLUMN);

                byte regNum = (byte)(inst.Opcode & 0x0007);
                op.Operands.Add(new AddressRegisterOperand(regNum));

                sb.Append(op.Operands);
                return op;
            }

            /// <summary>
            /// Branch Conditionally
            /// </summary>
            /// <param name="inst"></param>
            /// <param name="sb"></param>
            protected Operation Bcc(Instruction inst, StringBuilder sb)
            {
                sb.Append('B');
                Condition cond = (Condition)((inst.Opcode & 0x0F00) >> 8);
                if (cond == 0)
                {
                    // BT and BRA are actually the same opcode.
                    sb.Append("RA");
                }
                else
                {
                    AppendCondition(cond, sb);
                }

                Operation op = new(Machine.CurrentInstructionAddress, sb.ToString());
                uint pc = Machine.CPU.CurrentPC;
                int disp = inst.Opcode & 0x00FF;
                OpSize size = OpSize.Word;
                if (disp == 0)
                {
                    // 16-bit displacement, uses ExtWord1
                    if (inst.SourceExtWord1.HasValue)
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
                        throw new NotSupportedException("Expecting SourceExtWord1");
                    }
                }
                else
                {
                    disp = Helpers.SignExtendValue((uint)disp, OpSize.Byte);
                    size = OpSize.Byte;
                }

                op.Size = AppendSizeAndTab(size, sb);

                uint address = (uint)(pc + disp);

                op.Operands.Add(new LabelOperand(address));

                sb.Append(op.Operands);
                return op;
            }

            protected Operation JMP_JSR(Instruction inst, StringBuilder sb)
            {
                Operation op = AppendMnemonic(inst, sb);
                sb.AppendTab(EA_COLUMN);

                op.Operands.Add(EffectiveAddressOp(inst, EAType.Source));

                sb.Append(op.Operands);
                return op;
            }

            protected Operation BRA_BSR(Instruction inst, StringBuilder sb)
            {
                Operation op = AppendMnemonic(inst, sb);
                uint pc = Machine.CPU.CurrentPC;
                int disp = inst.Opcode & 0x00FF;
                OpSize size = OpSize.Word;
                if (disp == 0)
                {
                    // Byte displacement is zero so use the extension word value as a 16-bit displacement.
                    if (inst.SourceExtWord1.HasValue)
                    {
                        disp = Helpers.SignExtendValue(inst.SourceExtWord1.Value, OpSize.Word);

                        // Step PC back a word as it should be pointing immediately after the instruction opcode word
                        // for the displacement to be correct (whereas it will currently be pointing at the location immediately
                        // after the extension word)
                        pc -= 2;
                    }
                    else
                    {
                        throw new NotSupportedException("Expecting SourceExtWord1");
                    }
                }
                else
                {
                    disp = Helpers.SignExtendValue((uint)disp, OpSize.Byte);
                    size = OpSize.Byte;
                }

                op.Size = AppendSizeAndTab(size, sb);

                uint address = (uint)(pc + disp);
                op.Operands.Add(new LabelOperand(address));

                sb.Append(op.Operands);
                return op;
            }

            /// <summary>
            /// Test Condition, Decrement, and Branch.
            ///
            ///     If Condition False
            ///         Then (Dn - 1 -> Dn; If Dn != -1 Then PC + dn -> PC)
            ///
            /// Controls a loop of instructions. The parameters are a condition code, a data
            /// register(counter), and a displacement value.The instruction first tests the condition for
            /// termination; if it is true, no operation is performed.If the termination condition is not
            /// true, the low-order 16 bits of the counter data register decrement by one.If the result
            /// is – 1, execution continues with the next instruction.If the result is not equal to – 1,
            /// execution continues at the location indicated by the current value of the program
            /// counter plus the sign-extended 16-bit displacement. The value in the program counter
            /// is the address of the instruction word of the DBcc instruction plus two. The
            /// displacement is a twos complement integer that represents the relative distance in
            /// bytes from the current program counter to the destination program counter.Condition
            /// code cc specifies one of the following conditional tests (refer to Table 3-19 for more
            /// information on these conditional tests):
            ///
            ///     Mnemonic    Condition           Mnemonic    Condition
            ///     ========    =========           ========    =========
            ///     CC(HI)      Carry Clear         LS          Low or Same
            ///     CS(LO)      Carry Set           LT          Less Than
            ///     EQ          Equal               MI          Minus
            ///     F           False               NE          Not Equal
            ///     GE          Greater or Equal    PL          Plus
            ///     GT          Greater Than        T           True
            ///     HI          High                VC          Overflow Clear
            ///     LE          Less or Equal       VS          Overflow Set
            ///
            /// Condition Codes:
            ///     Not affected.
            ///
            /// NOTE:
            ///
            /// The terminating condition is similar to the UNTIL loop clauses of
            /// high-level languages.For example: DBMI can be stated as
            /// "decrement and branch until minus".
            ///
            /// Most assemblers accept DBRA for DBF for use when only a
            /// count terminates the loop (no condition is tested).
            ///
            /// A program can enter a loop at the beginning or by branching to
            /// the trailing DBcc instruction.Entering the loop at the beginning
            /// is useful for indexed addressing modes and dynamically
            /// specified bit operations.In this case, the control index count
            /// must be one less than the desired number of loop executions.
            /// However, when entering a loop by branching directly to the
            /// trailing DBcc instruction, the control count should equal the loop
            /// execution count.In this case, if a zero count occurs, the DBcc
            /// instruction does not branch, and the main loop is not executed.
            /// </summary>
            /// <param name="inst"></param>
            /// <param name="sb"></param>
            protected Operation DBcc(Instruction inst, StringBuilder sb)
            {
                sb.Append("DB");
                Condition cond = (Condition)((inst.Opcode & 0x0F00) >> 8);
                AppendCondition(cond, sb);

                Operation op = new(Machine.CurrentInstructionAddress, sb.ToString(), OpSize.Word);
                sb.Append(".W");
                sb.AppendTab(EA_COLUMN);

                int dRegNum = inst.Opcode & 0x0007;
                uint pc = Machine.CPU.CurrentPC;

                // Note: extra -2 to account for PC pointing at the next instruction, not on the extension word for the
                // current instruction (as the displacement for DBcc instructions assumes)
                if (inst.SourceExtWord1.HasValue)
                {
                    int disp = Helpers.SignExtendValue((uint)inst.SourceExtWord1, OpSize.Word) - 2;
                    uint address = (uint)(pc + disp);
                    op.Operands.Add(new DataRegisterOperand(dRegNum));
                    op.Operands.Add(new LabelOperand(address));

                    sb.Append(op.Operands);
                }
                else
                {
                    throw new NotSupportedException("Expecting SourceExtWord1");
                }

                return op;
            }

            /// <summary>
            /// Set According to Condition.
            /// Sets the byte to all ones if the condition is true, sets the
            /// byte to zero if false.
            /// </summary>
            /// <param name="inst"></param>
            /// <param name="sb"></param>
            protected Operation Scc(Instruction inst, StringBuilder sb)
            {
                sb.Append('S');
                Condition condition = (Condition)((inst.Opcode & 0x0F00) >> 8);
                AppendCondition(condition, sb);

                Operation op = new(Machine.CurrentInstructionAddress, sb.ToString(), OpSize.Byte);
                sb.Append(".B"); // Size is always byte
                sb.AppendTab(EA_COLUMN);

                op.Operands.Add(EffectiveAddressOp(inst, EAType.Destination));

                sb.Append(op.Operands);
                return op;
            }

            protected Operation ADDA_SUBA_CMPA(Instruction inst, StringBuilder sb)
            {
                Operation op = AppendMnemonic(inst, sb);
                AppendSizeAndTab(inst, sb);
                int regNum = (inst.Opcode & 0x0E00) >> 9;

                op.Operands.Add(EffectiveAddressOp(inst, EAType.Source));
                op.Operands.Add(new AddressRegisterOperand(regNum));

                sb.Append(op.Operands);
                return op;
            }

            protected Operation BTST_BCHG_BCLR_BSET(Instruction inst, StringBuilder sb)
            {
                Operation op = AppendMnemonic(inst, sb);

                // Determine if the destination is a memory effectiveAddress. If it is then we work with a single byte.
                bool isMemory = EffectiveAddressIsMemory(inst, EAType.Destination);
                bool isImmediate = false;
                if (!isMemory && ((inst.Opcode & 0b0000_0000_0011_1111) == 0b0000_0000_0011_1100))
                {
                    isImmediate = true;
                }
                uint? bitNum = null;
                int? regNum = null;
                if ((inst.Opcode & 0x0100) != 0)       // Determine if dynamic (i.e. bit number specified in a register)
                {
                    regNum = (inst.Opcode & 0x0E00) >> 9;
                }
                else
                {
                    if (inst.SourceExtWord1.HasValue)
                    {
                        bitNum = (uint)inst.SourceExtWord1;
                    }
                }

                if (isMemory || isImmediate)
                {
                    if (bitNum.HasValue)
                    {
                        bitNum &= 0x000000FF;
                    }
                    inst.Size = OpSize.Byte;
                }
                else
                {
                    if (bitNum.HasValue)
                    {
                        bitNum &= 0x0000001F;
                    }
                    inst.Size = OpSize.Long;
                }

                AppendSizeAndTab(inst, sb);
                if (bitNum.HasValue)
                {
                    op.Operands.Add(new ImmediateOperand((byte)bitNum, "{0}"));
                }
                else if (regNum.HasValue)
                {
                    op.Operands.Add(new DataRegisterOperand(regNum.Value));
                    // $"D{regNum}"
                }
                op.Operands.Add(EffectiveAddressOp(inst, EAType.Destination));

                sb.Append(op.Operands);
                return op;
            }

            protected Operation ASL_ASR_LSL_LSR_ROL_ROR_ROXL_ROXR(Instruction inst, StringBuilder sb)
            {
                Operation op = AppendMnemonic(inst, sb);
                op.Size = AppendSizeAndTab(inst, sb);

                byte sizeBits = (byte)((inst.Opcode & 0x00C0) >> 6);
                if (sizeBits == 0x03)
                {
                    // Only one operand, shift one bit
                    op.Operands.Add(EffectiveAddressOp(inst, EAType.Source));
                }
                else
                {
                    byte dRegNum = (byte)(inst.Opcode & 0x0007);

                    // Determine if a data register holds the shift amount.
                    bool dRegShift = (inst.Opcode & 0x0020) != 0;
                    int shift = (inst.Opcode & 0x0E00) >> 9;
                    if (dRegShift)
                    {
                        // The shift value holds the number of the data register that holds the number of bits to shift by.
                        op.Operands.Add(new DataRegisterOperand(shift));
                    }
                    else
                    {
                        int shiftAmt = shift != 0 ? shift : 8;
                        op.Operands.Add(new ImmediateOperand((sbyte)shiftAmt, "{0}"));
                    }
                    op.Operands.Add(new DataRegisterOperand(dRegNum));
                }

                sb.Append(op.Operands);
                return op;
            }

            protected Operation LEA(Instruction inst, StringBuilder sb)
            {
                Operation op = AppendMnemonic(inst, sb);
                sb.AppendTab(EA_COLUMN);
                int regNum = (inst.Opcode & 0x0E00) >> 9;

                op.Operands.Add(EffectiveAddressOp(inst, EAType.Source));
                op.Operands.Add(new AddressRegisterOperand(regNum));

                sb.Append(op.Operands);
                return op;
            }

            protected Operation EXT(Instruction inst, StringBuilder sb)
            {
                Operation op = AppendMnemonic(inst, sb);
                OpSize size = (inst.Opcode & 0x0040) == 0 ? OpSize.Word : OpSize.Long;
                op.Size = size;
                string sz = size switch
                {
                    OpSize.Word => ".W",
                    OpSize.Long => ".L",
                    _ => throw new NotSupportedException("Operation size not supported")
                };
                sb.Append(sz);
                sb.AppendTab(EA_COLUMN);

                byte regNum = (byte)(inst.Opcode & 0x0007);

                op.Operands.Add(new DataRegisterOperand(regNum));

                sb.Append(op.Operands);
                return op;
            }

            protected Operation SWAP(Instruction inst, StringBuilder sb)
            {
                Operation op = AppendMnemonic(inst, sb);
                sb.AppendTab(EA_COLUMN);

                byte regNum = (byte)(inst.Opcode & 0x0007);
                op.Operands.Add(new DataRegisterOperand(regNum));

                sb.Append(op.Operands);
                return op;
            }

            protected Operation MOVEUSP(Instruction inst, StringBuilder sb)
            {
                Operation op = new(Machine.CurrentInstructionAddress, "MOVE");
                sb.Append("MOVE");
                AppendTab(EA_COLUMN, sb);

                byte regNum = (byte)(inst.Opcode & 0x0007);
                if ((inst.Opcode & 0x0008) == 0)
                {
                    op.Operands.Add(new AddressRegisterOperand(regNum));
                    op.Operands.Add(new AddressRegisterOperand(USP));
                    // $"{AddressReg(regNum)},USP"
                }
                else
                {
                    op.Operands.Add(new AddressRegisterOperand(USP));
                    op.Operands.Add(new AddressRegisterOperand(regNum));
                    // $"USP,{AddressReg(regNum)}"
                }

                sb.Append(op.Operands);
                return op;
            }

            protected Operation ABCD_SBCD(Instruction inst, StringBuilder sb)
            {
                Operation op = AppendMnemonic(inst, sb);
                AppendTab(EA_COLUMN, sb);

                byte rSrc = (byte)(inst.Opcode & 0x0007);
                byte rDest = (byte)((inst.Opcode & 0x0E00) >> 9);

                if ((inst.Opcode & 0x0008) == 0)
                {
                    // Working with data registers
                    op.Operands.Add(new DataRegisterOperand(rSrc));
                    op.Operands.Add(new DataRegisterOperand(rDest));
                    // $"D{rSrc},D{rDest}"
                }
                else
                {
                    // Working with memory addresses.
                    op.Operands.Add(new AddressPreDecOperand(rSrc));
                    op.Operands.Add(new AddressPreDecOperand(rDest));
                    // $"-({AddressReg(rSrc)}),-({AddressReg(rDest)})"
                }

                sb.Append(op.Operands);
                return op;
            }

            protected Operation EXG(Instruction inst, StringBuilder sb)
            {
                Operation op = AppendMnemonic(inst, sb);
                op.Size = OpSize.Long;
                sb.Append(".L");
                sb.AppendTab(EA_COLUMN);

                // NOTE: x and y are the reverse of the convention used
                // in the NXP Programmer's Reference Manual.
                byte rX = (byte)(inst.Opcode & 0x0007);
                byte rY = (byte)((inst.Opcode & 0x0E00) >> 9);
                byte mode = (byte)((inst.Opcode & 0x00F8) >> 3);
                switch (mode)
                {
                    case 0x08:      // Data Register <-> Data Register
                        op.Operands.Add(new DataRegisterOperand(rY));
                        op.Operands.Add(new DataRegisterOperand(rX));
                        // $"D{rY},D{rX}"
                        break;
                    case 0x09:      // Address Register <-> Address Register
                        op.Operands.Add(new AddressRegisterOperand(rY));
                        op.Operands.Add(new AddressRegisterOperand(rX));
                        // $"{AddressReg(rY)},{AddressReg(rX)}"
                        break;
                    case 0x11:      // Data Register <-> Address Register
                        op.Operands.Add(new DataRegisterOperand(rY));
                        op.Operands.Add(new AddressRegisterOperand(rX));
                        // $"D{rY},{AddressReg(rX)}"
                        break;
                    default:
                        throw new NotSupportedException("Invalid operating mode for EXG instruction.");
                }

                sb.Append(op.Operands);
                return op;
            }

            protected Operation STOP(Instruction inst, StringBuilder sb)
            {
                Operation op = AppendMnemonic(inst, sb);
                sb.AppendTab(EA_COLUMN);

                var data = inst.SourceExtWord1;
                if (data.HasValue)
                {
                    op.Operands.Add(new ImmediateOperand(data.Value));

                    sb.Append(op.Operands);
                }
                return op;
            }

            protected Operation TRAP(Instruction inst, StringBuilder sb)
            {
                Operation op = AppendMnemonic(inst, sb);
                sb.AppendTab(EA_COLUMN);

                ushort vector = (ushort)(inst.Opcode & 0x000F);
                op.Operands.Add(new ImmediateOperand(vector));

                sb.Append(op.Operands);
                return op;
            }

            protected Operation CHK(Instruction inst, StringBuilder sb)
            {
                Operation op = AppendMnemonic(inst, sb);
                op.Size = AppendSizeAndTab(inst, sb);

                int regNum = (inst.Opcode & 0x0E00) >> 9;

                op.Operands.Add(EffectiveAddressOp(inst, EAType.Source));
                op.Operands.Add(new DataRegisterOperand(regNum));

                sb.Append(op.Operands);
                return op;
            }

            protected Operation ADDX(Instruction inst, StringBuilder sb)
            {
                Operation op = AppendMnemonic(inst, sb);
                op.Size = AppendSizeAndTab(inst, sb);

                byte rX = (byte)(inst.Opcode & 0x0007);
                byte rY = (byte)((inst.Opcode & 0x0E00) >> 9);
                bool usingDataReg = (inst.Opcode & 0x0008) == 0;
                if (usingDataReg)
                {
                    op.Operands.Add(new DataRegisterOperand(rX));
                    op.Operands.Add(new DataRegisterOperand(rY));
                    // $"D{rX},D{rY}"
                }
                else
                {
                    op.Operands.Add(new AddressPreDecOperand(rX));
                    op.Operands.Add(new AddressPreDecOperand(rY));
                    // $"-({AddressReg(rX)}),-({AddressReg(rY)})"
                }

                sb.Append(op.Operands);
                return op;
            }

            protected Operation NOOPERANDS(Instruction inst, StringBuilder sb)
            {
                Operation op = AppendMnemonic(inst, sb);

                return op;
            }

#pragma warning disable S2325 // Methods and properties that don't access instance data should be static
            protected Operation NONE(Instruction inst, StringBuilder sb)
#pragma warning restore S2325 // Methods and properties that don't access instance data should be static
            {
                throw new NotSupportedException("Operation unknown");
            }

            public virtual string? GetTrapName(ushort opcode)
            {
                return null;
            }

            protected virtual Operation LINEA(Instruction inst, StringBuilder sb)
            {
                Operation op = new(Machine.CurrentInstructionAddress, "LINEA");
                sb.Append($"LINEA");
                sb.AppendTab(EA_COLUMN);

                op.Operands.Add(new ImmediateOperand((ushort)(inst.Opcode & 0x0fff), "${0:x3}"));
                // $"${(ushort)(inst.Opcode & 0x0fff):x3}")

                sb.Append(op.Operands);
                return op;
            }

            protected virtual Operation LINEF(Instruction inst, StringBuilder sb)
            {
                Operation op = new(Machine.CurrentInstructionAddress, "LINEF");
                sb.Append($"LINEF");
                sb.AppendTab(EA_COLUMN);

                op.Operands.Add(new ImmediateOperand((ushort)(inst.Opcode & 0x0fff), "${0:x3}"));
                // $"${(ushort)(inst.Opcode & 0x0fff):x3}")

                sb.Append(op.Operands);
                return op;
            }
            //////////////////////////////////////////////////////////////////////////
            // Support for documentation
            //////////////////////////////////////////////////////////////////////////

            /// <summary>
            /// Operand addressing modes used as hints for disassembly.
            /// </summary>
            public enum Mode : byte
            {
                DataRegister = AddrMode.DataRegister,       // Dn

                AddressRegister = AddrMode.AddressRegister, // An
                Address = AddrMode.Address,                 // (An)
                AddressPostInc = AddrMode.AddressPostInc,   // (An)+
                AddressPreDec = AddrMode.AddressPreDec,     // -(An)

                AddressDisp = AddrMode.AddressDisp,         // (d16,An)

                AddressIndex = AddrMode.AddressIndex,       // (d8,An,Xn)

                AbsShort = AddrMode.AbsShort,               // (xxx).W
                AbsLong = AddrMode.AbsLong,                 // (xxx).L

                PCDisp = AddrMode.PCDisp,                   // (d16,PC)
                PCIndex = AddrMode.PCIndex,                 // (d8,PC,Xn)

                Immediate = AddrMode.Immediate,             // #<data>

                RegList = 0xf0,                             // MOVEM An,d0-d7/a0-a7
                Quick = 0xf1,                               // #<data>
                Label = 0xf2,                               // <label>

                Illegal,                                     // Illegal instruction mode
                RegisterDirect
            }

            /// <summary>
            /// MOVEM register list.
            /// </summary>
            public class RegisterList
            {
                public static RegisterList Make(ushort regMask, bool preDec = false) => new(regMask, preDec);
                public RegisterList(ushort regMask, bool preDec = false)
                {
                    this.preDec = preDec;
                    this.regMask = regMask;
                }

                private readonly ushort regMask;
                private readonly bool preDec;

                public bool PreDec => preDec;
                public ushort RegMask => regMask;

                public void AppendRegisterList(StringBuilder sb)
                {
                    int? startReg = null;
                    int? lastReg = null;
                    int range = 0;
                    uint[] bits = preDec ? _rbit : _bit;
                    int offset = preDec ? 16 : 0;
                    for (int n = 0; n < 16; n++)
                    {
                        int bit = n + offset;
                        if ((regMask & bits[bit]) == bits[bit])
                        {
                            if (!startReg.HasValue)
                            {
                                startReg = n;
                                if (range > 0)
                                {
                                    sb.Append('/');
                                }
                                range++;
                                sb.Append(_reg[n]);
                            }
                            lastReg = n;
                            if (n == 15 && (startReg.Value != lastReg.Value))
                            {
                                sb.Append('-');
                                sb.Append(_reg[n]);
                            }
                        }
                        else
                        {
                            // Skip this register, emit previous range if any
                            if (startReg.HasValue && lastReg.HasValue && lastReg.Value != startReg.Value)
                            {
                                sb.Append('-');
                                sb.Append(_reg[lastReg.Value]);
                            }
                            startReg = null;
                            lastReg = null;
                        }
                    }
                }

                public override string ToString()
                {
                    StringBuilder sb = new();
                    AppendRegisterList(sb);
                    return sb.ToString();
                }
            }

            /// <summary>
            /// Class the represents an operand's displacement value.
            /// </summary>
            public class Displacement : ImmediateData
            {
                public Displacement(uint value) : base(value) { }
                public Displacement(ushort value) : base(value) { }
                public Displacement(byte value) : base(value) { }
                public Displacement(int value) : base(value) { }
                public Displacement(short value) : base(value) { }
                public Displacement(sbyte value) : base(value) { }

                public override string ToString()
                {
                    string formattedVal = Value.ToString();
                    if (!Signed && Value > 0)
                    {
                        formattedVal = Size switch
                        {
                            OpSize.Byte => $"${Value:x2}",
                            OpSize.Word => $"${Value:x4}",
                            OpSize.Long => $"${Value:x8}",
                            _ => throw new InvalidOperationException("Invalid size")
                        };
                    }
                    return formattedVal;
                }
            }

            /// <summary>
            /// Class that represents a quick immediate value for one of the
            /// quick operations (e.g., MOVEQ, ADDQ, etc.).
            /// </summary>
            public class QuickData : ImmediateData
            {
                public QuickData(uint value) : base(value) { }
                public QuickData(ushort value) : base(value) { }
                public QuickData(byte value) : base(value) { }
                public QuickData(int value) : base(value) { }
                public QuickData(short value) : base(value) { }
                public QuickData(sbyte value) : base(value) { }

                public override string ToString()
                {
                    return Value.ToString();
                }
            }

            /// <summary>
            /// Contains a text expression or symbolic value that
            /// may be part of some operands.  The StartCol is based
            /// on the specific formatting of that operand, e.g.,
            /// "(MyValue).L" has a StartCol of 1, whereas
            /// "MyValue(A0,D1.W)" has a StartCol of 0.
            ///
            /// This can be  used as a hint to the UI when highlighting the
            /// "MyValue" part of the expression in order to provide,
            /// perhaps, the ability to modify the text for clearer
            /// documentation.  E.g., "4" might be the text, and the
            /// user may change this to "MaxLen-1", where "MaxLen" is
            /// defined in a EQU assembly line to be equal to 5.
            /// </summary>
            public class Expression
            {
                /// <summary>
                /// Create an instance.
                /// </summary>
                /// <param name="startCol"></param>
                /// <param name="text"></param>
                public Expression(Operand operand, int startCol, string text)
                {
                    Operand = operand;
                    StartCol = startCol;
                    Text = text;
                }

                /// <summary>
                /// Operand containing this expression.
                /// </summary>
                public Operand Operand { get; set; }

                /// <summary>
                /// Starting column (0-based) from the beginning of the
                /// operand text.
                /// </summary>
                public int StartCol { get; set; }

                /// <summary>
                /// Expression or symbol, e.g., "MyStart+1", "BufferSize",
                /// etc.
                /// </summary>
                public string Text { get; set; }
            }

            /// <summary>
            /// AddressRegisterOperand class.
            /// </summary>
            public class AddressRegisterOperand : Operand
            {
                public AddressRegisterOperand(AddressRegister addressRegister, string? format = null) : base(format)
                {
                    AddressRegister = addressRegister;
                    AddressMode = AddrMode.AddressRegister;
                }
                public AddressRegisterOperand(int addressRegNum) : this(AddressRegisters[addressRegNum]) { }

                public AddressRegister AddressRegister { get; set; }

                /// <summary>
                /// Format the operand disassembly display and for the assembler.
                /// </summary>
                /// <returns>Operand string suitable for an assembler.</returns>
                public override string? ToString()
                {
                    return $"{AddressRegister}";
                }
            }

            /// <summary>
            /// AddressOperand class.
            /// </summary>
            public class AddressOperand : Operand
            {
                public AddressOperand(AddressRegister addressRegister)
                {
                    AddressRegister = addressRegister;
                    AddressMode = AddrMode.Address;
                }
                public AddressOperand(int addressRegNum) : this(AddressRegisters[addressRegNum]) { }

                public AddressRegister AddressRegister { get; set; }

                /// <summary>
                /// Format the operand disassembly display and for the assembler.
                /// </summary>
                /// <returns>Operand string suitable for an assembler.</returns>
                public override string? ToString()
                {
                    return $"({AddressRegister})";
                }
            }

            /// <summary>
            /// AddressPostIncOperand class.
            /// </summary>
            public class AddressPostIncOperand : Operand
            {
                public AddressPostIncOperand(AddressRegister addressRegister) : base()
                {
                    AddressRegister = addressRegister;
                    AddressMode = AddrMode.AddressPostInc;
                }
                public AddressPostIncOperand(int addressRegNum) : this(AddressRegisters[addressRegNum]) { }

                public AddressRegister AddressRegister { get; set; }

                /// <summary>
                /// Format the operand disassembly display and for the assembler.
                /// </summary>
                /// <returns>Operand string suitable for an assembler.</returns>
                public override string? ToString()
                {
                    return $"({AddressRegister})+";
                }
            }

            /// <summary>
            /// AddressPreDecOperand class.
            /// </summary>
            public class AddressPreDecOperand : Operand
            {
                public AddressPreDecOperand(AddressRegister addressRegister)
                {
                    AddressRegister = addressRegister;
                    AddressMode = AddrMode.AddressPreDec;
                }

                public AddressPreDecOperand(int addressRegNum) : this(AddressRegisters[addressRegNum]) { }

                public AddressRegister AddressRegister { get; set; }

                /// <summary>
                /// Format the operand disassembly display and for the assembler.
                /// </summary>
                /// <returns>Operand string suitable for an assembler.</returns>
                public override string? ToString()
                {
                    return $"-({AddressRegister})";
                }
            }

            /// <summary>
            /// AddressDispOperand class.
            /// </summary>
            public class AddressDispOperand : Operand
            {
                public AddressDispOperand(AddressRegister addressRegister, Displacement displacement, string? format = null) : base(format)
                {
                    AddressRegister = addressRegister;
                    AddressMode = AddrMode.AddressDisp;
                    Displacement = displacement;
                }

                public AddressDispOperand(int addressRegNum, short disp, string? format = null) : this(AddressRegisters[addressRegNum], new Displacement(disp), format) { }

                public AddressRegister AddressRegister { get; set; }
                public Displacement Displacement { get; set; }

                /// <summary>
                /// Format the operand disassembly display and for the assembler.
                /// </summary>
                /// <returns>Operand string suitable for an assembler.</returns>
                public override string? ToString()
                {
                    string? disp = CurrentDisassembler?.GetExpression(Op.Address, Pos);
                    if (disp == null)
                    {
                        if (Format != null)
                        {
                            disp = string.Format(Format, Displacement.Value);
                        }
                        else if (Displacement.Value < 0)
                        {
                            disp = $"{Displacement.Value}";
                        }
                        else
                        {
                            disp = $"${Displacement.Value:x4}";
                        }
                    }

                    Expression = new Expression(this, 0, disp);
                    return $"{disp}({AddressRegister})";
                }
            }

            /// <summary>
            /// AddressIndexOperand class.
            /// </summary>
            public class AddressIndexOperand : Operand
            {
                public AddressIndexOperand(AddressRegister addressRegister, int indexRegnum, bool indexIsAddressRegister, OpSize indexSize, Displacement displacement, string? format = null) : base(format)
                {
                    AddressRegister = addressRegister;
                    AddressMode = AddrMode.AddressIndex;
                    IndexRegister = new IndexRegister(indexRegnum, indexIsAddressRegister);
                    Displacement = displacement;
                    IndexSize = indexSize;
                }

                public AddressIndexOperand(AddressRegister addressRegister, int indexRegNum, bool indexIsAddressRegister, OpSize indexSize, sbyte disp, string? format = null) : this(addressRegister, indexRegNum, indexIsAddressRegister, indexSize, new Displacement(disp), format) { }

                public AddressRegister AddressRegister { get; set; }
                public IndexRegister IndexRegister { get; set; }
                public Displacement Displacement { get; set; }
                public OpSize? IndexSize { get; set; }

                /// <summary>
                /// Format the operand disassembly display and for the assembler.
                /// </summary>
                /// <returns>Operand string suitable for an assembler.</returns>
                public override string? ToString()
                {
                    char sz = IndexSize == OpSize.Long ? 'L' : 'W';
                    string? disp = CurrentDisassembler?.GetExpression(Op.Address, Pos);
                    int column = 0;
                    if (disp == null)
                    {
                        if (Format != null)
                        {
                            disp = string.Format(Format, Displacement.Value);
                        }
                        else if (Displacement.Value <= 0)
                        {
                            disp = $"{Displacement.Value}";

                        }
                        else
                        {
                            disp = $"${Displacement.Value:x2}";
                        }
                    }

                    Expression = new Expression(this, column, disp);
                    return $"{disp}({AddressRegister},{IndexRegister}.{sz})";

                }
            }

            public class AbsShortOperand : Operand
            {
                public AbsShortOperand(Displacement displacement, string? format = null) : base(format)
                {
                    Displacement = displacement;
                }

                public AbsShortOperand(ushort value, string? format = null) : this(new Displacement(value), format) { }

                public AbsShortOperand(short value, string? format = null) : this(new Displacement(value), format) { }

                public Displacement Displacement { get; set; }

                public override string? ToString()
                {
                    string? disp = CurrentDisassembler?.GetExpression(Op.Address, Pos);
                    if (disp == null)
                    {
                        if (Format != null)
                        {
                            disp = string.Format(Format, Displacement.Value);
                        }
                        else
                        {
                            disp = $"{Displacement}";
                        }
                    }

                    Expression = new Expression(this, 1, disp);
                    return $"({disp}).W";
                }
            }

            public class AbsLongOperand : Operand
            {
                public AbsLongOperand(Displacement displacement, string? format = null) : base(format)
                {
                    Displacement = displacement;
                }
                public AbsLongOperand(uint value, string? format = null) : this(new Displacement(value), format) { }
                public AbsLongOperand(int value, string? format = null) : this(new Displacement(value), format) { }

                public Displacement Displacement { get; set; }

                public override string? ToString()
                {
                    string? disp = CurrentDisassembler?.GetExpression(Op.Address, Pos);
                    if (disp == null)
                    {
                        if (Format != null)
                        {
                            disp = string.Format(Format, Displacement.Value);
                        }
                        else
                        {
                            disp = $"{Displacement}";
                        }
                    }

                    Expression = new Expression(this, 1, disp);
                    return $"({disp}).L";
                }
            }

            public class DataRegisterOperand : Operand
            {
                public DataRegisterOperand(DataRegister dataRegister, OpSize? size = null)
                {
                    DataRegister = dataRegister;
                    AddressMode = AddrMode.DataRegister;
                    Size = size;
                }

                public DataRegisterOperand(int regNum, OpSize? size = null) : this(DataRegisters[regNum], size) { }

                public DataRegister DataRegister { get; set; }

                /// <summary>
                /// Format the operand disassembly display and for the assembler.
                /// </summary>
                /// <returns>Operand string suitable for an assembler.</returns>
                public override string? ToString()
                {
                    return $"{DataRegister}";
                }
            }

            public class ImmediateOperand : Operand
            {
                public ImmediateOperand(ImmediateData data, string? format = null) : base(format)
                {
                    Data = data;
                    AddressMode = AddrMode.Immediate;
                    Size = data.Size;
                }
                public ImmediateOperand(byte value, string? format = null) : this(new ImmediateData(value), format) { }
                public ImmediateOperand(sbyte value, string? format = null) : this(new ImmediateData(value), format) { }
                public ImmediateOperand(ushort value, string? format = null) : this(new ImmediateData(value), format) { }
                public ImmediateOperand(short value, string? format = null) : this(new ImmediateData(value), format) { }
                public ImmediateOperand(uint value, string? format = null) : this(new ImmediateData(value), format) { }
                public ImmediateOperand(int value, string? format = null) : this(new ImmediateData(value), format) { }

                public ImmediateData Data { get; set; }

                /// <summary>
                /// Format the operand disassembly display and for the assembler.
                /// </summary>
                /// <returns>Operand string suitable for an assembler.</returns>
                public override string? ToString()
                {
                    string? opStr;
                    string? disp = CurrentDisassembler?.GetExpression(Op.Address, Pos);
                    if (disp == null)
                    {
                        if (Format != null)
                        {
                            disp = string.Format(Format, Data.Value);
                        }
                        else
                        {
                            disp = Data.ToString();
                        }
                    }

                    if (Op.Name != "LINEA" && Op.Name != "LINEF" && Op.Name != "DC")
                    {
                        Expression = new Expression(this, 1, disp!);
                        opStr = $"#{disp}";
                    }
                    else
                    {
                        Expression = new Expression(this, 0, disp!);
                        opStr = disp;
                    }

                    return opStr;
                }
            }

            public class RegListOperand : Operand
            {
                public RegListOperand(RegisterList registerList)
                {
                    RegisterList = registerList;
                }

                public RegListOperand(ushort regMask, bool preDec) : this(new RegisterList(regMask, preDec)) { }

                public RegisterList RegisterList { get; set; }

                /// <summary>
                /// Format the operand disassembly display and for the assembler.
                /// </summary>
                /// <returns>Operand string suitable for an assembler.</returns>
                public override string? ToString()
                {
                    return RegisterList.ToString();
                }
            }

            public class LabelOperand : Operand
            {
                public LabelOperand(Label label, AddrMode? addressMode = null, string? format = null) : base(format)
                {
                    Label = label;
                    AddressMode = addressMode;
                }

                public LabelOperand(uint address, AddrMode? addressMode = null, string? format = null) : this(new Label(address), addressMode, format) { }

                public Label Label { get; set; }

                /// <summary>
                /// Format the operand disassembly display and for the assembler.
                /// </summary>
                /// <returns>Operand string suitable for an assembler.</returns>
                public override string? ToString()
                {
                    string? disp = CurrentDisassembler?.GetExpression(Op.Address, Pos) ?? CurrentDisassembler?.GetLabelName(Label.Address, Op.Address);
                    if (disp == null && Format != null)
                    {
                        disp = string.Format(Format, Label.Address);
                    }
                    else disp ??= $"{Label}";

                    Expression = new Expression(this, 0, disp);
                    if (AddressMode == AddrMode.PCDisp)
                    {
                        disp = $"{disp}(PC)";
                        Expression.StartCol = 0;
                    }
                    else if (Size == OpSize.Long || AddressMode == AddrMode.AbsLong)
                    {
                        disp = $"({disp}).L";
                        Expression.StartCol = 1;
                    }
                    else if (Size == OpSize.Word || AddressMode == AddrMode.AbsShort)
                    {
                        disp = $"({disp}).W";
                        Expression.StartCol = 1;
                    }
                    return disp;
                }
            }

            /// <summary>
            /// Not used - use LabelOperand instead to allow symbolic references to be used
            /// by subclasses.
            /// </summary>
            public class PCDispOperand : Operand
            {
                public PCDispOperand(Displacement displacement, string? format = null) : base(format)
                {
                    Displacement = displacement;
                }

                public PCDispOperand(uint address, string? format = null) : this(new Displacement(address), format) { }

                public Displacement Displacement { get; set; }

                /// <summary>
                /// Format the operand disassembly display and for the assembler.
                /// </summary>
                /// <returns>Operand string suitable for an assembler.</returns>
                public override string? ToString()
                {
                    string? disp = CurrentDisassembler?.GetExpression(Op.Address, Pos);
                    if (disp == null)
                    {
                        if (Format != null)
                        {
                            disp = string.Format(Format, Displacement.Value);
                        }
                        else
                        {
                            disp = $"{Displacement}(PC)";
                        }
                    }

                    Expression = new Expression(this, 0, disp);
                    return disp;
                }
            }

            public class PCIndexOperand : Operand
            {
                public PCIndexOperand(int regNum, bool indexIsAddressRegister, Displacement displacement, OpSize size, string? format = null) : base(format)
                {
                    AddressMode = AddrMode.PCIndex;
                    IndexRegister = new IndexRegister(regNum, indexIsAddressRegister);
                    IndexIsAddressRegister = indexIsAddressRegister;
                    Displacement = displacement;
                    Size = size;
                }

                public PCIndexOperand(int indexRegNum, bool indexIsAddressRegister, uint address, OpSize size, string? format = null) : this(indexRegNum, indexIsAddressRegister, new Displacement(address), size, format) { }

                public IndexRegister IndexRegister { get; set; }
                public bool IndexIsAddressRegister { get; set; }
                public Displacement Displacement { get; set; }

                /// <summary>
                /// Format the operand disassembly display and for the assembler.
                /// </summary>
                /// <returns>Operand string suitable for an assembler.</returns>
                public override string? ToString()
                {
                    string? disp = CurrentDisassembler?.GetExpression(Op.Address, Pos);
                    if (disp == null)
                    {
                        if (Format != null)
                        {
                            disp = string.Format(Format, Displacement.Value);
                        }
                        else if (Displacement.Value <= 100)
                        {
                            disp = $"{Displacement.Value}";
                        }
                        else
                        {
                            disp = $"${Displacement.Value:x2}";
                        }
                    }
                    OpSize? size = Size;
                    string opStr;
                    if (size == OpSize.Long)
                    {
                        opStr = $"{disp}(PC,{IndexRegister}.L)";
                    }
                    else
                    {
                        opStr = $"{disp}(PC,{IndexRegister}.W)";
                    }

                    Expression = new Expression(this, 0, disp);
                    return opStr;
                }
            }

            public class QuickDataOperand : Operand
            {
                public QuickDataOperand(QuickData quickData, string? format = null)
                {
                    QuickData = quickData;
                }

                public QuickDataOperand(sbyte value, string? format = null) : this(new QuickData(value), format) { }
                public QuickDataOperand(byte value, string? format = null) : this(new QuickData(value), format) { }
                public QuickDataOperand(short value, string? format = null) : this(new QuickData(value), format) { }
                public QuickDataOperand(ushort value, string? format = null) : this(new QuickData(value), format) { }
                public QuickDataOperand(int value, string? format = null) : this(new QuickData(value), format) { }
                public QuickDataOperand(uint value, string? format = null) : this(new QuickData(value), format) { }

                public QuickData QuickData { get; set; }

                /// <summary>
                /// Format the operand disassembly display and for the assembler.
                /// </summary>
                /// <returns>Operand string suitable for an assembler.</returns>
                public override string? ToString()
                {
                    string? disp = CurrentDisassembler?.GetExpression(Op.Address, Pos);
                    if (disp == null)
                    {
                        if (Format != null)
                        {
                            disp = string.Format(Format, QuickData.Value);
                        }
                        else
                        {
                            disp = $"{QuickData}";
                        }
                    }
                    if (disp.StartsWith('#'))
                    {
                        disp = disp[1..];
                    }

                    Expression = new Expression(this, 1, disp);
                    return $"#{disp}";
                }
            }

            public class CCROperand : Operand
            {
                public CCROperand()
                {
                }

                public override string ToString()
                {
                    return "CCR";
                }
            }

            public class SROperand : Operand
            {
                public SROperand()
                {
                }

                public override string ToString()
                {
                    return "SR";
                }

            }

            /// <summary>
            /// Represents an operand for either a Directive or an Operation.
            ///
            /// For an Operation, it can be either Source (Pos = 0) or
            /// Destination (Pos = 1).
            ///
            /// For a Directive, the Pos represents which Operand it is
            /// in the list of operands starting at 0.
            /// </summary>
            public class Operand
            {
                public Operand(string? format = null)
                {
                    Format = format;
                    _op = dummyOp;
                }

                /// <summary>
                /// Dummy operand to prevent compiler warnings when the <see cref="Op"/>
                /// property is not initialized in the constructor.  The <see cref="Op"/>
                /// property is set by the <see cref="OperandList.Add(Operand op)"/> method.
                /// </summary>
                private static readonly Operation dummyOp = new(0, "DUMMY OP TO PREVENT COMPILER WARNINGS");

                protected DirectiveOrOperation _op;

                /// <summary>
                /// The directive or operation that this operand is associated with.
                /// Set when the Operand is added to the OperandList in the Operation
                /// object.
                /// </summary>
                public DirectiveOrOperation Op
                {
                    get { return _op; }
                    set { _op = value; _text = null; }
                }

                public bool IsMemory { get; set; } = false;

                public AddrMode? AddressMode { get; set; } = null;

                OpSize? _size;
                public OpSize? Size
                {
                    get { return _size; }
                    set { _size = value; _text = null; }
                }

                protected string? _format;
                public string? Format
                {
                    get { return _format; }
                    set { _format = value; _text = null; }
                }

                public int Pos { get; set; } = 0;

                protected string? _text;
                public virtual string Text
                {
                    get
                    {
                        _text ??= ToString();
                        return _text ?? "ERROR";
                    }
                    protected set
                    {
                        _text = value;
                    }
                }

                protected Expression? _expression;
                /// <summary>
                /// Optional expression that can represent an immediate
                /// value or displacement for this operand.  May be defined by
                /// an EQU for example.
                /// </summary>
                public Expression? Expression
                {
                    get { return _expression; }
                    set { _expression = value; _text = null; }
                }
            }

            /// <summary>
            /// Represents immediate data in an operand.  Handles
            /// all sizes and signed and unsigned.
            /// </summary>
            public class ImmediateData
            {
                public ImmediateData(uint value)
                {
                    Value = (int)value;
                    Size = OpSize.Long;
                    Signed = false;
                }

                public ImmediateData(ushort value)
                {
                    Value = value;
                    Size = OpSize.Word;
                    Signed = false;
                }

                public ImmediateData(byte value)
                {
                    Value = value;
                    Size = OpSize.Byte;
                    Signed = false;
                }

                public ImmediateData(int value)
                {
                    Value = value;
                    Size = OpSize.Long;
                    Signed = true;
                }

                public ImmediateData(short value)
                {
                    Value = value;
                    Size = OpSize.Word;
                    Signed = true;
                }

                public ImmediateData(sbyte value)
                {
                    Value = value;
                    Size = OpSize.Byte;
                    Signed = true;
                }

                public OpSize Size { get; private set; }
                public bool Signed { get; private set; }
                public int Value { get; private set; }

                public override string? ToString()
                {
                    string formattedVal = Value.ToString();
                    if (!Signed || Value > 0)
                    {
                        formattedVal = Size switch
                        {
                            OpSize.Byte => $"${Value:x2}",
                            OpSize.Word => $"${Value:x4}",
                            OpSize.Long => $"${Value:x8}",
                            _ => throw new InvalidOperationException("Invalid size")
                        };
                    }
                    return formattedVal;
                }
            }

            /// <summary>
            /// Label address.  Hint to disassembler that a subclass may
            /// want to replace the label address in an operand with a symbol.
            /// </summary>
            public class Label
            {
                /// <summary>
                /// Create an instance.
                /// </summary>
                /// <param name="address"></param>
                public Label(uint address)
                {
                    Address = address;
                }

                /// <summary>
                /// 32-bit address.
                /// </summary>
                public uint Address { get; private set; }

                public override string? ToString()
                {
                    return $"${Address:x8}";
                }
            }


            //////////////////////////////////////////////////////////////////////////
            // Register names.
            //
            // Registers are given their own classes simply to hold the name of
            // the register and allow easy initialization of the operand so that
            // the correct mode (e.g., Mode.AddressIndex) is set in the operand
            // by the constructor without having to pass another parameter into
            // the constructor.
            //////////////////////////////////////////////////////////////////////////

            /// <summary>
            /// Base class for control registers (SR, CCR, PC).
            /// </summary>
            public class ControlRegister
            {
                internal ControlRegister(string name)
                {
                    Name = name;
                }

                /// <summary>
                /// Name of the register - i.e., "SR", "CCR", or "PC".
                /// </summary>
                public string Name { get; private set; }

                public override string ToString()
                {
                    return Name;
                }
            }

            /// <summary>
            /// PC name.
            /// </summary>
            public class ProgramCounter : ControlRegister
            {
                internal ProgramCounter() : base("PC") { }
            }

            /// <summary>
            /// SR name.
            /// </summary>
            public class StatusRegister : ControlRegister
            {
                /// <summary>
                /// Create an instance.
                /// </summary>
                internal StatusRegister() : base("SR") { }
            }

            /// <summary>
            /// CCR name.
            /// </summary>
            public class ConditionCodeRegister : ControlRegister
            {
                /// <summary>
                /// Create an instance.
                /// </summary>
                public ConditionCodeRegister() : base("CCR") { }
            }

            public class IndexRegister
            {
                /// <summary>
                /// Create an instance.
                /// </summary>
                /// <param name="name"></param>
                public IndexRegister(int regNum, bool isAddressRegister)
                {
                    Name = isAddressRegister ?
                        $"A{regNum}" :
                        $"D{regNum}";
                }

                public IndexRegister(string name)
                {
                    Name = name;
                }

                /// <summary>
                /// Register name, e.g. "D2", "A4".
                /// </summary>
                public string Name { get; set; }

                public override string ToString()
                {
                    return Name;
                }

            }

            /// <summary>
            /// Data register name.
            /// </summary>
            public class DataRegister : IndexRegister
            {
                /// <summary>
                /// Create an instance.
                /// </summary>
                /// <param name="name"></param>
                public DataRegister(string name) : base(name)
                {
                    Name = name;
                }
            }

            /// <summary>
            /// Address register name.
            /// </summary>
            public class AddressRegister : IndexRegister
            {
                public AddressRegister(string name) : base(name)
                {
                    Name = name;
                }
            }

            /// <summary>
            /// CCR name.
            /// </summary>
            public static readonly ConditionCodeRegister CCR = new();

            /// <summary>
            /// SR name.
            /// </summary>
            public static readonly StatusRegister SR = new();

            /// <summary>
            /// PC name.
            /// </summary>
            public static readonly ProgramCounter PC = new();

            /// <summary>
            /// Address register names.
            /// </summary>
            public static readonly AddressRegister[] AddressRegisters =
            [
                new AddressRegister("A0"),
                new AddressRegister("A1"),
                new AddressRegister("A2"),
                new AddressRegister("A3"),
                new AddressRegister("A4"),
                new AddressRegister("A5"),
                new AddressRegister("A6"),
                new AddressRegister("SP"),
                new AddressRegister("USP")
            ];

            /// <summary>
            /// USP register name (alias for for the MOVEtoUSP and
            /// MOVEfromUSP instructions.
            /// </summary>
            public AddressRegister USP => AddressRegisters[8];

            /// <summary>
            /// Data register names.
            /// </summary>
            public static readonly DataRegister[] DataRegisters =
            [
                new DataRegister("D0"),
                new DataRegister("D1"),
                new DataRegister("D2"),
                new DataRegister("D3"),
                new DataRegister("D4"),
                new DataRegister("D5"),
                new DataRegister("D6"),
                new DataRegister("D7")
            ];

            /// <summary>
            /// Base class for directives (e.g., "DC.L") and operations (e.g., "MOVE", "JMP").
            /// </summary>
            public class DirectiveOrOperation
            {
                /// <summary>
                /// Create an instance.
                /// </summary>
                /// <param name="address"></param>
                /// <param name="name"></param>
                /// <param name="size"></param>
                public DirectiveOrOperation(uint address, string name, OpSize? size = null)
                {
                    Address = address;
                    Name = name;
                    Operands = new OperandList(this);
                    Size = size;
                    MachineCode = [];
                    Assembly = "";
                    PostOperandAnnotation = "";
                }

                /// <summary>
                /// 32-bit address.
                /// </summary>
                public uint Address { get; private set; }

                /// <summary>
                /// Directive or Operation name, e.g., "DC", "MOVE", "EXG", etc.
                /// </summary>
                public string Name { get; set; }

                /// <summary>
                /// Size (<see cref="OpSize"/>) of the operation if not default (usually OpSize.Word).
                /// </summary>
                public OpSize? Size { get; set; }

                /// <summary>
                /// Memory bytes for this operation or directive.
                /// </summary>
                public byte[] MachineCode { get; set; }

                /// <summary>
                /// Assembler text for this operation or directive including operands but
                /// not including comments.
                /// </summary>
                public string Assembly { get; set; }

                /// <summary>
                /// Optional annotation used for displaying ASCII data for
                /// DC directives.
                /// </summary>
                public string PostOperandAnnotation { get; set; }

                /// <summary>
                /// Operands for this directives or operation.  Typically 0-2 operands
                /// (no operands, src/dst only, or src,dst).  For directives like
                /// "DC.B" there may be a long list of operands.
                /// </summary>
                public OperandList Operands { get; set; }

                /// <summary>
                /// Get the Expression at the specified column,
                /// starting from 0 as the first column of the
                /// operation mnemonic.
                ///
                /// Return null if the position is out of range
                /// or there is no expression under that column.
                ///
                public Expression? GetExpressionAtColumn(int assemblyStartColumn, int column)
                {
                    StringBuilder sb = new();
                    sb.Append(Name);
                    sb.AppendSizeAndTab(Size);
                    int start = sb.Length + assemblyStartColumn;

                    foreach (Operand op in Operands.Where(op => op.Expression != null))
                    {
                        int opStart = start + op.Expression!.StartCol;
                        if (opStart >= column && column <= opStart + op.Expression.Text.Length)
                        {
                            return op.Expression;
                        }
                        start = opStart + op.Expression.Text.Length + 1; // comma after
                    }
                    return null;
                }
            }

            /// <summary>
            /// List of operands.  Can be zero, one or two if used with Operations,
            /// or can be more if used with Directives.
            /// </summary>
            public class OperandList : List<Operand>
            {
                /// <summary>
                /// Create an instance of the class.
                /// </summary>
                /// <param name="op"></param>
                public OperandList(DirectiveOrOperation op)
                {
                    Op = op;
                }

                /// <summary>
                /// Parent Operation or Directive.
                /// </summary>
                public DirectiveOrOperation Op { get; private set; }

                /// <summary>
                /// Append an Operand to the list of operands, setting
                /// the parent Directive or Operation and the operand
                /// position (0-based).
                /// </summary>
                /// <param name="operand"></param>
                /// <returns></returns>
                public new Operand Add(Operand operand)
                {
                    operand.Op = Op;
                    operand.Pos = Count;
                    base.Add(operand);
                    return operand;
                }

                /// <summary>
                /// Return a comma-separated list of operands.
                /// </summary>
                /// <returns></returns>
                public override string? ToString()
                {
                    if (Count == 1)
                    {
                        return this[0].ToString();
                    }
                    bool firstPass = true;
                    StringBuilder sb = new();
                    for (int i = 0; i < Count; i++)
                    {
                        if (!firstPass)
                        {
                            sb.Append(',');
                        }
                        else
                        {
                            firstPass = false;
                        }
                        sb.Append(this[i]);
                    }
                    return sb.ToString();
                }
            }

            /// <summary>
            /// Directive (e.g., "DC").
            /// </summary>
            public class Directive : DirectiveOrOperation
            {
                public Directive(uint address, string name, OpSize? size) : base(address, name, size) { }
            }

            /// <summary>
            /// Operation (e.g., "MOVE").
            /// </summary>
            public class Operation : DirectiveOrOperation
            {
                public Operation(uint address, string name, OpSize? size = null) : base(address, name, size) { }
            }
        }
    }

    /// <summary>
    /// Convenience extensions.
    /// </summary>
    public static partial class Extensions
    {
        /// <summary>
        /// Append spaces up to the tab stop.  Guaranteed at least
        /// one space.
        /// </summary>
        /// <param name="sb"></param>
        /// <param name="tabStop"></param>
        public static void AppendTab(this StringBuilder sb, int tabStop)
        {
            do
            {
                sb.Append(' ');
            } while (sb.Length < tabStop);
        }

        /// <summary>
        /// Append spaces up to the tab stop.  Guaranteed at least
        /// one space.
        /// </summary>
        /// <param name="sb"></param>
        /// <param name="tabStop"></param>
        public static void AppendSizeAndTab(this StringBuilder sb, OpSize? size)
        {
            Machine.Disassembler.AppendSizeAndTab(size, sb);
        }
    }

    /// <summary>
    /// Stack utility.  Top of stack is at the end of the list.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class Stack<T>
    {
        private readonly List<T> elements = [];

        public void Push(T item)
        {
            elements.Add(item);
            System.Diagnostics.Debug.WriteLine($"Pushing {item}, stack now has {elements.Count} elements.");
        }

        public T? Pop()
        {
            if (elements.Count == 0)
            {
                System.Diagnostics.Debug.WriteLine($"Popping, stack was already empty.");
                return default;
            }

            T item = elements[elements.Count - 1];
            elements.RemoveAt(elements.Count - 1);
            System.Diagnostics.Debug.WriteLine($"Popping {item}, stack now has {elements.Count} elements.");
            return item;
        }

        public T? Peek()
        {
            if (elements.Count == 0)
            {
                return default;
            }

            return elements[^1];
        }
    }
}
