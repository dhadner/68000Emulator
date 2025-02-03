using PendleCodeMonkey.MC68000EmulatorLib.Enumerations;
using System;
using System.Collections.Generic;
using System.Text;
using static PendleCodeMonkey.MC68000EmulatorLib.Machine.Disassembler;

namespace PendleCodeMonkey.MC68000EmulatorLib
{
    /// <summary>
    /// Partial implementation of the <see cref="Machine"/> class.
    /// </summary>
    public partial class Machine
    {
        /// <summary>
        /// Implementation of the <see cref="Disassembler"/> class.  It
        /// disassembles instructions and displays memory but never
        /// alters the state of the actual machine unless reading
        /// a location or I/O address would change the state
        /// of the (simulated) hardware.
        /// </summary>
        public class Disassembler
        {

            /// <summary>
            /// Record returned when disassembling a single instruction at an address.
            /// Comment can be provided by subclasses overriding the <see cref="Comment"/>
            /// method.
            /// </summary>
            public record DisassemblyRecord
            {
                /// <summary>
                /// Create in instance of the <see cref="DisassemblyRecord"/> class.
                /// </summary>
                /// <param name="endOfData">Set to <c>true</c> if the disassembler
                /// ran out of bytes prior to completing disassembly of this instrluction.</param>
                /// <param name="address">Address of this instruction</param>
                /// <param name="machineCode">Actual instruction bytes</param>
                /// <param name="assembly">Instruction, e.g., "MOVEQ.L #1,D0".  This text
                /// is suitable for round-tripping through the VASM assembler.  Uses
                /// only spaces, no tabs.</param>
                /// <param name="comment"></param>
                public DisassemblyRecord(bool endOfData, DirectiveOrOperation op)
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
                /// Address of this instruction
                /// </summary>
                public uint Address => Op.Address;

                /// <summary>
                /// Actual instruction bytes
                /// </summary>
                public byte[] MachineCode => Op.MachineCode;

                /// <summary>
                /// Instruction or data, e.g., "MOVEQ.L #1,D0", "DC.B  $03".  This text
                /// is suitable for round-tripping through the VASM assembler.  Uses
                /// only spaces, no tabs, and the mnemonic starts at string position 0.
                /// 
                /// This means that to send to VASM, at least one space must be prepended
                /// to prevent the assembler treating the mnemonic as a label.
                /// </summary>
                public string AssemblyLine => Op.Assembly;

                /// <summary>
                /// True if this is part of a Non-Executable Section.
                /// </summary>
                public bool IsNES => Op is Directive;
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
                    Debugger = machine.Debugger;
                }

                /// <summary>
                /// End of data not reached until end of address space.
                /// </summary>
                protected override bool IsEndOfData => CPU.PC >= 0xffffffff;

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
            public const int EAColumn = 8;

            /// <summary>
            /// Maximum instruction length in bytes.
            /// </summary>
            public const int MaxInstructionLength = 10;

            /// <summary>
            /// Gets or sets the <see cref="Machine"/> instance for which this <see cref="Disassembler"/> instance
            /// is handling the disassembly of instructions.
            /// </summary>
            protected DisassemblerMachine Machine { get; set; }

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
            /// Address of instruction being disassembled.
            /// </summary>
            protected uint InstructionAddress { get; set; }

            /// <summary>
            /// Gets a value indicating if the disassembly has reached the end of the specified block of memory.
            /// </summary>
            protected bool IsEndOfData => CurrentAddress >= StartAddress + Length;

            /// <summary>
            /// Represents a non-executable section.
            /// </summary>
            /// <param name="address"></param>
            /// <param name="length"></param>
            /// <param name="elementSize">'A' auto (default), 'B' byte, 'W' word, 'L' long</param>
            public class NonExecSection(uint address, uint length, char elementSize = 'A')
            {
                public virtual uint Address { get; set; } = address;
                public virtual uint Length { get; set; } = length;
                public virtual char ElementSize { get; set; } = elementSize;

                /// <summary>
                /// Return true if the section contains at least one byte of the
                /// range passed in.
                /// </summary>
                /// <param name="startAddress"></param>
                /// <param name="length"></param>
                /// <returns></returns>
                public virtual bool IntersectsWith(uint startAddress, uint length)
                {
                    if (startAddress + length <= Address || startAddress >= Address + Length)
                    {
                        return false;
                    }
                    return true;
                }
            }

            /// <summary>
            /// Maximum number of bytes to include in a disassembler record
            /// in a non-executable section.
            /// E.g., DC.B $01,$02,$03,$04
            ///       DC.W $0001,$0002
            ///       DC.L $00000001
            /// </summay>
            public const int MaxNESBytesPerRecord = 4;
            protected List<NonExecSection> NonExecSections { get; set; } = [];
            protected Dictionary<uint, NonExecSection> NonExecSectionsByAddress { get; set; } = [];

            protected delegate Operation DisassemblyHandler(Instruction inst, StringBuilder sb);
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
                _handlers.Add(OpHandlerID.CLR, CLR);
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
                _handlers.Add(OpHandlerID.LINEF, NOOPERANDS);
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
                    // Same memory, different CPU state for disassembler
                    Machine = new DisassemblerMachine(machine);
                }

                InitOpcodeHandlers();
            }

            ~Disassembler()
            {
                Disassemblers.Pop();
            }

