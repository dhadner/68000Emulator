using PendleCodeMonkey.MC68000EmulatorLib.Enumerations;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Xml.Linq;

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
                /// Comments can be provided by subclasses if the <see cref="Disassembler"/>
                /// class overriding the <see cref= "Comment" />
                /// method in that class.                
                /// </summary>
                public string? Comment => Op.Comment;

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
            /// Column where the effective address (source,dest) text starts.
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
                switch (section.ElementSize) {
                    case 'A':
                    case 'L':
                    default:
                        directive = "DC.L";
                        elementSize = 4;
                        size = OpSize.Long;
                        break;
                    case 'W':
                        directive = "DC.W";
                        elementSize = 2;
                        size = OpSize.Word;
                        break;
                    case 'B':
                        directive = "DC.B";
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

                dir.Assembly = NonExecutableDataDisassembly(length, address);
                dir.Comment = Comment(address, dir.MachineCode, dir.Assembly, true);
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
            /// <param name="length">Must be <= 4</param>
            /// <param name="startAddress"></param>
            /// <param name="elementSize"></param>
            /// <returns></returns>
            protected string NonExecutableDataDisassembly(uint length, uint startAddress)
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
                        sb.Append($"${value:x2}");
                    }
                    sb.Append($"  '{GetBytesAsString(_bytes, length)}'");
                }
                return sb.ToString();
            }

            /// <summary>
            /// Allow subclass to add a comment.
            /// </summary>
            /// <param name="address"></param>
            /// <param name="codeBytes"></param>
            /// <param name="assembly"></param>
            /// <param name="isNonExecutableSection"></param>
            /// <returns></returns>
            protected virtual string? Comment(uint address, byte[] codeBytes, string? assembly, bool isNonExecutableSection = false)
            {
                return null;
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

                    byte[] machineCode = codeBytes.ToArray();
                    op.MachineCode = machineCode;
                    op.Assembly = assembly;
                    op.Comment = Comment(InstructionAddress, machineCode, assembly, false);
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
            /// Append an address register.
            /// </summary>
            /// <param name="regNum"></param>
            /// <returns></returns>
            protected static string AddressReg(int regNum)
            {
                return regNum == 7 ? "SP" : $"A{regNum}";
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
            protected static OpSize AppendSizeAndTab(OpSize? size, StringBuilder sb)
            {
                string sSize = size switch
                {
                    OpSize.Byte => ".B",
                    OpSize.Long => ".L",
                    _ => ".W"
                };
                sb.Append(sSize);
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
            /// Append a data register.
            /// </summary>
            /// <param name="regnum"></param>
            /// <param name="sb"></param>
            protected static Operand AppendDataRegister(int regnum, StringBuilder sb, int pos)
            {
                Operand op = new(DataRegisters[regnum], pos);
                sb.Append('D');
                sb.Append(regnum);
                return op;
            }

            /// <summary>
            /// Append the effective address.
            /// </summary>
            /// <param name="instruction"></param>
            /// <param name="eaType"></param>
            /// <param name="sb"></param>
            protected Operand AppendEffectiveAddress(Instruction instruction, EAType eaType, StringBuilder sb, int? pos, string? format = null)
            {
                (_, _, string effectiveAddress, Operand operand) = ComputeEffectiveAddress(instruction, eaType, pos, format);
                sb.Append(effectiveAddress);
                return operand;
            }

            /// <summary>
            /// Return true if the effective address is a memory reference.
            /// </summary>
            /// <param name="instruction"></param>
            /// <param name="eaType"></param>
            /// <returns></returns>
            protected bool EffectiveAddressIsMemory(Instruction instruction, EAType eaType)
            {
                (bool isMemory, _, _, _) = ComputeEffectiveAddress(instruction, eaType);
                return isMemory;
            }

            protected virtual string? GetLabelName(uint address)
            {
                return null;
            }

            protected virtual string? GetExpression(uint address, int operandPos)
            {
                return null;
            }

            /// <summary>
            /// Return a formatted string representing the source or destination
            /// expression that resolves to an effective address or offset
            /// for clearer documentation in the disassembled code.
            /// 
            /// 68000 assembly language has either 0 or 1 constant values in the
            /// source and 0 or 1 constant values in the destination.  Subclasses
            /// can create dictionaries of source and detstination annotations/
            /// expressions to substitute into the disassembly for additional
            /// clarity.  For example, source value at address 420330 may be replaced 
            /// by 'RecordOrigin+FieldOffset1' (without the quotes) by 
            /// looking up the annotations in a dictionary ordered by address and
            /// containing entries for the source and/or destination expressions
            /// to be used for the respective arguments.
            /// </summary>
            /// <param name="assemblyAddress"></param>
            /// <param name="operand"></param>
            /// <returns>Formatted operand</returns>
            protected virtual string FormatOperand(uint assemblyAddress, Operand operand)
            {
                string opStr;
                string? disp;
                switch (operand.Mode)
                {
                    case Mode.DataRegister:
                        if (operand.DataRegister == null)
                        {
                            throw new ArgumentException("DataRegister is null");
                        }
                        opStr = $"{operand.DataRegister}";
                        break;
                    case Mode.AddressRegister:
                        if (operand.AddressRegister == null)
                        {
                            throw new ArgumentException("AddressRegister is null");
                        }
                        opStr = $"{operand.AddressRegister}";
                        break;
                    case Mode.Address:
                        if (operand.AddressRegister == null)
                        {
                            throw new ArgumentException("AddressRegister is null");
                        }
                        opStr = $"({operand.AddressRegister})";
                        break;
                    case Mode.AddressPostInc:
                        if (operand.AddressRegister == null)
                        {
                            throw new ArgumentException("AddressRegister is null");
                        }
                        opStr = $"({operand.AddressRegister})+";
                        break;
                    case Mode.AddressPreDec:
                        if (operand.AddressRegister == null)
                        {
                            throw new ArgumentException("AddressRegister is null");
                        }
                        opStr = $"-({operand.AddressRegister})";
                        break;
                    case Mode.AddressDisp:
                        if (operand.Displacement == null)
                        {
                            throw new ArgumentException("Displacement is null");
                        }
                        disp = GetExpression(assemblyAddress, operand.Pos);
                        if (disp == null)
                        {
                            if (operand.Format != null)
                            {
                                disp = string.Format(operand.Format, operand.Displacement.Value);
                            }
                            else if (operand.Displacement.Value < 0)
                            {
                                disp = $"{operand.Displacement.Value}";
                            }
                            else
                            {
                                disp = $"${operand.Displacement.Value:x4}";
                            }                          
                        }
                        opStr = $"({disp},{operand.AddressRegister})";
                        break;
                    case Mode.AddressIndex:
                        if (operand.Displacement == null)
                        {
                            throw new ArgumentException("Displacement is null");
                        }
                        if (operand.IndexRegister == null)
                        {
                            throw new ArgumentException("IndexRegister is null");
                        }
                        if (operand.AddressRegister == null)
                        {
                            throw new ArgumentException("AddressRegister is null");
                        }
                        char sz = operand.IndexSize == OpSize.Long ? 'L' : 'W';
                        disp = GetExpression(assemblyAddress, operand.Pos);
                        if (disp == null)
                        {
                            if (operand.Format != null)
                            {
                                disp = string.Format(operand.Format, operand.Displacement.Value);
                            }
                            else if (operand.Displacement.Value <= 0)
                            {
                                disp = $"{operand.Displacement.Value}";
                            }
                            else
                            {
                                disp = $"${operand.Displacement.Value:x2}";
                            }
                        }
                        
                        opStr = $"({disp},{operand.AddressRegister},{operand.IndexRegister}.{sz})";
                        break;
                    case Mode.AbsShort:
                        if (operand.Displacement == null)
                        {
                            throw new ArgumentException("Displacement is null");
                        }
                        disp = GetExpression(assemblyAddress, operand.Pos);
                        if (disp == null)
                        {
                            if (operand.Format != null)
                            {
                                disp = string.Format(operand.Format, operand.Displacement.Value);
                            }
                            else
                            {
                                disp = $"{operand.Displacement}";
                            }
                        }
                        opStr = $"({disp}).W";
                        break;
                    case Mode.AbsLong:
                        if (operand.Displacement == null)
                        {
                            throw new ArgumentException("Displacement is null");
                        }
                        disp = GetExpression(assemblyAddress, operand.Pos);
                        if (disp == null)
                        {
                            if (operand.Format != null)
                            {
                                disp = string.Format(operand.Format, operand.Displacement.Value);
                            }
                            else
                            {
                                disp = $"{operand.Displacement}";
                            }
                        }
                        opStr = $"({disp}).L";
                        break;
                    case Mode.PCDisp:
                        if (operand.PC == null)
                        {
                            throw new ArgumentException("PC is null");
                        }
                        if (operand.Displacement == null)
                        {
                            throw new ArgumentException("Displacement is null");
                        }
                        disp = GetExpression(assemblyAddress, operand.Pos);
                        if (disp == null)
                        {
                            if (operand.Format != null)
                            {
                                disp = string.Format(operand.Format, operand.Displacement.Value);
                            }
                            else
                            {
                                disp = $"{operand.Displacement}";
                            }
                        }
                        opStr = disp;
                        break;
                    case Mode.PCIndex:
                        if (operand.PC == null)
                        {
                            throw new ArgumentException("PC is null");
                        }
                        if (operand.IndexRegister == null)
                        {
                            throw new ArgumentException("IndexRegister is null");
                        }
                        if (operand.Displacement == null)
                        {
                            throw new ArgumentException("Displacement is null");
                        }
                        disp = GetExpression(assemblyAddress, operand.Pos);
                        if (disp == null)
                        {
                            if (operand.Format != null)
                            {
                                disp = string.Format(operand.Format, operand.Displacement.Value);
                            }
                            else if (operand.Displacement.Value <= 100)
                            {
                                disp = $"{operand.Displacement.Value}";
                            }
                            else
                            {
                                disp = $"${operand.Displacement.Value:x2}";
                            }
                        }
                        opStr = $"({disp},{operand.PC.Name},{operand.IndexRegister}.W)";
                        break;
                    case Mode.Immediate:
                        if (operand.Data == null)
                        {
                            throw new ArgumentException("Data is null");
                        }

                        disp = GetExpression(assemblyAddress, operand.Pos);
                        if (disp == null)
                        {
                            if (operand.Format != null)
                            {
                                disp = string.Format(operand.Format, operand.Data.Value);
                            }
                            else
                            {
                                disp = $"{operand.Data}";
                            }
                        }
                        opStr = disp;
                        break;
                    case Mode.RegList:
                        if (operand.RegisterList == null)
                        {
                            throw new ArgumentException("RegisterList is null");
                        }
                        opStr = $"{operand.RegisterList}";
                        break;
                    case Mode.Quick:
                        if (operand.QuickData == null)
                        {
                            throw new ArgumentException("QuickData is null");
                        }
                        disp = GetExpression(assemblyAddress, operand.Pos);
                        if (disp == null)
                        {
                            if (operand.Format != null)
                            {
                                disp = string.Format(operand.Format, operand.QuickData.Value);
                            }
                            else
                            {
                                disp = $"{operand.QuickData}";
                            }
                        }
                        opStr = disp;
                        break;
                    case Mode.Label:
                        if (operand.Label == null)
                        {
                            throw new ArgumentException("Label is null");
                        }
                        disp = GetExpression(assemblyAddress, operand.Pos) ?? GetLabelName(operand.Label.Address);
                        if (disp == null)
                        {
                            if (operand.Format != null)
                            {
                                disp = string.Format(operand.Format, operand.Label.Address);
                            }
                            else
                            {
                                disp = $"{operand.Label}";
                            }
                        }
                        opStr = disp;
                        break;
                    default:
                        throw new ArgumentException($"{operand.Mode:x2} is not a valid operande mode");
                }
                return opStr;
            }

            /// <summary>
            /// Evaluate the specified effective effectiveAddress (EA).
            /// </summary>
            /// <param name="instruction">The <see cref="Instruction"/> instance.</param>
            /// <param name="eaType">The type of effective effectiveAddress to be evaluated (Source or Destination).</param>
            /// <returns>isMemory = true if effective effectiveAddress is memory, effective effectiveAddress as an assembler string</returns>
            protected (bool isMemory, OpSize? size, string eaStr, Operand operand) ComputeEffectiveAddress(Instruction instruction, EAType eaType, int? operandPos = null, string? format = null)
            {
                ushort? ea = eaType == EAType.Source ? instruction.SourceAddrMode : instruction.DestAddrMode;
                ushort? ext1 = eaType == EAType.Source ? instruction.SourceExtWord1 : instruction.DestExtWord1;
                ushort? ext2 = eaType == EAType.Source ? instruction.SourceExtWord2 : instruction.DestExtWord2;

                uint? address;
                uint? immVal;
                bool isMemory = true;
                OpSize? size = null;
                string eaStr = "";
                int pos = 0;
                if (eaType == EAType.Destination)
                {
                    pos = 1;
                }
                Operand? operand = null;
                if (operandPos != null)
                {
                    pos = operandPos.Value;
                }
                if (ea.HasValue)
                {
                    OpSize opSize = instruction.Size ?? OpSize.Word;

                    // Get register number (for addressing modes that use a register)
                    ushort regNum = (ushort)(ea & 0x0007);
                    switch (ea & 0x0038)
                    {
                        case (byte)AddrMode.DataRegister:
                            eaStr = FormatOperand(InstructionAddress, operand = new(DataRegisters[regNum], pos, opSize));
                            isMemory = false;
                            break;
                        case (byte)AddrMode.AddressRegister:
                            eaStr = FormatOperand(InstructionAddress, operand = new(AddressRegisters[regNum], Mode.AddressRegister, pos));
                            isMemory = false;
                            break;
                        case (byte)AddrMode.Address:
                            eaStr = FormatOperand(InstructionAddress, operand = new(AddressRegisters[regNum], Mode.Address, pos));
                            break;
                        case (byte)AddrMode.AddressPostInc:
                            eaStr = FormatOperand(InstructionAddress, operand = new(AddressRegisters[regNum], Mode.AddressPostInc, pos));
                            break;
                        case (byte)AddrMode.AddressPreDec:
                            eaStr = FormatOperand(InstructionAddress, operand = new(AddressRegisters[regNum], Mode.AddressPreDec, pos));
                            break;
                        case (byte)AddrMode.AddressDisp:
                            //Debug.Assert(ext1.HasValue, "Required extension word is not available");
                            if (ext1.HasValue)
                            {
                                short eaVal = (short)ext1.Value;
                                if (eaVal < 0)
                                {
                                    // HACK: Some external assemblers (e.g., VASM) can't handle negative values efficiently -
                                    // they sign extend them and thus generate different code from what was disassembled here.
                                    eaStr = FormatOperand(InstructionAddress, operand = new(AddressRegisters[regNum], Mode.AddressDisp, eaVal, pos, format));
                                }
                                else
                                {
                                    eaStr = FormatOperand(InstructionAddress, operand = new(AddressRegisters[regNum], Mode.AddressDisp, eaVal, pos, format));
                                }
                            }
                            break;
                        case (byte)AddrMode.AddressIndex:
                            //Debug.Assert(ext1.HasValue, "Required extension word is not available");
                            if (ext1.HasValue)
                            {
                                byte disp = (byte)(ext1.Value & 0x00FF);
                                byte indexRegNum = (byte)((ext1.Value & 0x7000) >> 12);
                                OpSize sz = (ext1.Value & 0x0800) == 0 ? OpSize.Word : OpSize.Long;
                                eaStr = FormatOperand(InstructionAddress, operand = new(AddressRegisters[regNum], DataRegisters[indexRegNum], sz, (sbyte)disp, pos, null, format));
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
                                        eaStr = FormatOperand(InstructionAddress, operand = new(new Label(address.Value), pos, format));
                                    }
                                    break;
                                case (byte)AddrMode.AbsLong:
                                    //Debug.Assert(ext1.HasValue && ext2.HasValue, "Required extension word is not available");
                                    if (ext1.HasValue && ext2.HasValue)
                                    {
                                        address = (uint)((ext1.Value << 16) + ext2.Value);
                                        eaStr = FormatOperand(InstructionAddress, operand = new(new Label(address.Value), pos, format));
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
                                        eaStr = FormatOperand(InstructionAddress, operand = new(new Label(address.Value), pos, format));
                                    }
                                    break;
                                case (byte)AddrMode.PCIndex:
                                    //Debug.Assert(ext1.HasValue, "Required extension word is not available");
                                    if (ext1.HasValue)
                                    {
                                        byte disp = (byte)(ext1.Value & 0x00FF);
                                        byte indexRegNum = (byte)((ext1.Value & 0x7000) >> 12);
                                        char sz = (ext1.Value & 0x0800) == 0 ? 'W' : 'L';
                                        // PC has been incremented past the extension word.  The definition of
                                        // PC displacement uses the value of the extension word address as the PC value.
                                        int pcDecrement = 2; // Assume source, PC just after ext1 or dest, PC just after ext1
                                        if (eaType == EAType.Source && instruction.DestExtWord1 != null)
                                        {
                                            pcDecrement += (instruction.DestExtWord2 == null) ? 2 : 4;
                                        }
                                        uint baseAddress = (uint)((sbyte)disp + (int)Machine.CPU.PC - pcDecrement);
                                        eaStr = FormatOperand(InstructionAddress, operand = new(PC, baseAddress, pos, opSize, format));
                                        eaStr = $"{eaStr}(PC,D{indexRegNum}.{sz})";
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
                                                eaStr = FormatOperand(InstructionAddress, operand = new(immVal!.Value, pos, format));
                                                eaStr = $"#{eaStr}";
                                                size = OpSize.Long;
                                            }
                                        }
                                        else if (opSize == OpSize.Word)
                                        {
                                            immVal = ext1.Value;
                                            eaStr = FormatOperand(InstructionAddress, operand = new((ushort)immVal!.Value, pos, format));
                                            eaStr = $"#{eaStr}";
                                        }
                                        else
                                        {
                                            immVal = ext1.Value;
                                            eaStr = FormatOperand(InstructionAddress, operand = new((byte)immVal!.Value, pos, format));
                                            eaStr = $"#{eaStr}";
                                        }
                                    }
                                    isMemory = false;
                                    break;
                            }
                            break;
                    }
                }

                if (isMemory && size != null)
                {
                    string sSize = size switch
                    {
                        OpSize.Byte => ".B",
                        OpSize.Long => ".L",
                        _ => ".W"
                    };
                    eaStr = $"({eaStr}){sSize}";
                }

                if (operand == null)
                {
                    operand = new(pos);
                }
                else
                {
                    operand.IsMemory = isMemory;
                    operand.OperandSize = size;
                    operand.EffectiveAddress = eaStr;
                }
                
                return (isMemory, size, eaStr, operand!);
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
            /// Given the register mask and pre-decrement flag, append the list of registers,
            /// e.g. "D0-D7/A0-A7".
            /// </summary>
            /// <param name="regMask">Register mask</param>
            /// <param name="preDec">Determines which way to read the register mask</param>
            /// <param name="sb"></param>
            protected Operand AppendRegisterList(ushort regMask, bool preDec, int pos, StringBuilder sb)
            {
                Operand op = new(new RegisterList(regMask, preDec), pos);
                sb.Append(FormatOperand(InstructionAddress, op));
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
                op.Operands.Add(AppendEffectiveAddress(inst, EAType.Source, sb, 0));
                return op;
            }

            protected Operation CLR(Instruction inst, StringBuilder sb)
            {
                Operation op = AppendMnemonic(inst, sb);
                op.Size = AppendSizeAndTab(inst, sb);
                op.Operands.Add(AppendEffectiveAddress(inst, EAType.Destination, sb, 0));
                return op;
            }

            protected Operation DST(Instruction inst, StringBuilder sb)
            {
                Operation op = AppendMnemonic(inst, sb);
                op.Size = AppendSizeAndTab(inst, sb);
                op.Operands.Add(AppendEffectiveAddress(inst, EAType.Destination, sb, 0));
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
                    op.Operands.Add(new((byte)value, 0));
                    sb.Append($"#{FormatOperand(InstructionAddress, op.Operands[0])},CCR");
                    op.Operands.Add(new(CCR, Mode.Immediate, 1));
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
                    op.Operands.Add(new(value, 0));
                    op.Operands.Add(new(SR, Mode.Immediate, 1));
                    sb.Append($"#{FormatOperand(InstructionAddress, op.Operands[0])},SR");
                }
                return op;
            }

            protected Operation MOVEtoSR(Instruction inst, StringBuilder sb)
            {
                Operation op = new(InstructionAddress, "MOVE");
                sb.Append("MOVE");
                sb.AppendTab(EAColumn);

                op.Operands.Add(AppendEffectiveAddress(inst, EAType.Source, sb, 0));
                op.Operands.Add(new(SR, Mode.RegisterDirect, 1));
                sb.Append(",SR");
                return op;
            }

            protected Operation MOVEtoCCR(Instruction inst, StringBuilder sb)
            {
                Operation op = new(InstructionAddress, "MOVE");
                sb.Append("MOVE");
                sb.AppendTab(EAColumn);

                op.Operands.Add(AppendEffectiveAddress(inst, EAType.Source, sb, 0));
                op.Operands.Add(new(CCR, Mode.RegisterDirect, 1));
                sb.Append(",CCR");
                return op;
            }

            protected Operation MOVEfromSR(Instruction inst, StringBuilder sb)
            {
                Operation op = new(InstructionAddress, "MOVE");
                sb.Append("MOVE");
                sb.AppendTab(EAColumn);

                sb.Append("SR,");
                op.Operands.Add(new(SR, Mode.RegisterDirect, 0));
                op.Operands.Add(AppendEffectiveAddress(inst, EAType.Destination, sb, 1));
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
                        OpSize.Byte => new Operand((byte)value, 0),
                        OpSize.Word => new Operand((short)value, 0),
                        OpSize.Long => new Operand((uint)value, 0),
                        _ => new Operand((uint)0xDEADBEEF, 0)
                    };
                    sb.Append($"#{FormatOperand(InstructionAddress, operand)}");
                    op.Operands.Add(operand);
                    sb.Append(',');
                    op.Operands.Add(AppendEffectiveAddress(inst, EAType.Destination, sb, 1));
                }
                return op;
            }

            protected Operation MULS_MULU_DIVU_DIVS(Instruction inst, StringBuilder sb)
            {
                Operation op = AppendMnemonic(inst, sb);
                op.Size = OpSize.Word;
                sb.Append(".W");
                sb.AppendTab(EAColumn);

                op.Operands.Add(AppendEffectiveAddress(inst, EAType.Source, sb, 0));
                sb.Append(',');
                int dRegNum = (inst.Opcode & 0x0E00) >> 9;
                op.Operands.Add(AppendDataRegister(dRegNum, sb, 1));
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
                    op.Operands.Add(new(AddressRegisters[srcReg], Mode.AddressPreDec, 0));
                    op.Operands.Add(new(AddressRegisters[dstReg], Mode.AddressPreDec, 1));
                    sb.Append($"-(A{srcReg}),-(A{dstReg})");
                }
                else
                {
                    op.Operands.Add(AppendDataRegister(srcReg, sb, 0));
                    sb.Append(',');
                    op.Operands.Add(AppendDataRegister(dstReg, sb, 1));
                }
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
                    op.Operands.Add(AppendEffectiveAddress(inst, EAType.Destination, sb, 0));
                    sb.Append(',');
                    op.Operands.Add(AppendDataRegister(dRegNum, sb, 1));
                }
                else
                {
                    op.Operands.Add(AppendDataRegister(dRegNum, sb, 0));
                    sb.Append(',');
                    op.Operands.Add(AppendEffectiveAddress(inst, EAType.Destination, sb, 1));
                }
                return op;
            }

            protected Operation CMPM(Instruction inst, StringBuilder sb)
            {
                Operation op = AppendMnemonic(inst, sb);
                op.Size = AppendSizeAndTab(inst, sb);

                byte aDstRegNum = (byte)((inst.Opcode & 0x0E00) >> 9);
                byte aSrcRegNum = (byte)(inst.Opcode & 0x0007);
                op.Operands.Add(new(AddressRegisters[aSrcRegNum], Mode.AddressPostInc, 0));
                op.Operands.Add(new(AddressRegisters[aDstRegNum], Mode.AddressPostInc, 1));
                sb.Append("(A");
                sb.Append(aSrcRegNum);
                sb.Append(")+,(A");
                sb.Append(aDstRegNum);
                sb.Append(")+");

                return op;
            }

            protected Operation MOVE(Instruction inst, StringBuilder sb)
            {
                Operation op = AppendMnemonic(inst, sb);
                op.Size = AppendSizeAndTab(inst, sb);
                op.Operands.Add(AppendEffectiveAddress(inst, EAType.Source, sb, 0));
                sb.AppendComma();
                op.Operands.Add(AppendEffectiveAddress(inst, EAType.Destination, sb, 1));

                return op;
            }

            protected Operation MOVEA(Instruction inst, StringBuilder sb)
            {
                Operation op = AppendMnemonic(inst, sb);
                op.Size = AppendSizeAndTab(inst, sb);

                op.Operands.Add(AppendEffectiveAddress(inst, EAType.Source, sb, 0));
                sb.AppendComma();

                int regNum = (inst.Opcode & 0x0E00) >> 9;
                op.Operands.Add(new(AddressRegisters[regNum], Mode.Address, 1));
                sb.Append($"{AddressReg(regNum)}");

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
                        op.Operands.Add(new Operand(AddressRegisters[aRegNum], Mode.AddressDisp, (short)disp, 0));
                        sb.Append(FormatOperand(InstructionAddress, op.Operands[0]));
                        sb.AppendComma();
                        op.Operands.Add(AppendDataRegister(dRegNum, sb, 1));
                        //sb.Append($"{srcExpression},D{dRegNum}");
                    }
                    else
                    {
                        op.Operands.Add(AppendDataRegister(dRegNum, sb, 1));
                        sb.AppendComma();
                        op.Operands.Add(new Operand(AddressRegisters[aRegNum], Mode.AddressDisp, (short)disp, 1));
                        sb.Append(FormatOperand(InstructionAddress, op.Operands[1]));
                    }
                }
                else
                {
                    sb.Append("[SourceExtWord1 missing]");
                }

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
                            op.Operands.Add(AppendRegisterList(regMask, true, 0, sb));
                        }
                        else
                        {
                            op.Operands.Add(AppendRegisterList(regMask, false, 0, sb));
                        }
                        sb.AppendComma();
                        op.Operands.Add(AppendEffectiveAddress(inst, EAType.Destination, sb, 1));
                    }
                    else
                    {
                        // Source is mem, dest is reg (but EA is in dest field)
                        op.Operands.Add(AppendEffectiveAddress(inst, EAType.Destination, sb, 0));
                        sb.AppendComma();
                        op.Operands.Add(AppendRegisterList(regMask, false, 1, sb));
                    }
                }

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
                op.Operands.Add(new Operand(new QuickData(data), 0));
                string? srcExpression = FormatOperand(InstructionAddress, op.Operands[0]);
                sb.Append($"#{srcExpression},");
                op.Operands.Add(AppendDataRegister(dRegNum, sb, 1));

                return op;
            }

            protected Operation ADDQ_SUBQ(Instruction inst, StringBuilder sb)
            {
                Operation op = AppendMnemonic(inst, sb);

                int addVal = (inst.Opcode & 0x0E00) >> 9;
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
                op.Operands.Add(new Operand(new QuickData(addVal), 0));
                string? srcExpression = FormatOperand(InstructionAddress, op.Operands[0]);

                // When being applied to an effectiveAddress register, we work with the entire 32-bit value regardless
                // of the size that has been specified. This operation also doesn't affect the flags.
                if ((inst.Opcode & 0x0038) == (int)AddrMode.AddressRegister)
                {
                    int regNum = inst.Opcode & 0x0007;
                    op.Size = opSize;
                    sb.Append(sz);
                    sb.AppendTab(EAColumn);
                    sb.Append($"#{srcExpression},");
                    op.Operands.Add(new(AddressRegisters[regNum], Mode.Address, 1));
                    sb.Append($"{AddressReg(regNum)}");
                }
                else
                {
                    sb.Append(sz);
                    op.Size = opSize;
                    sb.AppendTab(EAColumn);
                    sb.Append($"#{srcExpression},");
                    op.Operands.Add(AppendEffectiveAddress(inst, EAType.Destination, sb, 1));
                }

                return op;
            }

            protected Operation LINK(Instruction inst, StringBuilder sb)
            {
                Operation op = AppendMnemonic(inst, sb);
                sb.AppendTab(EAColumn);

                if (inst.SourceExtWord1.HasValue)
                {
                    byte regNum = (byte)(inst.Opcode & 0x0007);
                    op.Operands.Add(new(AddressRegisters[regNum], Mode.Address, 0));
                    int disp = Helpers.SignExtendValue((uint)inst.SourceExtWord1, OpSize.Word);
                    op.Operands.Add(new Operand(disp, 1));
                    string dst = FormatOperand(InstructionAddress, op.Operands[1]);
                    sb.Append($"{AddressReg(regNum)},#{dst}");
                }

                return op;
            }

            protected Operation UNLK(Instruction inst, StringBuilder sb)
            {
                Operation op = AppendMnemonic(inst, sb);
                sb.AppendTab(EAColumn);

                byte regNum = (byte)(inst.Opcode & 0x0007);
                op.Operands.Add(new(AddressRegisters[regNum], Mode.Address, 0));
                sb.Append(op.Operands[0]);

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
                op.Operands.Add(new Operand(new Label(address), 0));
                sb.Append(FormatOperand(InstructionAddress, op.Operands[0]));

                return op;
            }

            protected Operation JMP_JSR(Instruction inst, StringBuilder sb)
            {
                Operation op = AppendMnemonic(inst, sb);
                sb.AppendTab(EAColumn);
                op.Operands.Add(AppendEffectiveAddress(inst, EAType.Source, sb, 0));

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
                op.Operands.Add(new Operand(new Label(address), 0));
                sb.Append(FormatOperand(InstructionAddress, op.Operands[0]));

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
                    op.Operands.Add(AppendDataRegister(dRegNum, sb, 0));
                    sb.AppendComma();
                    op.Operands.Add(new(new Label(address), 1));
                    sb.Append(FormatOperand(InstructionAddress, op.Operands[1]));
                    //sb.Append($"D{dRegNum},{dst}");
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
                op.Operands.Add(AppendEffectiveAddress(inst, EAType.Destination, sb, 0));

                return op;
            }

            protected Operation ADDA_SUBA_CMPA(Instruction inst, StringBuilder sb)
            {
                Operation op = AppendMnemonic(inst, sb);
                AppendSizeAndTab(inst, sb);

                op.Operands.Add(AppendEffectiveAddress(inst, EAType.Source, sb, 0));
                sb.AppendComma();
                int regNum = (inst.Opcode & 0x0E00) >> 9;
                op.Operands.Add(new(AddressRegisters[regNum], Mode.Address, 1));
                sb.Append($"{AddressReg(regNum)}");

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
                    op.Operands.Add(new((byte)bitNum, 0, "{0}"));
                    sb.Append($"#{FormatOperand(InstructionAddress, op.Operands[0])}");
                }
                else if (regNum.HasValue)
                {
                    op.Operands.Add(AppendDataRegister(regNum.Value, sb, 0));
                    //sb.Append($"D{regNum}");
                }
                sb.AppendComma();
                op.Operands.Add(AppendEffectiveAddress(inst, EAType.Destination, sb, 1));

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
                    op.Operands.Add(AppendEffectiveAddress(inst, EAType.Source, sb, 0));
                }
                else
                {
                    byte dRegNum = (byte)(inst.Opcode & 0x0007);

                    // Determine if a data register holds the shift amount.
                    bool dRegShift = (inst.Opcode & 0x0020) != 0;
                    int shift = (inst.Opcode & 0x0E00) >> 9;
                    if (dRegShift)
                    {
                        op.Operands.Add(AppendDataRegister(shift, sb, 0));
                        // The shift value holds the number of the data register that holds the number of bits to shift by.
                        //sb.Append($"D{shift}");
                    }
                    else
                    {
                        int shiftAmt = shift != 0 ? shift : 8;
                        op.Operands.Add(new((sbyte)shiftAmt, 0, "{0}"));
                        sb.Append($"#{FormatOperand(InstructionAddress, op.Operands[0])}");
                    }
                    sb.AppendComma();
                    op.Operands.Add(AppendDataRegister(dRegNum, sb, 1));
                    //sb.Append($"D{dRegNum}");
                }
                return op;
            }

            protected Operation LEA(Instruction inst, StringBuilder sb)
            {
                Operation op = AppendMnemonic(inst, sb);
                (_, OpSize? size, string effectiveAddress, Operand src) = ComputeEffectiveAddress(inst, EAType.Source);
                if (size != null)
                {
                    src.OperandSize = size;
                    AppendSizeAndTab(size, sb);
                }
                else
                {
                    sb.AppendTab(EAColumn);
                }
                if (src != null)
                {
                    op.Operands.Add(src);
                    src.EffectiveAddress = effectiveAddress;
                }
                sb.Append(effectiveAddress);
                int regNum = (inst.Opcode & 0x0E00) >> 9;
                sb.AppendComma();
                op.Operands.Add(new(AddressRegisters[regNum], Mode.AddressRegister, 1));
                sb.Append($"{AddressReg(regNum)}");

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
                op.Operands.Add(AppendDataRegister(regNum, sb, 0));
                //sb.Append($"D{regNum}");

                return op;
            }

            protected Operation SWAP(Instruction inst, StringBuilder sb)
            {
                Operation op = AppendMnemonic(inst, sb);
                sb.AppendTab(EAColumn);

                byte regNum = (byte)(inst.Opcode & 0x0007);
                op.Operands.Add(AppendDataRegister(regNum, sb, 0));
                //sb.Append($"D{regNum}");

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
                    op.Operands.Add(new(AddressRegisters[regNum], Mode.Address, 0));
                    op.Operands.Add(new(USP, Mode.Address, 1));
                    sb.Append($"{AddressReg(regNum)},USP");
                }
                else
                {
                    op.Operands.Add(new(USP, Mode.Address, 0));
                    op.Operands.Add(new(AddressRegisters[regNum], Mode.Address, 1));
                    sb.Append($"USP,{AddressReg(regNum)}");
                }
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
                    op.Operands.Add(AppendDataRegister(rSrc, sb, 0));
                    sb.AppendComma();
                    op.Operands.Add(AppendDataRegister(rDest, sb, 1));
                    //sb.Append($"D{rSrc},D{rDest}");
                }
                else
                {
                    // Working with memory addresses, so predecrement both effectiveAddress registers by 1 byte.
                    op.Operands.Add(new(AddressRegisters[rSrc], Mode.AddressPreDec, 0));
                    op.Operands.Add(new(AddressRegisters[rDest], Mode.AddressPreDec, 1));
                    sb.Append($"-({AddressReg(rSrc)}),-({AddressReg(rDest)})");
                }
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
                        op.Operands.Add(AppendDataRegister(rY, sb, 0));
                        sb.Append(',');
                        op.Operands.Add(AppendDataRegister(rX, sb, 1));
                        //sb.Append($"D{rY},D{rX}");
                        break;
                    case 0x09:      // Address Register <-> Address Register
                        op.Operands.Add(new(AddressRegisters[rY], Mode.Address, 0));
                        sb.Append(op.Operands[0]);
                        sb.Append(',');
                        op.Operands.Add(new(AddressRegisters[rX], Mode.Address, 1));
                        sb.Append(op.Operands[1]);
                        //sb.Append($"{AddressReg(rY)},{AddressReg(rX)}");
                        break;
                    case 0x11:      // Data Register <-> Address Register
                        op.Operands.Add(AppendDataRegister(rY, sb, 0));
                        sb.Append(',');
                        op.Operands.Add(new(AddressRegisters[rX], Mode.Address, 1));
                        sb.Append(op.Operands[1]);
                        //sb.Append($"D{rY},{AddressReg(rX)}");
                        break;
                    default:
                        //Debug.Assert(false, "Invalid operating mode for EXG instruction.");
                        break;
                }
                return op;
            }

            protected Operation STOP(Instruction inst, StringBuilder sb)
            {
                Operation op = AppendMnemonic(inst, sb);
                sb.AppendTab(EAColumn);

                var data = inst.SourceExtWord1;
                if (data.HasValue)
                {
                    op.Operands.Add(new Operand(data.Value, 0));
                    sb.Append($"#{FormatOperand(InstructionAddress, op.Operands[0])}");
                }
                return op;
            }

            protected Operation TRAP(Instruction inst, StringBuilder sb)
            {
                Operation op = AppendMnemonic(inst, sb);
                sb.AppendTab(EAColumn);

                ushort vector = (ushort)(inst.Opcode & 0x000F);
                op.Operands.Add(new Operand(vector, 0));
                sb.Append($"#{FormatOperand(InstructionAddress, op.Operands[0] )}");
                return op;
            }

            protected Operation CHK(Instruction inst, StringBuilder sb)
            {
                Operation op = AppendMnemonic(inst, sb);
                op.Size = AppendSizeAndTab(inst, sb);

                op.Operands.Add(AppendEffectiveAddress(inst, EAType.Source, sb, 0));
                sb.Append(',');
                int regNum = (inst.Opcode & 0x0E00) >> 9;
                op.Operands.Add(AppendDataRegister(regNum, sb, 1));
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
                    op.Operands.Add(AppendDataRegister(rX, sb, 0));
                    sb.AppendComma();
                    op.Operands.Add(AppendDataRegister(rY, sb, 1));
                    //sb.Append($"D{rX},D{rY}");
                }
                else
                {
                    op.Operands.Add(new(AddressRegisters[rX], Mode.AddressPreDec, 0));
                    op.Operands.Add(new(AddressRegisters[rY], Mode.AddressPreDec, 1));
                    sb.Append($"-({AddressReg(rX)}),-({AddressReg(rY)})");
                }

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
                op.Operands.Add(new((ushort)(inst.Opcode & 0x0fff), 0, "{0:x3}"));
                sb.Append($"${(ushort)(inst.Opcode & 0x0fff):x3}");
                return op;
            }

            //////////////////////////////////////////////////////////////////////////
            // Support for documentation
            //////////////////////////////////////////////////////////////////////////

            public class ParsedAssembly
            {
                public ParsedAssembly(Operation operation, string assembly, string? comment = null)
                {
                    Assembly = assembly;
                    Comment = comment;
                    _directiveOrOperation = operation;
                }

                public ParsedAssembly(Directive directive, string assembly, string? comment = null)
                {
                    Assembly = assembly;
                    Comment = comment;
                    _directiveOrOperation = directive;
                }

                public uint Address => _directiveOrOperation.Address;
                public string Assembly { get; private set; }
                public string? Comment { get; set; }

                protected DirectiveOrOperation _directiveOrOperation;
                public List<Operand>? Operands => _directiveOrOperation.Operands;
            }

            public class ParsedDirective : ParsedAssembly
            {
                public ParsedDirective(Directive directive, string assembly, string? comment = null) : base(directive, assembly, comment)
                {
                }

                public Directive Dir => (Directive)_directiveOrOperation;
            }

            public class ParsedOperation : ParsedAssembly
            {
                public ParsedOperation(Operation operation, string assembly, string? comment = null) : base(operation, assembly, comment)
                {
                }

                public Operation Op => (Operation)_directiveOrOperation;
            }

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

            public class Displacement : ImmediateData
            {
                public Displacement(uint value) : base(value) { }
                public Displacement(ushort value) : base(value) { }
                public Displacement(byte value) : base(value) { }
                public Displacement(int value) : base(value) { }
                public Displacement(short value) : base(value) { }
                public Displacement(sbyte value) : base(value) { }

                public static new Displacement Make(uint value) => new(value);
                public static new Displacement Make(ushort value) => new(value);
                public static new Displacement Make(byte value) => new(value);
                public static new Displacement Make(int value) => new(value);
                public static new Displacement Make(short value) => new(value);
                public static new Displacement Make(sbyte value) => new(value);
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

            public class QuickData : ImmediateData
            {
                public QuickData(uint value) : base(value) { }
                public QuickData(ushort value) : base(value) { }
                public QuickData(byte value) : base(value) { }
                public QuickData(int value) : base(value) { }
                public QuickData(short value) : base(value) { }
                public QuickData(sbyte value) : base(value) { }
                public static new QuickData Make(uint value) => new(value);
                public static new QuickData Make(ushort value) => new(value);
                public static new QuickData Make(byte value) => new(value);
                public static new QuickData Make(int value) => new(value);
                public static new QuickData Make(short value) => new(value);
                public static new QuickData Make(sbyte value) => new(value);

                public override string ToString()
                {
                    return Value.ToString();
                }
            }

            public class Operand
            {
                public Operand(int pos)
                {
                    Mode = Mode.Illegal;
                    Pos = pos;
                }

                /// <summary>
                /// Because an operand with only an address register can be direct, indirect,
                /// indirect with predecrement or indirect with postincrement, the mode
                /// must be specified.
                /// </summary>
                /// <param name="addressRegister"></param>
                /// <param name="eaType"></param>
                /// <param name="mode"></param>
                public Operand(AddressRegister addressRegister, Mode mode, int pos)
                {
                    if (mode != Mode.AddressRegister && mode != Mode.Address && mode != Mode.AddressPostInc && mode != Mode.AddressPreDec)
                    {
                        throw new ArgumentException("Address register mode must be direct, indirect, indirect with predecrement or indirect with postincrement.");
                    }
                    Mode = mode;
                    AddressRegister = addressRegister;
                    Pos = pos;
                }

                public Operand(AddressRegister addressRegister, Mode mode, ushort displacement, int pos, string? format = null)
                {
                    if (mode != Mode.AddressDisp) throw new ArgumentException("Bad mode");
                    Mode = Mode.AddressDisp;
                    AddressRegister = addressRegister;
                    Displacement = new Displacement(displacement);
                    Pos = pos;
                    Format = format;
                }

                public Operand(AddressRegister addressRegister, Mode mode, short displacement, int pos, string? format = null)
                {
                    if (mode != Mode.AddressDisp) throw new ArgumentException("Bad mode");
                    Mode = Mode.AddressDisp;
                    AddressRegister = addressRegister;
                    Displacement = new Displacement(displacement);
                    Pos = pos;
                    Format = format;
                }

                public Operand(DataRegister dataRegister, int pos, OpSize? opSize = null, string? format = null)
                {
                    Mode = Mode.DataRegister;
                    DataRegister = dataRegister;
                    Pos = pos;
                    OperandSize = opSize;
                    Format = format;
                }

                public Operand(uint data, int pos, string? format = null)
                {
                    Mode = Mode.Immediate;
                    Data = new ImmediateData(data);
                    Pos = pos;
                    OperandSize = OpSize.Long;
                    Format = format;
                }

                public Operand(int data, int pos, string? format = null)
                {
                    Mode = Mode.Immediate;
                    Data = new ImmediateData(data);
                    Pos = pos;
                    OperandSize = OpSize.Long;
                    Format = format;
                }

                public Operand(ushort data, int pos, string? format = null)
                {
                    Mode = Mode.Immediate;
                    Data = new ImmediateData(data);
                    Pos = pos;
                    OperandSize = OpSize.Word;
                    Format = format;
                }

                public Operand(short data, int pos, string? format = null)
                {
                    Mode = Mode.Immediate;
                    Data = new ImmediateData(data);
                    Pos = pos;
                    OperandSize = OpSize.Word;
                    Format = format;
                }

                public Operand(sbyte data, int pos, string? format = null)
                {
                    Mode = Mode.Immediate;
                    Data = new ImmediateData(data);
                    Pos = pos;
                    OperandSize = OpSize.Byte;
                    Format = format;
                }

                public Operand(byte data, int pos, string? format = null)
                {
                    Mode = Mode.Immediate;
                    Data = new ImmediateData(data);
                    Pos = pos;
                    OperandSize = OpSize.Byte;
                    Format = format;
                }

                public Operand(RegisterList regList, int pos, OpSize? opSize = null)
                {
                    Mode = Mode.RegList;
                    RegisterList = regList;
                    Pos = pos;
                    OperandSize = opSize;
                }

                public Operand(Label label, int pos, string? format = null)
                {
                    Mode = Mode.Label;
                    Label = label;
                    Pos = pos;
                    Format = format;
                }

                public Operand(ProgramCounter pc, uint address, int pos, OpSize? opSize, string? format = null)
                {
                    Mode = Mode.PCDisp;
                    PC = pc;
                    Displacement = new Displacement(address);
                    Pos = pos;
                    OperandSize = opSize;
                    Format = format;
                }

                public Operand(ProgramCounter pc, DataRegister dataRegister, uint address, int pos, OpSize? opSize, string? format = null)
                {
                    Mode = Mode.PCIndex;
                    PC = pc;
                    DataRegister = dataRegister;
                    Displacement = new Displacement(address);
                    Pos = pos;
                    OperandSize = opSize;
                    Format = format;
                }

                public Operand(AddressRegister addressRegster, DataRegister indexRegister, OpSize indexSize, sbyte displacement, int pos, OpSize? opSize = null, string? format = null)
                {
                    Mode = Mode.AddressIndex;
                    AddressRegister = addressRegster;
                    IndexRegister = indexRegister;
                    Displacement = new Displacement(displacement);
                    Pos = pos;
                    OperandSize = opSize;
                    IndexSize = indexSize;
                    Format = format;
                }

                public Operand(QuickData quickData, int pos, string? format = null)
                {
                    Mode = Mode.Quick;
                    QuickData = quickData;
                    Pos = pos;
                    Format = format;
                }

                public Operand(ConditionCodeRegister ccr, Mode mode, int pos, string? format = null)
                {
                    Mode = mode;
                    CCR = ccr;
                    Pos = pos;
                    Format = format;
                }
                public Operand(StatusRegister sr, Mode mode, int pos, string? format = null)
                {
                    Mode = mode;
                    SR = sr;
                    Pos = pos;
                    Format = format;
                }

                public bool IsMemory { get; set; } = false;
                public string? EffectiveAddress { get; set; }
                public RegisterList? RegisterList { get; set; }
                public ConditionCodeRegister? CCR { get; set; }
                public StatusRegister? SR { get; set; }
                public ProgramCounter? PC { get; set; }
                public DataRegister? DataRegister { get; set; }
                public DataRegister? IndexRegister { get; set; }
                public Displacement? Displacement { get; set; }
                public AddressRegister? AddressRegister { get; set; }
                public ImmediateData? Data { get; set; }
                public QuickData? QuickData { get; set; }
                public Label? Label { get; set; }
                public OpSize? OperandSize { get; set; }
                public OpSize? IndexSize { get; set; }
                public string? Format { get; set; }
                public int Pos { get; set; }
                public Mode Mode { get; set; }

                public override string? ToString()
                {
                    switch (Mode)
                    {
                        case Mode.DataRegister:       // Dn
                            return DataRegister?.ToString();
                        case Mode.AddressRegister: // An
                            return AddressRegister?.ToString();
                        case Mode.Address:                 // (An)
                            return $"{AddressRegister}";
                        case Mode.AddressPostInc:   // (An)+
                            return $"({AddressRegister})+";
                        case Mode.AddressPreDec:     // -(An)
                            return $"-({AddressRegister})";
                        case Mode.AddressDisp:         // (d16,An)
                            return $"({Displacement},{AddressRegister})";
                        case Mode.AddressIndex:       // (d8,An,Xn)
                            return $"{Displacement},{AddressRegister})";
                        case Mode.AbsShort:            // (xxx).W
                            return $"({Displacement}).W";
                        case Mode.AbsLong:                 // (xxx).L
                            return $"({Displacement}).L";
                        case Mode.PCDisp:                   // (d16,PC)
                            return $"({Displacement},PC)";
                        case Mode.PCIndex:                 // (d8,PC,Xn)
                            return $"{Displacement}(PC,{DataRegister})";
                        case Mode.Immediate:             // #<data>
                            return $"#{Data}";
                        case Mode.RegList:                             // MOVEM An,d0-d7/a0-a7
                            return RegisterList?.ToString();
                        case Mode.Quick:                               // #<data>
                            return $"#{QuickData}";
                        case Mode.Label:                               // <label>
                            return Label?.ToString();
                        case Mode.RegisterDirect:
                            if (CCR != null)
                            {
                                return CCR.ToString();
                            }
                            else if (SR != null){
                                return SR.ToString();
                            }
                            else if (PC != null)
                            {
                                return PC.ToString();
                            }
                            else
                            {
                                return "ILLEGAL REGISTER";
                            }
                        case Mode.Illegal:                                     // Illegal instruction mode
                        default:
                            return "ILLEGAL REGISTER";
                    }
                }
            }

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

                public static ImmediateData Make(uint value) => new(value);
                public static ImmediateData Make(ushort value) => new(value);
                public static ImmediateData Make(byte value) => new(value);

                public static ImmediateData Make(int value) => new(value);
                public static ImmediateData Make(short value) => new(value);
                public static ImmediateData Make(sbyte value) => new(value);

                public OpSize Size { get; private set; }
                public bool Signed { get; private set; }
                public int Value { get; private set; }

                public override string ToString()
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

            public class Label
            {
                public Label(uint address)
                {
                    Address = address;
                }
                public static Label Make(uint address) => new(address);
                public uint Address { get; private set; }

                public override string ToString()
                {
                    return $"${Address:x8}";
                }
            }

            public class Register
            {
                internal Register(string name)
                {
                    Name = name;
                }
                public string Name { get; private set; }

                public override string ToString()
                {
                    return Name;
                }
            }

            public class ProgramCounter : Register
            {
                internal ProgramCounter() : base("PC") { }
            }
            public class StatusRegister : Register
            {
                internal StatusRegister() : base("SR") { }
            }

            public class ConditionCodeRegister
            {
                public ConditionCodeRegister()
                {
                    Name = "CCR";
                }

                public string Name { get; set; }

                public override string ToString()
                {
                    return Name;
                }
            }

            public class DataRegister
            {
                public DataRegister(string name)
                {
                    Name = name;
                }

                public string Name { get; set; }

                public override string ToString()
                {
                    return Name;
                }
            }

            public class AddressRegister
            {
                public AddressRegister(string name)
                {
                    Name = name;
                }

                public string Name { get; set; }

                public override string ToString()
                {
                    return Name;
                }
            }

            public static readonly ConditionCodeRegister CCR = new();
            public static readonly StatusRegister SR = new();
            public static readonly ProgramCounter PC = new();

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

            public AddressRegister USP = AddressRegisters[8];

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

            public class DirectiveOrOperation
            {
                public DirectiveOrOperation(uint address, string name, OpSize? size = null)
                {
                    Address = address;
                    Name = name;
                    Operands = [];
                    Size = size;
                    MachineCode = [];
                    Assembly = "";
                }
                public uint Address { get; private set; }
                public string Name { get; set; }
                public OpSize? Size { get; set; }
                public byte[] MachineCode { get; set; }
                public string Assembly { get; set; }
                public string? Comment { get; set; }
                public List<Operand> Operands { get; set; }
            }

            public class Directive : DirectiveOrOperation
            {
                public Directive(uint address, string name, OpSize? size) : base(address, name, size) { }
            }

            public class Operation : DirectiveOrOperation
            {
                public Operation(uint address, string name, OpSize? size = null) : base(address, name, size) { }
            }

            public static bool IsDirective(string token)
            {
                return DirectiveInfoList.ContainsKey(token);
            }

            public static bool IsOperation(string token)
            {
                return OpInfoList.ContainsKey(token);
            }

            public static readonly Dictionary<string, OperationOrDirectiveInfo> DirectiveInfoList = new(StringComparer.OrdinalIgnoreCase)
            {
                // Assembler Directive Operand Counts

                // Directives

                {"EQU", new(1, 1, false) },     // Equate                     
                {"DC", new(1, 256, true)},      // Define constant                
                {"DS", new(1, 1, true)},        // Define storage        
                {"ORG", new(1, 1, false)},      // Origin                     
                {"END", new(0, 1, false)},      // End - may include label or starting address                       
                {"INCLUDE", new(1, 1, false)},  // Include file               
                {"MACRO", new(0, 256, false)},  // Macro definition           
                {"ENDM", new(0, 0, false)},     // End macro                 
            };

            public class OperationOrDirectiveInfo
            {
                /// <summary>
                /// Create an instances of <see cref="OperationOrDirectiveInfo"/>.
                /// </summary>
                /// <param name="minArgs">Minimum required number of operands</param>
                /// <param name="maxArgs">Maximum allowed number of operands.</param>
                /// <param name="allowsSize">True if size (.B, .W, .L) is allowed</param>
                public OperationOrDirectiveInfo(int minArgs, int? maxArgs = null, bool allowsSize = false)
                {
                    MinArgs = minArgs;
                    MaxArgs = maxArgs ?? minArgs;
                    AllowsSize = allowsSize;
                }

                public int MinArgs { get; init; }
                public int MaxArgs { get; init; }
                public bool AllowsSize { get; init; }
            }

            private static readonly Dictionary<string, OperationOrDirectiveInfo> OpInfoList = new(StringComparer.OrdinalIgnoreCase)
            {
                // Motorola 68000 Instructions and Operand Counts

                // Data Movement Instructions
    
                {"MOVE", new(2, 2, true)},    // Move data                  
                {"MOVEA", new(2, 2, true)},   // Move address               
                {"MOVEQ", new(2, 2, true)},   // Move quick immediate to Dn 
                {"MOVEM", new(2, 2, true)},   // Move multiple registers
                {"MOVEP", new(2, 2, true)},   // Move peripheral data
                {"EXG", new(2, 2, false)},    // Exchange registers
                {"SWAP", new(1, 1, false)},   // Swap register words
                {"PEA", new(1, 1, false)},    // Push effective address
                {"LEA", new(2, 2, false)},    // Load effective address
                {"LINK", new(2, 2, false)},   // Link and allocate
                {"UNLK", new(1, 1, false)},   // Unlink
                {"EXT", new(1, 1, true)},     // Sign extend
            
                // Arithmetic Instructions
            
                {"ADD", new(2, 2, true)},     // Add
                {"ADDA", new(2, 2, true)},    // Add address
                {"ADDI", new(2, 2, true)},    // Add immediate
                {"ADDQ", new(2, 2, true)},    // Add quick immediate
                {"SUB", new(2, 2, true)},     // Subtract
                {"SUBA", new(2, 2, true)},    // Subtract address
                {"SUBI", new(2, 2, true)},    // Subtract immediate
                {"SUBQ", new(2, 2, true)},    // Subtract quick immediate
                {"MULS", new(2, 2, true)},    // Multiply signed
                {"MULU", new(2, 2, true)},    // Multiply unsigned
                {"DIVS", new(2, 2, true)},    // Divide signed
                {"DIVU", new(2, 2, true)},    // Divide unsigned
                {"NEG", new(1, 1, true)},     // Negate
                {"NEGX", new(1, 1, true)},    // Negate with extend
                {"CLR", new(1, 1, true)},     // Clear
                {"CMP", new(2, 2, true)},     // Compare
                {"CMPA", new(2, 2, true)},    // Compare address
                {"CMPI", new(2, 2, true)},    // Compare immediate
                {"CMPM", new(2, 2, true)},    // Compare memory-to-memory
                {"TST", new(1, 1, true)},     // Test
                {"CHK", new(2, 2, true)},     // Check against bounds
            
                // Logical Instructions
            
                {"AND", new(2, 2, true)},     // Logical AND
                {"ANDI", new(2, 2, true)},    // Logical AND immediate
                {"OR", new(2, 2, true)},      // Logical OR
                {"ORI", new(2, 2, true)},     // Logical OR immediate
                {"EOR", new(2, 2, true)},     // Exclusive OR
                {"EORI", new(2, 2, true)},    // Exclusive OR immediate
                {"NOT", new(1, 1, true)},     // Logical NOT
            
                // Shift and Rotate Instructions
                //
                // Shift and rotate instructions can use either immediate
                // values or data registers to specify the shift count.
                // They can have one or two operands.
            
                {"ASL", new(1, 2, true)},     // Arithmetic shift left
                {"ASR", new(1, 2, true)},     // Arithmetic shift right
                {"LSL", new(1, 2, true)},     // Logical shift left
                {"LSR", new(1, 2, true)},     // Logical shift right
                {"ROL", new(1, 2, true)},     // Rotate left
                {"ROR", new(1, 2, true)},     // Rotate right
                {"ROXL", new(1, 2, true)},    // Rotate with extend left
                {"ROXR", new(1, 2, true)},    // Rotate with extend right
            
                // Bit Manipulation Instructions
            
                {"BTST", new(2, 2, true)},    // Test bit
                {"BCLR", new(2, 2, true)},    // Clear bit
                {"BCHG", new(2, 2, true)},    // Complement bit
                {"BSET", new(2, 2, true)},    // Set bit
            
                // Program Control Instructions
            
                {"BSR", new(1, 1, false)},    // Branch to subroutine
                {"BRA", new(1, 1, false)},    // Branch always
                {"BHI", new(1, 1, false)},    // Branch if (Higher)
                {"BLS", new(1, 1, false)},    // Branch if (Lower or same)
                {"BCC", new(1, 1, false)},    // Branch if (Carry clear)
                {"BCS", new(1, 1, false)},    // Branch if (Carry set)
                {"BHS", new(1, 1, false)},    // Branch if (Higher or same)
                {"BLO", new(1, 1, false)},    // Branch if (Lower)
                {"BNE", new(1, 1, false)},    // Branch if (Not equal)
                {"BEQ", new(1, 1, false)},    // Branch if (Equal)
                {"BVC", new(1, 1, false)},    // Branch if (Overflow clear)
                {"BVS", new(1, 1, false)},    // Branch if (Overflow set)
                {"BPL", new(1, 1, false)},    // Branch if (Plus)
                {"BMI", new(1, 1, false)},    // Branch if (Minus)
                {"BGE", new(1, 1, false)},    // Branch if (Greater or equal)
                {"BLT", new(1, 1, false)},    // Branch if (Less than)
                {"BGT", new(1, 1, false)},    // Branch if (Greater than)
                {"BLE", new(1, 1, false)},    // Branch if (Less or equal)
                {"DBRA", new(2, 2, false)},   // Decrement and branch (Always)
                {"DBHI", new(2, 2, false)},   // Decrement and branch if (Higher)
                {"DBLS", new(2, 2, false)},   // Decrement and branch if (Lower or same)
                {"DBCC", new(2, 2, false)},   // Decrement and branch if (Carry clear)
                {"DBCS", new(2, 2, false)},   // Decrement and branch if (Carry set)
                {"DBHS", new(2, 2, false)},   // Decrement and branch if (Higher or same)
                {"DBLO", new(2, 2, false)},   // Decrement and branch if (Lower)
                {"DBNE", new(2, 2, false)},   // Decrement and branch if (Not equal)
                {"DBEQ", new(2, 2, false)},   // Decrement and branch if (Equal)
                {"DBVC", new(2, 2, false)},   // Decrement and branch if (Overflow clear)
                {"DBVS", new(2, 2, false)},   // Decrement and branch if (Overflow set)
                {"DBPL", new(2, 2, false)},   // Decrement and branch if (Plus)
                {"DBMI", new(2, 2, false)},   // Decrement and branch if (Minus)
                {"DBGE", new(2, 2, false)},   // Decrement and branch if (Greater or equal)
                {"DBLT", new(2, 2, false)},   // Decrement and branch if (Less than)
                {"DBGT", new(2, 2, false)},   // Decrement and branch if (Greater than)
                {"DBLE", new(2, 2, false)},   // Decrement and branch if (Less or equal)
                {"JMP", new(1, 1, false)},    // Jump
                {"JSR", new(1, 1, false)},    // Jump to subroutine
                {"RTS", new(0, 0, false)},    // Return from subroutine
                {"RTR", new(0, 0, false)},    // Return and restore condition codes
                {"RTE", new(0, 0, false)},    // Return from exception
                {"TRAP", new(1, 1, false)},   // Trap
                {"TRAPV", new(0, 0, false)},  // Trap on overflow
                {"STOP", new(1, 1, false)},   // Stop processor
                {"RESET", new(0, 0, false)},  // Reset external devices
                {"ILLEGAL", new(0, 0, false)},// Illegal instruction
                {"NOP", new(0, 0, false)},    // No operation
                {"BKPT", new(1, 1, false)},   // Breakpoint
            
                // Miscellaneous Instructions
            
                {"TAS", new(1, 1, true)},     // Test and set
                {"SRA", new(1, 1, true)},     // Set if (Always)
                {"SHI", new(1, 1, true)},     // Set if (Higher)
                {"SLS", new(1, 1, true)},     // Set if (Lower or same)
                {"SCC", new(1, 1, true)},     // Set if (Carry clear)
                {"SCS", new(1, 1, true)},     // Set if (Carry set)
                {"SHS", new(1, 1, true)},     // Set if (Higher or same)
                {"SLO", new(1, 1, true)},     // Set if (Lower)
                {"SNE", new(1, 1, true)},     // Set if (Not equal)
                {"SEQ", new(1, 1, true)},     // Set if (Equal)
                {"SVC", new(1, 1, true)},     // Set if (Overflow clear)
                {"SVS", new(1, 1, true)},     // Set if (Overflow set)
                {"SPL", new(1, 1, true)},     // Set if (Plus)
                {"SMI", new(1, 1, true)},     // Set if (Minus)
                {"SGE", new(1, 1, true)},     // Set if (Greater or equal)
                {"SLT", new(1, 1, true)},     // Set if (Less than)
                {"SGT", new(1, 1, true)},     // Set if (Greater than)
                {"SLE", new(1, 1, true)},     // Set if (Less or equal)
            };


        }
    }

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
        /// Append a comma to the string.  Typically used to separate
        /// operands.
        /// </summary>
        /// <param name="sb"></param>
        public static void AppendComma(this StringBuilder sb)
        {
            sb.Append(',');
        }
    }
}