            /// <summary>
            /// Sort the list and then walk up the list, removing duplicate sections that cover
            /// the same memory and combine those that overlap.  Updates the dictionary by 
            /// address before returning.
            /// </summary>
            protected void NormalizeSections()
            {
                // Sort the sections by address
                List<NonExecSection> sections = NonExecSections;
                sections.Sort((a, b) => a.Address.CompareTo(b.Address));

                bool adjusted;

                // Keep cycling up through the list until we haven't adjusted any sections.
                do
                {
                    adjusted = false;

                    for (int i = 0; i < sections.Count - 1; i++)
                    {
                        if (sections[i].Address + sections[i].Length > sections[i + 1].Address)
                        {
                            adjusted = true;

                            // We have an overlap, so combine them if same size
                            if (sections[i].ElementSize == sections[i + 1].ElementSize)
                            {
                                uint minAddress = Math.Min(sections[i].Address, sections[i + 1].Address);
                                uint maxAddress = Math.Max(sections[i].Address + sections[i].Length, sections[i + 1].Address + sections[i + 1].Length);
                                uint length = maxAddress - minAddress;
                                NonExecSection merged = new(minAddress, length, sections[i].ElementSize);
                                sections[i + 1] = merged;
                                sections.RemoveAt(i);
                            }
                            else
                            {
                                // Make the first one shorter.
                                sections[i].Length = sections[i + 1].Address - sections[i].Address;
                            }
                            break;
                        }
                    }
                } while (adjusted);
                NonExecSectionsByAddress.Clear();
                foreach (NonExecSection section in NonExecSections)
                {
                    NonExecSectionsByAddress.Add(section.Address, section);
                }
            }

            /// <summary>
            /// Add details of a non-executable block of data.
            /// </summary>
            /// <remarks>
            /// Non-executable sections are blocks of memory that contain data that is not executable code.
            /// Such data blocks are shown in the disassembly output using a DB directive.
            /// </remarks>
            /// <param name="startAddress">The start effectiveAddress of the block of non-executable data.</param>
            /// <param name="length">The length (in bytes) of the block of non-executable data.</param>
            /// <param name="elementSize">'A' auto (default), 'B' byte, 'W' word, 'L' long</param>
            public void SetNonExecutableRange(uint startAddress, uint length, char elementSize = 'A')
            {

                NormalizeSections();
                ClearNonExecutableRange(startAddress, length);
                NonExecSections.Add(new(startAddress, length, elementSize));
                NormalizeSections();
            }

            /// <summary>
            /// Find all executable sections in this range and either delete (if totally within range)
            /// or re-adjust to eliminate this range.  May have to split a section into two if the range
            /// is totally included in the section.
            /// </summary>
            /// <param name="startAddress"></param>
            /// <param name="length"></param>
            public void ClearNonExecutableRange(uint startAddress, uint length)
            {
                uint maxAddress = startAddress + length - 1;
                List<NonExecSection> sections = [];
                NormalizeSections();

                // CASE 0: Range to be [c]leared does not intersect any [s]ections.
                //         Nothing needs to be done.
                //
                //    This is handled by calculating the intersections and including
                //    only sections that intersect in the cases below.
                //
                foreach (var section in NonExecSections)
                {
                    if (section.IntersectsWith(startAddress, length))
                    {
                        sections.Add(section);
                    }
                }

                // Sort the sections by address
                sections.Sort((a, b) => a.Address.CompareTo(b.Address));

                foreach (var section in sections)
                {
                    uint nesMaxAddress = section.Address + section.Length - 1;

                    // CASE 1: Range to be [c]leared totally contains the current [s]ection,
                    //         so deleting the entire section is needed.
                    //
                    //    startAddress     [ccccccccccccccccc]        startAddress + length
                    //    section.Address     [ssssssssss]            section.Address + section.Length
                    //    section.Address  [ssssssssss]               section.Address + section.Length
                    //    section.Address         [ssssssssss]        section.Address + section.Length
                    //
                    if (section.Address >= startAddress && nesMaxAddress <= maxAddress)
                    {
                        // This section is totally contained within the range.
                        NonExecSections.Remove(section);
                    }
                    // CASE 2: Range to be [c]leared is totally within the current [s]ection
                    //         (and not at beginning or end of the section), so the current
                    //         section must be split into two.
                    //
                    //    startAddress         [cccccccccc]           startAddress + length
                    //    section.Address   [sssxxxxxxxxxxxssssss]    section.Address + section.Length
                    //
                    else if (section.Address < startAddress && nesMaxAddress > maxAddress)
                    {
                        // This section contains the range and must be split into two.
                        NonExecSections.Remove(section);
                        NonExecSections.Add(new(section.Address, startAddress - section.Address, section.ElementSize));
                        NonExecSections.Add(new(startAddress + length, nesMaxAddress - maxAddress, section.ElementSize));
                    }
                    // CASE 3: Range to be [c]leared top extends up into the current [s]ection,
                    //         so the section must be recalculated to cut off the bottom.
                    //
                    //    startAddress    [cccccccccc]                startAddress + length
                    //    section.Address   [xxxxxssssssss]           section.Address + section.Length
                    //    section.Address [xxxxxxxxxxsss]             section.Address + section.Length
                    //
                    else if (startAddress <= section.Address && nesMaxAddress > maxAddress)
                    {
                        // The low portion of nes encroaches into the top of the range and so nes must be
                        // truncated.
                        NonExecSections.Remove(section);
                        NonExecSections.Add(new(startAddress + length, nesMaxAddress - maxAddress, section.ElementSize));
                    }
                    // CASE 4: Range to be [c]leared bottom is less than current [s]ection top, so the
                    //         current section must be truncated on the top.
                    //
                    //    startAddress               [cccccccccc]     startAddress + length
                    //    section.Address   [sssssssssxxxx]           section.Address + section.Length
                    //
                    else if (startAddress >= section.Address)
                    {
                        // The high portion of nes encroaches into the low end of the range and so nes
                        // must be truncated.
                        NonExecSections.Remove(section);
                        NonExecSections.Add(new(section.Address, startAddress - section.Address, section.ElementSize));
                    }
                    else
                    {
                        throw new ApplicationException("ClearNonExecutableSectionRange: Should not happen - logic error!");
                    }
                }
                NormalizeSections();
            }

            /// <summary>
            /// Clear all non-executable sections.
            /// </summary>
            public void ClearNonExecutableSections()
            {
                NonExecSections.Clear();
                NonExecSectionsByAddress.Clear();
            }

            /// <summary>
            /// Perform a full disassembly of the specified block of memory.
            /// </summary>
            /// <description>
            /// DisassemblyRecord output is compatible with vasm using the following options:
            ///     vasm.exe -m68000 -Fsrec -exec -o output.h68 -L output.lis output.a68
            /// </description>
            /// <param name="startAddress">The start effectiveAddress of the block of memory being disassembled.</param>
            /// <param name="length">The length (in bytes) of the block of memory being disassembled.</param>
            /// <param name="maxCount">Maximum number of instructions or nonexecutable sections to disassemble.</param>
            /// <returns>A list of <see cref="DisassemblyRecord"/>.
            /// </returns>
            public List<DisassemblyRecord> Disassemble(uint startAddress, uint length, int maxCount = int.MaxValue)
            {
                try
                {
                    Disassembling = true;
                    Machine.CPU.PC = startAddress;
                    StartAddress = startAddress;
                    Length = length;
                    CurrentAddress = StartAddress;
                    int count = 0;

                    List<DisassemblyRecord> result = [];
                    while (!IsEndOfData && count++ < maxCount)
                    {
                        var nonExecSection = GetNonExecutableSection(CurrentAddress);
                        if (nonExecSection != null)
                        {
                            // Disassemble part of a non-executable section
                            uint len = Math.Min(nonExecSection.Address + nonExecSection.Length - CurrentAddress, MaxNESBytesPerRecord);
                            len = Math.Min(len, length - (CurrentAddress - startAddress));
                            result.Add(GetNonExecutableSectionRecord(CurrentAddress, len, nonExecSection));
                        }
                        else
                        {
                            // Disassemble an instruction
                            result.Add(DisassembleAtCurrentAddress());
                        }
                        if (Machine.Debugger?.Cancelling == true) break;
                    }
                    return result;
                }
                finally
                {
                    Disassembling = false;
                }
            }

            /// <summary>
            /// Return a Disassembly record for the section that starts at <see cref="address"/>
            /// and has the requested <see cref="length"/>.
            /// Note that the actual section may start at a much lower address and continue on past the
            /// requested length so handle appropriately.  Also, the requested section may
            /// end prior to the length passed in, so also handle that appropriately.
            /// </summary>
            /// <param name="address"></param>
            /// <param name="length"></param>
            /// <param name="section"></param>
            /// <returns></returns>
            protected DisassemblyRecord GetNonExecutableSectionRecord(uint address, uint length, NonExecSection section)
            {
                string directive;
                uint elementSize;
                OpSize size;
                directive = "DC";
                switch (section.ElementSize)
                {
                    case 'A':
                    case 'L':
                    default:
                        elementSize = 4;
                        size = OpSize.Long;
                        break;
                    case 'W':
                        elementSize = 2;
                        size = OpSize.Word;
                        break;
                    case 'B':
                        elementSize = 1;
                        size = OpSize.Byte;
                        break;
                }
                Directive dir = new(address, directive, size);

                length = Math.Min(length, elementSize);

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

                NonExecutableDataDisassembly(dir, length, address);
                var record = new DisassemblyRecord(false, dir);
                return record;
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

            /// <summary>
            /// Determines if the current effectiveAddress is within a non-executable data block.
            /// </summary>
            /// <returns>The zero-based index of the first non-executable data block that the current effectiveAddress falls within, or null if
            /// the current effectiveAddress is within executable code.</returns>
            public NonExecSection? GetNonExecutableSection(uint address)
            {
                foreach (var section in NonExecSections)
                {
                    if (address >= section.Address && address < (section.Address + section.Length))
                    {
                        return section;
                    }
                }

                return null;
            }

            /// <summary>
            /// Return true if this address is within a non-executable section.
            /// </summary>
            /// <param name="address"></param>
            /// <returns>True if in non-executable section</returns>
            public bool WithinNonExecutableData(uint address)
            {
                return GetNonExecutableSection(address) != null;
            }

            static readonly byte[] _bytes = new byte[MaxNESBytesPerRecord];
            static readonly StringBuilder _asciiBuilder = new();

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
            /// Generate disassembly for a non-executable section.  Uses the element size
            /// as much as possible, then fills in the end with smaller elements if necessary
            /// </summary>
            /// <param name="dir"></param>
            /// <param name="length">Must be <= 4</param>
            /// <param name="startAddress"></param>
            /// <param name="elementSize"></param>
            /// <returns></returns>
            protected void NonExecutableDataDisassembly(Directive dir, uint length, uint startAddress)
            {
                StringBuilder sb = new();
                string dc;
                if (length == 2)
                {
                    dc = "DC.W";
                }
                else if (length == 4)
                {
                    dc = "DC.L";
                }
                else
                {
                    dc = "DC.B";
                }
                sb.Append(dc);
                sb.AppendTab(EAColumn);
                Array.Clear(_bytes);

                if (length == 2)
                {
                    ushort val = 0;
                    for (int i = 0; i < length; i++)
                    {
                        if (IsEndOfData) { break; }
                        byte value = ReadNextByte();
                        _bytes[i] = value;
                        val = (ushort)((val << 8) | value);
                    }

                    dir.Operands.Add(new ImmediateOperand(val));
                    sb.Append($"${val:x4}        '{GetBytesAsString(_bytes, length)}'");
                }
                else if (length == 4)
                {
                    uint val = 0;
                    for (int i = 0; i < length; i++)
                    {
                        if (IsEndOfData) { break; }
                        byte value = ReadNextByte();
                        _bytes[i] = value;
                        val = (val << 8) | value;
                    }

                    dir.Operands.Add(new ImmediateOperand(val));
                    sb.Append($"${val:x8}    '{GetBytesAsString(_bytes, length)}'");
                }
                else
                {
                    for (int i = 0; i < length; i++)
                    {
                        if (IsEndOfData) { break; }
                        byte value = ReadNextByte();
                        _bytes[i] = value;
                        if (i > 0)
                        {
                            sb.Append(',');
                        }

                        dir.Operands.Add(new ImmediateOperand(value));
                        sb.Append($"${value:x2}");
                    }

                    sb.Append($"  '{GetBytesAsString(_bytes, length)}'");
                }
                dir.Assembly = sb.ToString();
            }

            bool _disassembling = false;
            protected bool Disassembling
            {
                get
                {
                    return _disassembling;
                }
                set
                {
                    _disassembling = value;
                    if (Machine.Debugger != null)
                    {
                        Machine.Debugger.Disassembling = value;
                    }
                }
            }

#if REORG
            private bool reOrgInProgress = false;
            private uint reOrgAddress = 0;
#endif 

            /// <summary>
            /// Disassemble one instruction at the current instruction.  The address is guaranteed 
            /// to not be in a non-executable section.
            /// </summary>
            /// <returns></returns>
            protected DisassemblyRecord DisassembleAtCurrentAddress()
            {
                try
                {
                    InstructionAddress = CurrentAddress;  // CurrentAddress will be incremented by ReadNextByte() below
                    Disassembling = true;
                    bool endOfData = false;
                    string assembly = "UNKNOWN";
                    Operation op;

                    // Decoder fetches the instruction at the current PC, so set it to
                    // where we want to disassembler.
                    Machine.CPU.PC = InstructionAddress;
                    Instruction? inst = Machine.Decoder.FetchInstruction();

                    // PC has been incremented to point to the next instruction after this one.
                    int length = (int)Machine.CPU.PC - (int)InstructionAddress;

                    List<byte> codeBytes = [];

                    // Show the actual instruction bytes
                    StringBuilder code = new();
                    int i;
                    for (i = 0; (i < length) && !IsEndOfData; i++)
                    {
                        byte value = ReadNextByte();
                        codeBytes.Add(value);
                    }
                    if (IsEndOfData && i < length)
                    {
                        // End of memory block before we finished
                        assembly = "... ";
                        op = new(InstructionAddress, assembly);
                        endOfData = true;
                    }
                    else if (inst != null)
                    {
                        StringBuilder sb = new();
                        if (_handlers.TryGetValue(inst.Info.HandlerID, out DisassemblyHandler? instructionDisassembler))
                        {
                            op = instructionDisassembler(inst, sb);
                        }
                        else
                        {
                            op = new Operation(InstructionAddress, "????");
                            sb.Append("????");
                        }
                        assembly = sb.ToString();
                    }
                    else
                    {
                        // inst == null
                        op = new(InstructionAddress, "MISSING");
                    }

                    byte[] machineCode = [.. codeBytes];
                    op.MachineCode = machineCode;
                    op.Assembly = assembly;
                    return new DisassemblyRecord(endOfData, op);
                }
                finally
                {
                    Disassembling = false;
                }
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
                sb.AppendTab(EAColumn);
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
            /// <returns></returns>
            protected virtual string? GetLabelName(uint address)
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
                            //Debug.Assert(ext1.HasValue, "Required extension word is not available");
                            if (ext1.HasValue)
                            {
                                operand = new AddressDispOperand(regNum, (short)ext1.Value);
                            }
                            break;
                        case (byte)AddrMode.AddressIndex:
                            //Debug.Assert(ext1.HasValue, "Required extension word is not available");
                            if (ext1.HasValue)
                            {
                                sbyte disp = (sbyte)(ext1.Value & 0x00FF);
                                int indexRegNum = ((ext1.Value & 0x7000) >> 12);
                                OpSize sz = (ext1.Value & 0x0800) == 0 ? OpSize.Word : OpSize.Long;
                                operand = new AddressIndexOperand(regNum, indexRegNum, sz, disp);
                            }
                            break;
                        case 0x0038:
                            switch (ea)
                            {
                                case (byte)AddrMode.AbsShort:
                                    //Debug.Assert(ext1.HasValue, "Required extension word is not available");
                                    if (ext1.HasValue)
                                    {
                                        address = ext1.Value | ((ext1.Value & 0x8000) == 0 ? 0x0 : 0xFFFF0000);
                                        operand = new LabelOperand(address.Value);
                                    }
                                    break;
                                case (byte)AddrMode.AbsLong:
                                    //Debug.Assert(ext1.HasValue && ext2.HasValue, "Required extension word is not available");
                                    if (ext1.HasValue && ext2.HasValue)
                                    {
                                        address = (uint)((ext1.Value << 16) + ext2.Value);
                                        operand = new LabelOperand(address.Value);
                                        size = OpSize.Long;
                                    }
                                    break;
                                case (byte)AddrMode.PCDisp:
                                    //Debug.Assert(ext1.HasValue, "Required extension word is not available");
                                    if (ext1.HasValue)
                                    {
                                        // PC has been incremented past the extension word.  The definition of
                                        // PC displacement uses the value of the extension word address as the PC value.
                                        int pcDecrement = 2; // Assume source, PC just after ext1 or dest, PC just after ext1
                                        if (eaType == EAType.Source && instruction.DestExtWord1 != null)
                                        {
                                            pcDecrement += (instruction.DestExtWord2 == null) ? 2 : 4;
                                        }
                                        address = (uint)((int)Machine.CPU.PC - pcDecrement + (short)ext1.Value);
                                        operand = new LabelOperand(address.Value);
                                    }
                                    break;
                                case (byte)AddrMode.PCIndex:
                                    //Debug.Assert(ext1.HasValue, "Required extension word is not available");
                                    if (ext1.HasValue)
                                    {
                                        byte disp = (byte)(ext1.Value & 0x00FF);
                                        byte indexRegNum = (byte)((ext1.Value & 0x7000) >> 12);
                                        OpSize sz = (ext1.Value & 0x0800) == 0 ? OpSize.Word : OpSize.Long;

                                        // PC has been incremented past the extension word.  The definition of
                                        // PC displacement uses the value of the extension word address as the PC value.
                                        int pcDecrement = 2; // Assume source, PC just after ext1 or dest, PC just after ext1
                                        if (eaType == EAType.Source && instruction.DestExtWord1 != null)
                                        {
                                            pcDecrement += (instruction.DestExtWord2 == null) ? 2 : 4;
                                        }

                                        uint baseAddress = (uint)((sbyte)disp + (int)Machine.CPU.PC - pcDecrement);
                                        operand = new PCIndexOperand(indexRegNum, baseAddress, sz);
                                    }
                                    break;
                                case (byte)AddrMode.Immediate:
                                    //Debug.Assert(ext1.HasValue, "Required extension word is not available");
                                    if (ext1.HasValue)
                                    {
                                        if (opSize == OpSize.Long)
                                        {
                                            //Debug.Assert(ext2.HasValue, "Required extension word is not available");
                                            if (ext2.HasValue)
                                            {
                                                immVal = (uint)((ext1.Value << 16) + ext2.Value);
                                                operand = new ImmediateOperand(immVal!.Value);
                                                size = OpSize.Long;
                                            }
                                        }
                                        else if (opSize == OpSize.Word)
                                        {
                                            immVal = ext1.Value;
                                            operand = new ImmediateOperand((ushort)immVal!.Value);
                                        }
                                        else
                                        {
                                            immVal = ext1.Value;
                                            operand = new ImmediateOperand((byte)immVal!.Value);
                                        }
                                    }
                                    isMemory = false;
                                    break;
                            }
                            break;
                    }
                }


                if (operand == null)
                {
                    operand = new();
                }
                else
                {
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
                Operation op = new(InstructionAddress, inst.Info.Mnemonic);
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
            protected bool HasSourceExtWord1(Instruction inst, StringBuilder sb)
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

            protected Operation PEA(Instruction inst, StringBuilder sb)
            {
                Operation op = AppendMnemonic(inst, sb);
                op.Size = AppendSizeAndTab(inst, sb);

                op.Operands.Add(EffectiveAddressOp(inst, EAType.Source));

                sb.Append(op.Operands);
                return op;
            }

            protected Operation CLR(Instruction inst, StringBuilder sb)
            {
                Operation op = AppendMnemonic(inst, sb);
                op.Size = AppendSizeAndTab(inst, sb);

                op.Operands.Add(EffectiveAddressOp(inst, EAType.Destination));

                sb.Append(op.Operands);
                return op;
            }

            protected Operation DST(Instruction inst, StringBuilder sb)
            {
                Operation op = AppendMnemonic(inst, sb);
                op.Size = AppendSizeAndTab(inst, sb);

                op.Operands.Add(EffectiveAddressOp(inst, EAType.Destination));

                sb.Append(op.Operands);
                return op;
            }

            protected Operation IMMEDtoCCR(Instruction inst, StringBuilder sb)
            {
                string mnemonic = inst.Info.Mnemonic;
                mnemonic = mnemonic[..^"toCCR".Length];
                Operation op = new(InstructionAddress, mnemonic);
                sb.Append(mnemonic);
                sb.AppendTab(EAColumn);

                // SourceExtWord1 holds the immediate operand value.
                if (HasSourceExtWord1(inst, sb))
                {
                    ushort value = (ushort)(inst.SourceExtWord1!.Value & 0x001F);

                    op.Operands.Add(new ImmediateOperand((byte)value));
                    op.Operands.Add(new CCROperand());
                    //sb.Append($"#{FormatOperand(InstructionAddress, op.Operands[0])},CCR");

                    sb.Append(op.Operands);
                }
                return op;
            }

            protected Operation IMMEDtoSR(Instruction inst, StringBuilder sb)
            {
                string mnemonic = inst.Info.Mnemonic;
                mnemonic = mnemonic[..^"toSR".Length];
                Operation op = new(InstructionAddress, mnemonic);
                sb.Append(mnemonic);
                sb.AppendTab(EAColumn);

                // SourceExtWord1 holds the immediate operand value.
                if (inst.SourceExtWord1.HasValue)
                {
                    ushort value = inst.SourceExtWord1.Value;

                    op.Operands.Add(new ImmediateOperand(value));
                    op.Operands.Add(new SROperand());

                    sb.Append(op.Operands);
                }
                return op;
            }

            protected Operation MOVEtoSR(Instruction inst, StringBuilder sb)
            {
                Operation op = new(InstructionAddress, "MOVE");
                sb.Append("MOVE");
                sb.AppendTab(EAColumn);

                op.Operands.Add(EffectiveAddressOp(inst, EAType.Source));
                op.Operands.Add(new SROperand());

                sb.Append(op.Operands);
                return op;
            }

            protected Operation MOVEtoCCR(Instruction inst, StringBuilder sb)
            {
                Operation op = new(InstructionAddress, "MOVE");
                sb.Append("MOVE");
                sb.AppendTab(EAColumn);

                op.Operands.Add(EffectiveAddressOp(inst, EAType.Source));
                op.Operands.Add(new CCROperand());

                sb.Append(op.Operands);
                return op;
            }

            protected Operation MOVEfromSR(Instruction inst, StringBuilder sb)
            {
                Operation op = new(InstructionAddress, "MOVE");
                sb.Append("MOVE");
                sb.AppendTab(EAColumn);

                op.Operands.Add(new SROperand());
                op.Operands.Add(EffectiveAddressOp(inst, EAType.Destination));

                sb.Append(op.Operands);
                return op;
            }

            protected Operation IMMED_OP(Instruction inst, StringBuilder sb)
            {
                Operation op = AppendMnemonic(inst, sb);
                OpSize opSize = AppendSizeAndTab(inst, sb);
                op.Size = opSize;
                uint? value = OpcodeExecutionHandler.GetSizedOperandValue(opSize, inst.SourceExtWord1, inst.SourceExtWord2);
                if (value.HasValue)
                {
                    Operand operand = opSize switch
                    {
                        OpSize.Byte => new ImmediateOperand((byte)value),
                        OpSize.Word => new ImmediateOperand((short)value),
                        OpSize.Long => new ImmediateOperand((uint)value),
                        _ => new ImmediateOperand(0xDEADBEEF)
                    };
                    op.Operands.Add(operand);
                    op.Operands.Add(EffectiveAddressOp(inst, EAType.Destination));

                    sb.Append(op.Operands);
                }
                return op;
            }

            protected Operation MULS_MULU_DIVU_DIVS(Instruction inst, StringBuilder sb)
            {
                Operation op = AppendMnemonic(inst, sb);
                op.Size = OpSize.Word;
                sb.Append(".W");
                sb.AppendTab(EAColumn);

                int dRegNum = (inst.Opcode & 0x0E00) >> 9;

                op.Operands.Add(EffectiveAddressOp(inst, EAType.Source));
                op.Operands.Add(new DataRegisterOperand(dRegNum));

                sb.Append(op.Operands);
                return op;
            }

            protected Operation SUBX(Instruction inst, StringBuilder sb)
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

            protected Operation ADD_SUB_OR_AND_EOR_CMP(Instruction inst, StringBuilder sb)
            {
                Operation op = AppendMnemonic(inst, sb);
                op.Size = AppendSizeAndTab(inst, sb);
                int dRegNum = (inst.Opcode & 0x0E00) >> 9;
                bool dnDest = (inst.Opcode & 0x0100) == 0;
                if (dnDest)
                {
                    op.Operands.Add(EffectiveAddressOp(inst, EAType.Destination));
                    op.Operands.Add(new DataRegisterOperand(dRegNum));
                }
                else
                {
                    op.Operands.Add(new DataRegisterOperand(dRegNum));
                    op.Operands.Add(EffectiveAddressOp(inst, EAType.Destination));
                }
                sb.Append(op.Operands);
                return op;
            }

            protected Operation CMPM(Instruction inst, StringBuilder sb)
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

            protected Operation MOVE(Instruction inst, StringBuilder sb)
            {
                Operation op = AppendMnemonic(inst, sb);
                op.Size = AppendSizeAndTab(inst, sb);

                op.Operands.Add(EffectiveAddressOp(inst, EAType.Source));
                op.Operands.Add(EffectiveAddressOp(inst, EAType.Destination));

                sb.Append(op.Operands);
                return op;
            }

            protected Operation MOVEA(Instruction inst, StringBuilder sb)
            {
                Operation op = AppendMnemonic(inst, sb);
                op.Size = AppendSizeAndTab(inst, sb);
                int regNum = (inst.Opcode & 0x0E00) >> 9;

                op.Operands.Add(EffectiveAddressOp(inst, EAType.Source));
                op.Operands.Add(new AddressRegisterOperand(regNum));

                sb.Append(op.Operands);
                return op;
            }

            protected Operation MOVEP(Instruction inst, StringBuilder sb)
            {
                Operation op = AppendMnemonic(inst, sb);
                OpSize size = (inst.Opcode & 0x0040) == 0 ? OpSize.Word : OpSize.Long;
                op.Size = size;
                string sz = size == OpSize.Word ? ".W" : ".L";
                sb.Append(sz);
                sb.AppendTab(EAColumn);

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
                    sb.Append("[SourceExtWord1 missing]");
                }

                sb.Append(op.Operands);
                return op;
            }

            protected Operation MOVEM(Instruction inst, StringBuilder sb)
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
                            op.Operands.Add(new RegListOperand(regMask, true));
                        }
                        else
                        {
                            op.Operands.Add(new RegListOperand(regMask, false));
                        }
                        op.Operands.Add(EffectiveAddressOp(inst, EAType.Destination));
                    }
                    else
                    {
                        // Source is mem, dest is reg (but EA is in dest field)
                        op.Operands.Add(EffectiveAddressOp(inst, EAType.Destination));
                        op.Operands.Add(new RegListOperand(regMask, false));
                    }
                }

                sb.Append(op.Operands);
                return op;
            }

            protected Operation MOVEQ(Instruction inst, StringBuilder sb)
            {
                Operation op = AppendMnemonic(inst, sb);
                op.Size = OpSize.Long;
                sb.Append(".L");
                sb.AppendTab(EAColumn);

                int dRegNum = (inst.Opcode & 0x0E00) >> 9;
                int data = Helpers.SignExtendValue((uint)(inst.Opcode & 0x00FF), OpSize.Byte);

                op.Operands.Add(new QuickDataOperand(data));
                op.Operands.Add(new DataRegisterOperand(dRegNum));

                sb.Append(op.Operands);
                return op;
            }

            protected Operation ADDQ_SUBQ(Instruction inst, StringBuilder sb)
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
                        sz = "";
                        opSize = null;
                        break;
                };
                op.Size = opSize;
                sb.Append(sz);
                sb.AppendTab(EAColumn);

                op.Operands.Add(new QuickDataOperand(addVal));

                // When being applied to an effectiveAddress register, we work with the entire 32-bit value regardless
                // of the size that has been specified. This operation also doesn't affect the flags.
                if ((inst.Opcode & 0x0038) == (int)AddrMode.AddressRegister)
                {
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
                sb.AppendTab(EAColumn);

                if (inst.SourceExtWord1.HasValue)
                {
                    byte regNum = (byte)(inst.Opcode & 0x0007);
                    int disp = Helpers.SignExtendValue((uint)inst.SourceExtWord1, OpSize.Word);

                    op.Operands.Add(new AddressRegisterOperand(regNum));
                    op.Operands.Add(new ImmediateOperand(disp));

                    sb.Append(op.Operands);
                }

                return op;
            }

            protected Operation UNLK(Instruction inst, StringBuilder sb)
            {
                Operation op = AppendMnemonic(inst, sb);
                sb.AppendTab(EAColumn);

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

                Operation op = new(InstructionAddress, sb.ToString());
                uint pc = Machine.CPU.PC;
                int disp = inst.Opcode & 0x00FF;
                OpSize size = OpSize.Word;
                if (disp == 0)
                {
                    // 16-bit displacement, uses ExtWord1
                    if (inst.SourceExtWord1.HasValue)
                    {
                        // Byte displacement is zero so use the extension word value as a 16-bit displacement.
                        disp = Helpers.SignExtendValue((uint)inst.SourceExtWord1, OpSize.Word);

                        // Step PC back a word as it should be pointing immediately after the instruction opcode word
                        // for the displacement to be correct (whereas it will currently be pointing at the location immediately
                        // after the extension word)
                        pc -= 2;
                    }
                    else
                    {
                        sb.Append("[SourceExtWord1 missing]");
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
                sb.AppendTab(EAColumn);

                op.Operands.Add(EffectiveAddressOp(inst, EAType.Source));

                sb.Append(op.Operands);
                return op;
            }

            protected Operation BRA_BSR(Instruction inst, StringBuilder sb)
            {
                Operation op = AppendMnemonic(inst, sb);
                uint pc = Machine.CPU.PC;
                int disp = inst.Opcode & 0x00FF;
                OpSize size = OpSize.Word;
                if (disp == 0)
                {
                    // Byte displacement is zero so use the extension word value as a 16-bit displacement.
                    if (inst.SourceExtWord1.HasValue)
                    {
                        disp = Helpers.SignExtendValue((uint)inst.SourceExtWord1, OpSize.Word);

                        // Step PC back a word as it should be pointing immediately after the instruction opcode word
                        // for the displacement to be correct (whereas it will currently be pointing at the location immediately
                        // after the extension word)
                        pc -= 2;
                    }
                    else
                    {
                        sb.Append("[SourceExtWord1 missing]");
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

                Operation op = new(InstructionAddress, sb.ToString(), OpSize.Word);
                sb.Append(".W");
                sb.AppendTab(EAColumn);

                int dRegNum = inst.Opcode & 0x0007;
                uint pc = Machine.CPU.PC;

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
                    sb.Append($"ERROR: Missing inst.SourceExtWord1");
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
                Condition cond = (Condition)((inst.Opcode & 0x0F00) >> 8);
                AppendCondition(cond, sb);

                Operation op = new(InstructionAddress, sb.ToString(), OpSize.Byte);
                sb.Append(".B"); // Size is always byte
                sb.AppendTab(EAColumn);

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

                if (isMemory)
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
                    //sb.Append($"D{regNum}");
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
                int regNum = (inst.Opcode & 0x0E00) >> 9;

                op.Operands.Add(EffectiveAddressOp(inst, EAType.Source));
                if (op.Operands[0].Size != null)
                {
                    AppendSizeAndTab(op.Operands[0].Size, sb);
                }
                else
                {
                    sb.AppendTab(EAColumn);
                }

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
                    _ => ".?"
                };
                sb.Append(sz);
                sb.AppendTab(EAColumn);

                byte regNum = (byte)(inst.Opcode & 0x0007);

                op.Operands.Add(new DataRegisterOperand(regNum));

                sb.Append(op.Operands);
                return op;
            }

            protected Operation SWAP(Instruction inst, StringBuilder sb)
            {
                Operation op = AppendMnemonic(inst, sb);
                sb.AppendTab(EAColumn);

                byte regNum = (byte)(inst.Opcode & 0x0007);
                op.Operands.Add(new DataRegisterOperand(regNum));

                sb.Append(op.Operands);
                return op;
            }

            protected Operation MOVEUSP(Instruction inst, StringBuilder sb)
            {
                Operation op = new(InstructionAddress, "MOVE");
                sb.Append("MOVE");
                AppendTab(EAColumn, sb);

                byte regNum = (byte)(inst.Opcode & 0x0007);
                if ((inst.Opcode & 0x0008) == 0)
                {
                    op.Operands.Add(new AddressRegisterOperand(regNum));
                    op.Operands.Add(new AddressRegisterOperand(USP));
                    //sb.Append($"{AddressReg(regNum)},USP");
                }
                else
                {
                    op.Operands.Add(new AddressRegisterOperand(USP));
                    op.Operands.Add(new AddressRegisterOperand(regNum));
                    //sb.Append($"USP,{AddressReg(regNum)}");
                }

                sb.Append(op.Operands);
                return op;
            }

            protected Operation ABCD_SBCD(Instruction inst, StringBuilder sb)
            {
                Operation op = AppendMnemonic(inst, sb);
                AppendTab(EAColumn, sb);

                byte rSrc = (byte)(inst.Opcode & 0x0007);
                byte rDest = (byte)((inst.Opcode & 0x0E00) >> 9);

                if ((inst.Opcode & 0x0008) == 0)
                {
                    // Working with data registers
                    op.Operands.Add(new DataRegisterOperand(rSrc));
                    op.Operands.Add(new DataRegisterOperand(rDest));
                    //sb.Append($"D{rSrc},D{rDest}");
                }
                else
                {
                    // Working with memory addresses.
                    op.Operands.Add(new AddressPreDecOperand(rSrc));
                    op.Operands.Add(new AddressPreDecOperand(rDest));
                    //sb.Append($"-({AddressReg(rSrc)}),-({AddressReg(rDest)})");
                }

                sb.Append(op.Operands);
                return op;
            }

            protected Operation EXG(Instruction inst, StringBuilder sb)
            {
                Operation op = AppendMnemonic(inst, sb);
                op.Size = OpSize.Long;
                sb.Append(".L");
                sb.AppendTab(EAColumn);

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
                        //sb.Append($"D{rY},D{rX}");
                        break;
                    case 0x09:      // Address Register <-> Address Register
                        op.Operands.Add(new AddressRegisterOperand(rY));
                        op.Operands.Add(new AddressRegisterOperand(rX));
                        //sb.Append($"{AddressReg(rY)},{AddressReg(rX)}");
                        break;
                    case 0x11:      // Data Register <-> Address Register
                        op.Operands.Add(new DataRegisterOperand(rY));
                        op.Operands.Add(new AddressRegisterOperand(rX));
                        //sb.Append($"D{rY},{AddressReg(rX)}");
                        break;
                    default:
                        //Debug.Assert(false, "Invalid operating mode for EXG instruction.");
                        break;
                }

                sb.Append(op.Operands);
                return op;
            }

            protected Operation STOP(Instruction inst, StringBuilder sb)
            {
                Operation op = AppendMnemonic(inst, sb);
                sb.AppendTab(EAColumn);

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
                sb.AppendTab(EAColumn);

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
                    //sb.Append($"D{rX},D{rY}");
                }
                else
                {
                    op.Operands.Add(new AddressPreDecOperand(rX));
                    op.Operands.Add(new AddressPreDecOperand(rY));
                    //sb.Append($"-({AddressReg(rX)}),-({AddressReg(rY)})");
                }

                sb.Append(op.Operands);
                return op;
            }

            protected Operation NOOPERANDS(Instruction inst, StringBuilder sb)
            {
                Operation op = AppendMnemonic(inst, sb);

                return op;
            }

            protected Operation NONE(Instruction inst, StringBuilder sb)
            {
                Operation op = AppendMnemonic(inst, sb);
                op.Name = " ???";

                sb.Append(op.Name);
                return op;
            }

            protected virtual Operation LINEA(Instruction inst, StringBuilder sb)
            {
                Operation op = new(InstructionAddress, "LINEA");
                sb.Append($"LINEA");
                sb.AppendTab(EAColumn);

                op.Operands.Add(new ImmediateOperand((ushort)(inst.Opcode & 0x0fff), "${0:x3}"));
                // sb.Append($"${(ushort)(inst.Opcode & 0x0fff):x3}");

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
                            if (startReg.HasValue)
                            {
                                if (lastReg.HasValue)
                                {
                                    if (lastReg.Value != startReg.Value)
                                    {
                                        sb.Append('-');
                                        sb.Append(_reg[lastReg.Value]);
                                    }
                                }
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
                public AddressIndexOperand(AddressRegister addressRegister, DataRegister indexRegister, OpSize indexSize, Displacement displacement, string? format = null) : base(format)
                {
                    AddressRegister = addressRegister;
                    IndexRegister = indexRegister;
                    Displacement = displacement;
                    IndexSize = indexSize;
                }

                public AddressIndexOperand(int addressRegNum, int dataRegNum, OpSize indexSize, sbyte disp, string? format = null) : this(AddressRegisters[addressRegNum], DataRegisters[dataRegNum], indexSize, new Displacement(disp), format) { }
                
                public AddressRegister AddressRegister { get; set; }
                public DataRegister IndexRegister { get; set; }
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

                    Expression = new Expression(this, 1, disp);
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

                    if (Op.Name != "LINEA" && Op.Name != "DC")
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
                public LabelOperand(Label label, string? format = null) : base(format)
                {
                    Label = label;
                }

                public LabelOperand(uint address, string? format = null) : this(new Label(address), format) { }

                public Label Label { get; set; }

                /// <summary>
                /// Format the operand disassembly display and for the assembler.
                /// </summary>
                /// <returns>Operand string suitable for an assembler.</returns>
                public override string? ToString()
                {
                    string? disp = CurrentDisassembler?.GetExpression(Op.Address, Pos) ?? CurrentDisassembler?.GetLabelName(Label.Address);
                    if (disp == null && Format != null)
                    {
                        disp = string.Format(Format, Label.Address);
                    }
                    else disp ??= $"{Label}";

                    Expression = new Expression(this, 0, disp);
                    if (Size == OpSize.Long)
                    {
                        disp = $"({disp}).L";
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
                            disp = $"{Displacement}";
                        }
                    }

                    Expression = new Expression(this, 0, disp);
                    return disp;
                }
            }

            public class PCIndexOperand : Operand
            {
                public PCIndexOperand(DataRegister indexRegister, Displacement displacement, OpSize size, string? format = null) : base(format)
                {
                    IndexRegister = indexRegister;
                    Displacement = displacement;
                    Size = size;
                }

                public PCIndexOperand(int indexRegNum, Displacement displacement, OpSize size, string? format = null) : this(DataRegisters[indexRegNum], displacement, size, format) { }
                public PCIndexOperand(int indexRegNum, uint address, OpSize size, string? format = null) : this(DataRegisters[indexRegNum], new Displacement(address), size, format) { }
                
                public DataRegister IndexRegister { get; set; }
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
                public string Text
                {
                    get
                    {
                        if (_text == null)
                        {
                            _text = ToString();
                        }
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

            /// <summary>
            /// Data register name.
            /// </summary>
            public class DataRegister
            {
                /// <summary>
                /// Create an instance.
                /// </summary>
                /// <param name="name"></param>
                public DataRegister(string name)
                {
                    Name = name;
                }

                /// <summary>
                /// Register name, e.g. "D2", "D7".
                /// </summary>
                public string Name { get; set; }

                public override string ToString()
                {
                    return Name;
                }
            }

            /// <summary>
            /// Address register name.
            /// </summary>
            public class AddressRegister
            {
                public AddressRegister(string name)
                {
                    Name = name;
                }

                /// <summary>
                /// Register name, e.g., "A3", "SP".
                /// </summary>
                public string Name { get; set; }

                public override string ToString()
                {
                    return Name;
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
            public AddressRegister USP = AddressRegisters[8];

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
            /// Base class for directives (e.g., "DC", "EQU") and operations (e.g., "MOVE", "JMP").
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
                /// Size (<see cref="OpSize"/>) of the operation if not default (usually Opsize.Word).
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
                public Expression? GetExpressionAtColumn(int assyStartColumn, int column)
                {
                    StringBuilder sb = new();
                    sb.Append(Name);
                    sb.AppendSizeAndTab(Size);
                    int start = sb.Length + assyStartColumn;

                    foreach (Operand op in Operands)
                    {
                        if (op.Expression != null)
                        {
                            int opStart = start + op.Expression.StartCol;
                            if (opStart >= column && column <= opStart + op.Expression.Text.Length)
                            {
                                return op.Expression;
                            }
                            start = opStart + op.Expression.Text.Length + 1; // comma after
                        }
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
