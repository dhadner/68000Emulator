using PendleCodeMonkey.MC68000EmulatorLib.Enumerations;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Text.Json.Serialization;

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
                public DisassemblyRecord(bool endOfData, uint address, byte[] machineCode, string assembly, string? comment)
                {
                    EndOfData = endOfData;
                    Address = address;
                    MachineCode = machineCode;
                    Assembly = assembly;
                    Comment = comment;
                }

                /// <summary>
                /// Set to <c>true</c> if the disassembler
                /// ran out of bytes prior to completing disassembly of this instruction
                /// </summary>
                public bool EndOfData { get; private set; }

                /// <summary>
                /// Address of this instruction
                /// </summary>
                public uint Address { get; private set; }

                /// <summary>
                /// Actual instruction bytes
                /// </summary>
                public byte[] MachineCode { get; private set; }

                /// <summary>
                /// Instruction, e.g., "MOVEQ.L #1,D0".  This text
                /// is suitable for round-tripping through the VASM assembler.  Uses
                /// only spaces, no tabs.
                /// </summary>
                public string Assembly { get; private set; }

                /// <summary>
                /// Comments can be provided by subclasses if the <see cref="Disassembler"/>
                /// class overriding the <see cref= "Comment" />
                /// method in that class.                
                /// </summary>
                public string? Comment { get; set; }
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
            /// Number of bytes to allow in one DC.B directive.  A <see cref="NonExecutableSection"/>
            /// is not limited in length and can consist of many non-executable data blocks.
            /// </summary>
            public const int MaxNonExecDataDisassemblyBlockSize = 4;

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
            protected uint CurrentInstructionAddress { get; set; }

            /// <summary>
            /// Gets a value indicating if the disassembly has reached the end of the specified block of memory.
            /// </summary>
            protected bool IsEndOfData => CurrentAddress >= StartAddress + Length;

            /// <summary>
            /// Represents a non-executable section.
            /// </summary>
            /// <param name="address"></param>
            /// <param name="length"></param>
            public class NonExecSection(uint address, uint length)
            {
                public virtual uint Address { get; protected set; } = address;
                public virtual uint Length { get; protected set; } = length;

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

            protected List<NonExecSection> NonExecSections { get; set; } = new();
            protected Dictionary<uint, NonExecSection> NonExecSectionsByAddress { get; set; } = new();

            protected delegate void DisassemblyHandler(Instruction inst, StringBuilder sb);
            protected readonly Dictionary<OpHandlerID, DisassemblyHandler> _handlers = new();
            protected readonly uint[] _bit = new uint[]
                                              { 0x00000001, 0x00000002, 0x00000004, 0x00000008, 0x00000010, 0x00000020, 0x00000040, 0x00000080,
                                                0x00000100, 0x00000200, 0x00000400, 0x00000800, 0x00001000, 0x00002000, 0x00004000, 0x00008000,
                                                0x00010000, 0x00020000, 0x00040000, 0x00080000, 0x00100000, 0x00200000, 0x00400000, 0x00800000,
                                                0x01000000, 0x02000000, 0x04000000, 0x08000000, 0x10000000, 0x20000000, 0x40000000, 0x80000000 };
            protected readonly uint[] _rbit = new uint[]
                                              { 0x80000000, 0x40000000, 0x20000000, 0x10000000, 0x08000000, 0x04000000, 0x02000000, 0x01000000,
                                                0x00800000, 0x00400000, 0x00200000, 0x00100000, 0x00090000, 0x00040000, 0x00020000, 0x00010000,
                                                0x00008000, 0x00004000, 0x00002000, 0x00001000, 0x00000800, 0x00000400, 0x00000200, 0x00000100,
                                                0x00000080, 0x00000040, 0x00000020, 0x00000010, 0x00000008, 0x00000004, 0x00000002, 0x00000001 };
            protected readonly string[] _reg = new string[]
            {
                    "D0","D1","D2","D3","D4","D5","D6","D7",
                    "A0","A1","A2","A3","A4","A5","A6","A7",
            };

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
                _handlers.Add(OpHandlerID.PEA, SRC);
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

                bool combined;

                // Keep cycling up through the list until we haven't combined any sections.
                do
                {
                    combined = false;

                    for (int i = 0; i < sections.Count - 1; i++)
                    {
                        if (sections[i].Address + sections[i].Length > sections[i + 1].Address)
                        {
                            combined = true;

                            // We have an overlap, so combine them.
                            uint minAddress = Math.Min(sections[i].Address, sections[i + 1].Address);
                            uint maxAddress = Math.Max(sections[i].Address + sections[i].Length, sections[i + 1].Address + sections[i + 1].Length);
                            uint length = maxAddress - minAddress;
                            NonExecSection merged = new(minAddress, length);
                            sections[i + 1] = merged;
                            sections.RemoveAt(i);
                            break;
                        }
                    }
                } while (combined);
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
            public void SetNonExecutableRange(uint startAddress, uint length)
            {
                
                NormalizeSections();
                NonExecSections.Add(new(startAddress, length));
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
                        NonExecSections.Add(new(section.Address, startAddress - section.Address));
                        NonExecSections.Add(new(startAddress + length, nesMaxAddress - maxAddress));
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
                        NonExecSections.Add(new(startAddress + length, nesMaxAddress - maxAddress));
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
                        NonExecSections.Add(new(section.Address, startAddress - section.Address));
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

                    List<DisassemblyRecord> result = new();
                    while (!IsEndOfData && count++ < maxCount)
                    {
                        var nonExecSection = GetNonExecutableSection(CurrentAddress);
                        if (nonExecSection != null)
                        {
                            uint len = Math.Min(nonExecSection.Address + nonExecSection.Length - CurrentAddress, MaxNonExecDataDisassemblyBlockSize);
                            len = Math.Min(len, length - (CurrentAddress - startAddress));
                            result.Add(GetNonExecutableSectionRecord(CurrentAddress, len, nonExecSection));
                        }
                        else
                        {
                            (bool endOfData, uint address, byte[] bytes, string assembly, string? comment) = DisassembleAtCurrentAddress();
                            result.Add(new DisassemblyRecord(endOfData, address, bytes, assembly, comment));
                        }
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
                length = Math.Min(length, MaxNonExecDataDisassemblyBlockSize);

                // Length of NES that is contained in this record.
                uint nesRecordLength = section.Length - (address - section.Address);

                uint recordLength = Math.Min(length, nesRecordLength);
                byte[] machineCode = new byte[recordLength];
                for (uint i = 0; i < recordLength; i++)
                {
                    // Can't use ReadNextByte() because NonExecutableDataDisassembly(...)
                    // will call it below and calling it here would result in double
                    // incrementing CurrentAddress.
                    machineCode[i] = Machine.Memory.ReadByte(address + i);
                }
                var assembly = NonExecutableDataDisassembly(length, address);
                string? comment = Comment(address, machineCode, assembly, true);
                var record = new DisassemblyRecord(false, address, machineCode, assembly, comment);
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

            static readonly byte[] _bytes = new byte[MaxNonExecDataDisassemblyBlockSize];
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
            /// Returns a string containing a block of non-executable data.
            /// </summary>
            /// <remarks>
            /// Non-executable data is output in the disassembly using a DC.B directive.
            /// Non-executable data sections are output in blocks of a maximum of 8 bytes (as set 
            /// by MaxNonExecDataDisassemblyBlockSize).
            /// </remarks>
            /// <param name="section">Zero-based index of the non-executable section.</param>
            /// <returns>A string containing the disassembled output for the block of non-executable data.</returns>
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
                AppendTab(EAColumn, sb);
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

            /// <summary>
            /// Instruction mnemonic.
            /// </summary>
            /// <param name="inst"></param>
            /// <returns>mnemonic for disassembly without size info (e.g., no ".W" suffix if word instruction)</returns>
            protected virtual string Mnemonic(Instruction inst)
            {
                return inst.Info.Mnemonic;
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
            /// Disassemble the current instruction.  The address is guaranteed to not be in a non-executable
            /// section.
            /// </summary>
            /// <returns></returns>
            protected (bool endOfData, uint address, byte[] machineCode, string assembly, string? comment) DisassembleAtCurrentAddress()
            {
                try
                {
                    CurrentInstructionAddress = CurrentAddress;
                    Disassembling = true;
                    bool endOfData = false;
                    string assembly = "UNKNOWN";

                    // Decoder fetches the instruction at the current PC, so set it to
                    // where we want to disassembler.
                    Machine.CPU.PC = CurrentAddress;
                    Instruction? inst = Machine.Decoder.FetchInstruction();

                    // PC has been incremented to point to the next instruction after this one.
                    int length = (int)Machine.CPU.PC - (int)CurrentInstructionAddress;

                    List<byte> codeBytes = new();

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
                        endOfData = true;
                    }
                    else if (inst != null)
                    {
                        StringBuilder sb = new();
                        if (_handlers.TryGetValue(inst.Info.HandlerID, out DisassemblyHandler? instructionDisassembler))
                        {
                            instructionDisassembler(inst, sb);
                        }
                        else
                        {
                            sb.Append("????");
                        }
                        assembly = sb.ToString();
                    }
#if REORG
                    // If we previously encroached on a non-executable section, we will
                    // use ORG to reset the PC to the start of the non-executable section.
                    // The next call to disassemble will be to the routine that
                    // handles non-executable sections.
                    if (reOrgInProgress)
                    {
                        StringBuilder orgBuilder = new();
                        orgBuilder.Append("ORG");
                        AppendTab(EAColumn, orgBuilder);
                        orgBuilder.Append($"${reOrgAddress:x8}");
                        CurrentAddress = reOrgAddress;
                        reOrgInProgress = false;
                        reOrgAddress = 0;
                        return (endOfData, CurrentAddress, [], orgBuilder.ToString(), "");
                    }
                    else
#endif
                    {
                        byte[] machineCode = codeBytes.ToArray();
#if REORG
                        NonExecSection? nonExecSection = null;
                        uint nesCheckAddress = CurrentInstructionAddress + 2;
                        while (nonExecSection == null && nesCheckAddress < CurrentAddress)
                        {
                            nonExecSection = GetNonExecutableSection(nesCheckAddress);
                            nesCheckAddress += 2;
                        }
                        if (nonExecSection != null)
                        {
                            reOrgInProgress = true;
                            reOrgAddress = nesCheckAddress - 2;
                        }
#endif
                        return (endOfData, CurrentInstructionAddress, machineCode, assembly, Comment(CurrentInstructionAddress, machineCode, assembly));
                    }
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
            protected void AppendSizeAndTab(Instruction inst, StringBuilder sb)
            {
                OpSize size = inst.Size ?? OpSize.Word;
                AppendSizeAndTab(size, sb);
            }

            /// <summary>
            /// Append the instruction size and tab.
            /// </summary>
            /// <param name="size"></param>
            /// <param name="sb"></param>
            protected static void AppendSizeAndTab(OpSize? size, StringBuilder sb)
            {
                string sSize = size switch
                {
                    OpSize.Byte => ".B",
                    OpSize.Long => ".L",
                    _ => ".W"
                };
                sb.Append(sSize);
                AppendTab(EAColumn, sb);
            }

            /// <summary>
            /// Append a condition code.
            /// </summary>
            /// <param name="cond"></param>
            /// <param name="sb"></param>
            protected static void AppendCondition(Condition cond, StringBuilder sb)
            {
                string sCond = cond.ToString();
                sb.Append(sCond);
            }

            /// <summary>
            /// Append a data register.
            /// </summary>
            /// <param name="regnum"></param>
            /// <param name="sb"></param>
            protected static void AppendDataRegister(int regnum, StringBuilder sb)
            {
                sb.Append('D');
                sb.Append(regnum);
            }

            /// <summary>
            /// Append the effective address.
            /// </summary>
            /// <param name="instruction"></param>
            /// <param name="eaType"></param>
            /// <param name="sb"></param>
            protected void AppendEffectiveAddress(Instruction instruction, EAType eaType, StringBuilder sb)
            {
                (_, _, string effectiveAddress) = ComputeEffectiveAddress(instruction, eaType);
                sb.Append(effectiveAddress);
            }

            /// <summary>
            /// Return true if the effective address is a memory reference.
            /// </summary>
            /// <param name="instruction"></param>
            /// <param name="eaType"></param>
            /// <returns></returns>
            protected bool EffectiveAddressIsMemory(Instruction instruction, EAType eaType)
            {
                (bool isMemory, _, _) = ComputeEffectiveAddress(instruction, eaType);
                return isMemory;
            }

            /// <summary>
            /// Format effective effectiveAddress.  Allows subclasses to show which I/O device is
            /// being accessed, etc.
            /// </summary>
            /// <param name="effectiveAddress"></param>
            /// <returns>effectiveAddress as a string</returns>
            protected virtual string FormatEffectiveAddress(uint effectiveAddress)
            {
                return $"${effectiveAddress:x8}";
            }

            /// <summary>
            /// Evaluate the specified effective effectiveAddress (EA).
            /// </summary>
            /// <param name="instruction">The <see cref="Instruction"/> instance.</param>
            /// <param name="eaType">The type of effective effectiveAddress to be evaluated (Source or Destination).</param>
            /// <returns>isMemory = true if effective effectiveAddress is memory, effective effectiveAddress as an assembler string</returns>
            protected (bool isMemory, OpSize? size, string eaStr) ComputeEffectiveAddress(Instruction instruction, EAType eaType)
            {
                ushort? ea = eaType == EAType.Source ? instruction.SourceAddrMode : instruction.DestAddrMode;
                ushort? ext1 = eaType == EAType.Source ? instruction.SourceExtWord1 : instruction.DestExtWord1;
                ushort? ext2 = eaType == EAType.Source ? instruction.SourceExtWord2 : instruction.DestExtWord2;

                uint? address;
                uint? immVal;
                bool isMemory = true;
                OpSize? size = null;
                string eaStr = "";
                if (ea.HasValue)
                {
                    OpSize opSize = instruction.Size ?? OpSize.Word;

                    // Get register number (for addressing modes that use a register)
                    ushort regNum = (ushort)(ea & 0x0007);
                    switch (ea & 0x0038)
                    {
                        case (byte)AddrMode.DataRegister:
                            eaStr = $"D{regNum}";
                            isMemory = false;
                            break;
                        case (byte)AddrMode.AddressRegister:
                            eaStr = AddressReg(regNum);
                            isMemory = false;
                            break;
                        case (byte)AddrMode.Address:
                            eaStr = $"({AddressReg(regNum)})";
                            break;
                        case (byte)AddrMode.AddressPostInc:
                            eaStr = $"({AddressReg(regNum)})+";
                            break;
                        case (byte)AddrMode.AddressPreDec:
                            eaStr = $"-({AddressReg(regNum)})";
                            break;
                        case (byte)AddrMode.AddressDisp:
                            //Debug.Assert(ext1.HasValue, "Required extension word is not available");
                            if (ext1.HasValue)
                            {
                                short eaVal = (short)ext1.Value;
                                if (eaVal < 0)
                                {
                                    // HACK: Some external assemblers can't handle negative values efficiently -
                                    // they sign extend them and thus generate different code from what was disassembled here.
                                    eaStr = $"({eaVal},{AddressReg(regNum)})";
                                }
                                else
                                {
                                    eaStr = $"(${eaVal:x4},{AddressReg(regNum)})";
                                }
                            }
                            break;
                        case (byte)AddrMode.AddressIndex:
                            //Debug.Assert(ext1.HasValue, "Required extension word is not available");
                            if (ext1.HasValue)
                            {
                                byte disp = (byte)(ext1.Value & 0x00FF);
                                byte indexRegNum = (byte)((ext1.Value & 0x7000) >> 12);
                                char sz = (ext1.Value & 0x0800) == 0 ? 'W' : 'L';
                                sbyte eaDisp = (sbyte)disp;
                                if (eaDisp < 0)
                                {
                                    // HACK: Some external assemblers can't handle negative values efficiently -
                                    // they sign extend them and thus generate different code from what was disassembled here.
                                    eaStr = $"({eaDisp},{AddressReg(regNum)},D{indexRegNum}.{sz})";
                                }
                                else
                                {
                                    eaStr = $"(${eaDisp:x2},{AddressReg(regNum)},D{indexRegNum}.{sz})";
                                }
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
                                        eaStr = FormatEffectiveAddress(address.Value);
                                    }
                                    break;
                                case (byte)AddrMode.AbsLong:
                                    //Debug.Assert(ext1.HasValue && ext2.HasValue, "Required extension word is not available");
                                    if (ext1.HasValue && ext2.HasValue)
                                    {
                                        address = (uint)((ext1.Value << 16) + ext2.Value);
                                        eaStr = FormatEffectiveAddress(address.Value);
                                        size = OpSize.Long;
                                    }
                                    break;
                                case (byte)AddrMode.PCDisp:
                                    //Debug.Assert(ext1.HasValue, "Required extension word is not available");
                                    if (ext1.HasValue)
                                    {
                                        uint pc;
                                        if (eaType == EAType.Source)
                                        {
                                            pc = CurrentInstructionAddress + 2;
                                        }
                                        else
                                        {
                                            pc = Machine.CPU.PC - 2;
                                        }
                                        address = (uint)(pc + (short)ext1.Value);
                                        eaStr = FormatEffectiveAddress(address.Value);
                                    }
                                    break;
                                case (byte)AddrMode.PCIndex:
                                    //Debug.Assert(ext1.HasValue, "Required extension word is not available");
                                    if (ext1.HasValue)
                                    {
                                        byte disp = (byte)(ext1.Value & 0x00FF);
                                        byte indexRegNum = (byte)((ext1.Value & 0x7000) >> 12);
                                        char sz = (ext1.Value & 0x0800) == 0 ? 'W' : 'L';
                                        uint baseAddress = (uint)((sbyte)disp + (int)CurrentInstructionAddress + 2);
                                        eaStr = $"${baseAddress:x8}(PC,D{indexRegNum}.{sz})";
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
                                                eaStr = $"#${immVal:x8}";
                                                size = OpSize.Long;
                                            }
                                        }
                                        else
                                        {
                                            immVal = ext1.Value;
                                            eaStr = $"#${immVal:x4}";
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
                return (isMemory, size, eaStr);
            }

            /// <summary>
            /// Append the instruction mnemonic.
            /// </summary>
            /// <param name="inst"></param>
            /// <param name="sb"></param>
            protected void AppendMnemonic(Instruction inst, StringBuilder sb)
            {
                sb.Append(Mnemonic(inst));
            }

            /// <summary>
            /// Given the register mask and pre-decrement flag, append the list of registers,
            /// e.g. "D0-D7/A0-A7".
            /// </summary>
            /// <param name="regMask">Register mask</param>
            /// <param name="preDec">Determines which way to read the register mask</param>
            /// <param name="sb"></param>
            protected void AppendRegisterList(ushort regMask, bool preDec, StringBuilder sb)
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

            protected void SRCDST(Instruction inst, StringBuilder sb)
            {
                AppendMnemonic(inst, sb);
                AppendTab(EAColumn, sb);
                AppendEffectiveAddress(inst, EAType.Source, sb);
                sb.Append(',');
                AppendEffectiveAddress(inst, EAType.Destination, sb);
            }

            protected void SRC(Instruction inst, StringBuilder sb)
            {
                AppendMnemonic(inst, sb);
                AppendSizeAndTab(inst, sb);
                AppendEffectiveAddress(inst, EAType.Source, sb);
            }

            protected void DST(Instruction inst, StringBuilder sb)
            {
                AppendMnemonic(inst, sb);
                AppendSizeAndTab(inst, sb);
                AppendEffectiveAddress(inst, EAType.Destination, sb);
            }

            protected void IMMEDtoCCR(Instruction inst, StringBuilder sb)
            {
                string mnemonic = Mnemonic(inst);
                mnemonic = mnemonic[..^"toCCR".Length];
                sb.Append(mnemonic);
                AppendTab(EAColumn, sb);

                // SourceExtWord1 holds the immediate operand value.
                if (HasSourceExtWord1(inst, sb))
                {
                    ushort value = (ushort)(inst.SourceExtWord1!.Value & 0x001F);
                    sb.Append($"#${value:x2},CCR");
                }
            }

            protected void IMMEDtoSR(Instruction inst, StringBuilder sb)
            {
                string mnemonic = Mnemonic(inst);
                mnemonic = mnemonic[..^"toSR".Length];
                sb.Append(mnemonic);
                AppendTab(EAColumn, sb);

                // SourceExtWord1 holds the immediate operand value.
                if (inst.SourceExtWord1.HasValue)
                {
                    ushort value = inst.SourceExtWord1.Value;
                    sb.Append($"#${value:x4},SR");
                }
            }

            protected void MOVEtoSR(Instruction inst, StringBuilder sb)
            {
                sb.Append("MOVE");
                AppendTab(EAColumn, sb);

                AppendEffectiveAddress(inst, EAType.Source, sb);
                sb.Append(",SR");
            }

            protected void MOVEtoCCR(Instruction inst, StringBuilder sb)
            {
                sb.Append("MOVE");
                AppendTab(EAColumn, sb);

                AppendEffectiveAddress(inst, EAType.Source, sb);
                sb.Append(",CCR");
            }

            protected void MOVEfromSR(Instruction inst, StringBuilder sb)
            {
                sb.Append("MOVE");
                AppendTab(EAColumn, sb);

                sb.Append("SR,");
                AppendEffectiveAddress(inst, EAType.Destination, sb);
            }

            protected void IMMED_OP(Instruction inst, StringBuilder sb)
            {
                AppendMnemonic(inst, sb);
                AppendSizeAndTab(inst, sb);
                OpSize opSize = inst.Size ?? OpSize.Word;
                uint? value = OpcodeExecutionHandler.GetSizedOperandValue(opSize, inst.SourceExtWord1, inst.SourceExtWord2);
                if (value.HasValue)
                {
                    string sValue = opSize switch
                    {
                        OpSize.Byte => $"#${value:x2}",
                        OpSize.Word => $"#${value:x4}",
                        OpSize.Long => $"#${value:x8}",
                        _ => "???"
                    };
                    sb.Append(sValue);
                    sb.Append(',');
                    AppendEffectiveAddress(inst, EAType.Destination, sb);
                }
            }

            protected void MULS_MULU_DIVU_DIVS(Instruction inst, StringBuilder sb)
            {
                AppendMnemonic(inst, sb);
                sb.Append(".W");
                AppendTab(EAColumn, sb);

                AppendEffectiveAddress(inst, EAType.Source, sb);
                sb.Append(',');
                int dRegNum = (inst.Opcode & 0x0E00) >> 9;
                sb.Append($"D{dRegNum}");
            }

            protected void SUBX(Instruction inst, StringBuilder sb)
            {
                AppendMnemonic(inst, sb);
                AppendSizeAndTab(inst, sb);
                bool isAddressPreDecrement = (inst.Opcode & 0x0008) != 0;
                int srcReg = inst.Opcode & 0x0007;
                int dstReg = (inst.Opcode & 0x0E00) >> 9;
                if (isAddressPreDecrement)
                {
                    sb.Append($"-(A{srcReg}),-(A{dstReg})");
                }
                else
                {
                    sb.Append($"D{srcReg},D{dstReg}");
                }
            }


            protected void ADD_SUB_OR_AND_EOR_CMP(Instruction inst, StringBuilder sb)
            {
                AppendMnemonic(inst, sb);
                AppendSizeAndTab(inst, sb);
                int dRegNum = (inst.Opcode & 0x0E00) >> 9;
                bool dnDest = (inst.Opcode & 0x0100) == 0;
                if (dnDest)
                {
                    AppendEffectiveAddress(inst, EAType.Destination, sb);
                    sb.Append(',');
                    AppendDataRegister(dRegNum, sb);
                }
                else
                {
                    AppendDataRegister(dRegNum, sb);
                    sb.Append(',');
                    AppendEffectiveAddress(inst, EAType.Destination, sb);
                }
            }
            protected void CMPM(Instruction inst, StringBuilder sb)
            {
                AppendMnemonic(inst, sb);
                AppendSizeAndTab(inst, sb);

                byte aDstRegNum = (byte)((inst.Opcode & 0x0E00) >> 9);
                byte aSrcRegNum = (byte)(inst.Opcode & 0x0007);

                sb.Append("(A");
                sb.Append(aSrcRegNum);
                sb.Append(")+,(A");
                sb.Append(aDstRegNum);
                sb.Append(")+");
            }

            protected void MOVE(Instruction inst, StringBuilder sb)
            {
                AppendMnemonic(inst, sb);
                AppendSizeAndTab(inst, sb);
                AppendEffectiveAddress(inst, EAType.Source, sb);
                sb.Append(',');
                AppendEffectiveAddress(inst, EAType.Destination, sb);
            }

            protected void MOVEA(Instruction inst, StringBuilder sb)
            {
                AppendMnemonic(inst, sb);
                AppendSizeAndTab(inst, sb);

                AppendEffectiveAddress(inst, EAType.Source, sb);
                sb.Append(',');

                int regNum = (inst.Opcode & 0x0E00) >> 9;
                sb.Append($"{AddressReg(regNum)}");
            }

            protected void MOVEP(Instruction inst, StringBuilder sb)
            {
                AppendMnemonic(inst, sb);
                OpSize size = (inst.Opcode & 0x0040) == 0 ? OpSize.Word : OpSize.Long;
                string sz = size == OpSize.Word ? ".W" : ".L";
                sb.Append(sz);
                AppendTab(EAColumn, sb);

                byte aRegNum = (byte)(inst.Opcode & 0x0007);
                byte dRegNum = (byte)((inst.Opcode & 0x0E00) >> 9);
                bool memToReg = (inst.Opcode & 0x0080) == 0;
                if (inst.SourceExtWord1.HasValue)
                {
                    int disp = Helpers.SignExtendValue((uint)inst.SourceExtWord1, OpSize.Word);

                    // For displacements < 1000 use decimal, otherwise use hex
                    string sDisp = disp >= 1000 ? $"${disp:x4}" : $"{disp}";
                    if (memToReg)
                    {
                        sb.Append($"({sDisp},{AddressReg(aRegNum)}),D{dRegNum}");
                    }
                    else
                    {
                        sb.Append($"D{dRegNum},({sDisp},{AddressReg(aRegNum)})");
                    }
                }
                else
                {
                    sb.Append("[SourceExtWord1 missing]");
                }
            }

            protected void MOVEM(Instruction inst, StringBuilder sb)
            {
                AppendMnemonic(inst, sb);
                OpSize size = inst.Size ?? OpSize.Long;
                AppendSizeAndTab(size, sb);
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
                            AppendRegisterList(regMask, true, sb);
                        }
                        else
                        {
                            AppendRegisterList(regMask, false, sb);
                        }
                        sb.Append(',');
                        AppendEffectiveAddress(inst, EAType.Destination, sb);
                    }
                    else
                    {
                        // Source is mem, dest is reg (but EA is in dest field)
                        AppendEffectiveAddress(inst, EAType.Destination, sb);
                        sb.Append(',');
                        AppendRegisterList(regMask, false, sb);
                    }
                }
            }

            protected void MOVEQ(Instruction inst, StringBuilder sb)
            {
                AppendMnemonic(inst, sb);
                sb.Append(".L");
                AppendTab(EAColumn, sb);
                int dRegNum = (inst.Opcode & 0x0E00) >> 9;
                int data = Helpers.SignExtendValue((uint)(inst.Opcode & 0x00FF), OpSize.Byte);
                sb.Append($"#{data},D{dRegNum}");
            }

            protected void ADDQ_SUBQ(Instruction inst, StringBuilder sb)
            {
                AppendMnemonic(inst, sb);

                int addVal = (inst.Opcode & 0x0E00) >> 9;
                if (addVal == 0)
                {
                    addVal = 8;
                }

                int size = (inst.Opcode & 0x00E0) >> 6;
                string sz = size switch
                {
                    0 => ".B",
                    1 => ".W",
                    2 => ".L",
                    _ => ""
                };

                // When being applied to an effectiveAddress register, we work with the entire 32-bit value regardless
                // of the size that has been specified. This operation also doesn't affect the flags.
                if ((inst.Opcode & 0x0038) == (int)AddrMode.AddressRegister)
                {
                    int regNum = inst.Opcode & 0x0007;
                    sb.Append(sz);
                    AppendTab(EAColumn, sb);
                    sb.Append($"#{addVal},");
                    sb.Append($"{AddressReg(regNum)}");
                }
                else
                {
                    sb.Append(sz);
                    AppendTab(EAColumn, sb);
                    sb.Append($"#{addVal},");
                    AppendEffectiveAddress(inst, EAType.Destination, sb);
                }
            }

            protected void LINK(Instruction inst, StringBuilder sb)
            {
                AppendMnemonic(inst, sb);
                AppendTab(EAColumn, sb);

                if (inst.SourceExtWord1.HasValue)
                {
                    byte regNum = (byte)(inst.Opcode & 0x0007);
                    int disp = Helpers.SignExtendValue((uint)inst.SourceExtWord1, OpSize.Word);
                    sb.Append($"{AddressReg(regNum)},#{disp}");
                }
            }

            protected void UNLK(Instruction inst, StringBuilder sb)
            {
                AppendMnemonic(inst, sb);
                AppendTab(EAColumn, sb);

                byte regNum = (byte)(inst.Opcode & 0x0007);
                sb.Append($"{AddressReg(regNum)}");
            }

            protected void Bcc(Instruction inst, StringBuilder sb)
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

                uint pc = Machine.CPU.PC;
                int disp = inst.Opcode & 0x00FF;
                OpSize size = OpSize.Word;
                if (disp == 0)
                {
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

                AppendSizeAndTab(size, sb);
                uint address = (uint)(pc + disp);
                sb.Append(FormatEffectiveAddress(address));
            }

            protected void JMP_JSR(Instruction inst, StringBuilder sb)
            {
                AppendMnemonic(inst, sb);
                AppendTab(EAColumn, sb);
                AppendEffectiveAddress(inst, EAType.Source, sb);
            }

            protected void BRA_BSR(Instruction inst, StringBuilder sb)
            {
                AppendMnemonic(inst, sb);
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

                AppendSizeAndTab(size, sb);
                uint address = (uint)(pc + disp);
                sb.Append(FormatEffectiveAddress(address));
            }

            protected void DBcc(Instruction inst, StringBuilder sb)
            {
                sb.Append("DB");
                Condition cond = (Condition)((inst.Opcode & 0x0F00) >> 8);
                AppendCondition(cond, sb);
                sb.Append(".W");
                AppendTab(EAColumn, sb);

                int dRegNum = inst.Opcode & 0x0007;
                uint pc = Machine.CPU.PC;

                // Note: extra -2 to account for PC pointing at the next instruction, not on the extension word for the
                // current instruction (as the displacement for DBcc instructions assumes)
                if (inst.SourceExtWord1.HasValue)
                {
                    int disp = Helpers.SignExtendValue((uint)inst.SourceExtWord1, OpSize.Word) - 2;
                    uint address = (uint)(pc + disp);
                    sb.Append($"D{dRegNum},{FormatEffectiveAddress(address)}");
                }
            }

            protected void Scc(Instruction inst, StringBuilder sb)
            {
                sb.Append('S');
                Condition cond = (Condition)((inst.Opcode & 0x0F00) >> 8);
                AppendCondition(cond, sb);
                sb.Append(".B"); // Size is always byte
                AppendTab(EAColumn, sb);
                AppendEffectiveAddress(inst, EAType.Destination, sb);
            }

            protected void ADDA_SUBA_CMPA(Instruction inst, StringBuilder sb)
            {
                AppendMnemonic(inst, sb);
                AppendSizeAndTab(inst, sb);

                AppendEffectiveAddress(inst, EAType.Source, sb);
                sb.Append(',');
                int regNum = (inst.Opcode & 0x0E00) >> 9;
                sb.Append($"{AddressReg(regNum)}");
            }

            protected void BTST_BCHG_BCLR_BSET(Instruction inst, StringBuilder sb)
            {
                AppendMnemonic(inst, sb);

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
                    sb.Append($"#{bitNum}");
                }
                if (regNum.HasValue)
                {
                    sb.Append($"D{regNum}");
                }
                sb.Append(',');
                AppendEffectiveAddress(inst, EAType.Destination, sb);
            }

            protected void ASL_ASR_LSL_LSR_ROL_ROR_ROXL_ROXR(Instruction inst, StringBuilder sb)
            {
                AppendMnemonic(inst, sb);
                AppendSizeAndTab(inst, sb);

                byte sizeBits = (byte)((inst.Opcode & 0x00C0) >> 6);
                if (sizeBits == 0x03)
                {
                    AppendEffectiveAddress(inst, EAType.Source, sb);
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
                        sb.Append($"D{shift}");
                    }
                    else
                    {
                        int shiftAmt = shift != 0 ? shift : 8;
                        sb.Append($"#{shiftAmt}");
                    }
                    sb.Append(',');
                    sb.Append($"D{dRegNum}");
                }
            }

            protected void LEA(Instruction inst, StringBuilder sb)
            {
                AppendMnemonic(inst, sb);
                (_, OpSize? size, string effectiveAddress) = ComputeEffectiveAddress(inst, EAType.Source);
                if (size != null)
                {
                    AppendSizeAndTab(size, sb);
                }
                else
                {
                    AppendTab(EAColumn, sb);
                }
                sb.Append(effectiveAddress);
                int regNum = (inst.Opcode & 0x0E00) >> 9;
                sb.Append(',');
                sb.Append($"{AddressReg(regNum)}");
            }

            protected void EXT(Instruction inst, StringBuilder sb)
            {
                AppendMnemonic(inst, sb);
                OpSize size = (inst.Opcode & 0x0040) == 0 ? OpSize.Word : OpSize.Long;
                string sz = size switch
                {
                    OpSize.Word => ".W",
                    OpSize.Long => ".L",
                    _ => ".?"
                };
                sb.Append(sz);
                AppendTab(EAColumn, sb);

                byte regNum = (byte)(inst.Opcode & 0x0007);
                sb.Append($"D{regNum}");
            }

            protected void SWAP(Instruction inst, StringBuilder sb)
            {
                AppendMnemonic(inst, sb);
                AppendTab(EAColumn, sb);

                byte regNum = (byte)(inst.Opcode & 0x0007);
                sb.Append($"D{regNum}");
            }

            protected void MOVEUSP(Instruction inst, StringBuilder sb)
            {
                sb.Append("MOVE");
                AppendTab(EAColumn, sb);

                byte regNum = (byte)(inst.Opcode & 0x0007);
                if ((inst.Opcode & 0x0008) == 0)
                {
                    sb.Append($"{AddressReg(regNum)},USP");
                }
                else
                {
                    sb.Append($"USP,{AddressReg(regNum)}");
                }
            }

            protected void ABCD_SBCD(Instruction inst, StringBuilder sb)
            {
                AppendMnemonic(inst, sb);
                AppendTab(EAColumn, sb);

                byte rSrc = (byte)(inst.Opcode & 0x0007);
                byte rDest = (byte)((inst.Opcode & 0x0E00) >> 9);

                if ((inst.Opcode & 0x0008) == 0)
                {
                    // Working with data registers
                    sb.Append($"D{rSrc},D{rDest}");
                }
                else
                {
                    // Working with memory addresses, so predecrement both effectiveAddress registers by 1 byte.
                    sb.Append($"-({AddressReg(rSrc)}),-({AddressReg(rDest)})");
                }
            }

            protected void EXG(Instruction inst, StringBuilder sb)
            {
                AppendMnemonic(inst, sb);
                sb.Append(".L");
                AppendTab(EAColumn, sb);

                // NOTE: x and y are the reverse of the convention used
                // in the NXP Programmer's Reference Manual.
                byte rX = (byte)(inst.Opcode & 0x0007);
                byte rY = (byte)((inst.Opcode & 0x0E00) >> 9);
                byte mode = (byte)((inst.Opcode & 0x00F8) >> 3);
                switch (mode)
                {
                    case 0x08:      // Data Register <-> Data Register
                        sb.Append($"D{rY},D{rX}");
                        break;
                    case 0x09:      // Address Register <-> Address Register
                        sb.Append($"{AddressReg(rY)},{AddressReg(rX)}");
                        break;
                    case 0x11:      // Data Register <-> Address Register
                        sb.Append($"D{rY},{AddressReg(rX)}");
                        break;
                    default:
                        //Debug.Assert(false, "Invalid operating mode for EXG instruction.");
                        break;
                }
            }

            protected void STOP(Instruction inst, StringBuilder sb)
            {
                AppendMnemonic(inst, sb);
                AppendTab(EAColumn, sb);

                var data = inst.SourceExtWord1;
                if (data.HasValue)
                {
                    sb.Append($"#${data.Value:x4}");
                }
            }

            protected void TRAP(Instruction inst, StringBuilder sb)
            {
                AppendMnemonic(inst, sb);
                AppendTab(EAColumn, sb);

                ushort vector = (ushort)(inst.Opcode & 0x000F);
                sb.Append($"#{vector}");
            }

            protected void CHK(Instruction inst, StringBuilder sb)
            {
                AppendMnemonic(inst, sb);
                AppendSizeAndTab(inst, sb);

                AppendEffectiveAddress(inst, EAType.Source, sb);
                int regNum = (inst.Opcode & 0x0E00) >> 9;
                sb.Append(',');
                sb.Append($"D{regNum}");
            }

            protected void ADDX(Instruction inst, StringBuilder sb)
            {
                AppendMnemonic(inst, sb);
                AppendSizeAndTab(inst, sb);

                byte rX = (byte)(inst.Opcode & 0x0007);
                byte rY = (byte)((inst.Opcode & 0x0E00) >> 9);
                bool usingDataReg = (inst.Opcode & 0x0008) == 0;
                if (usingDataReg)
                {
                    sb.Append($"D{rX},D{rY}");
                }
                else
                {
                    sb.Append($"-({AddressReg(rX)}),-({AddressReg(rY)})");
                }
            }

            protected void NOOPERANDS(Instruction inst, StringBuilder sb)
            {
                AppendMnemonic(inst, sb);
            }

            protected void NONE(Instruction inst, StringBuilder sb)
            {
                AppendMnemonic(inst, sb);
                sb.Append(" ???");
            }

            protected virtual void LINEA(Instruction inst, StringBuilder sb)
            {
                sb.Append($"LINEA");
                AppendTab(EAColumn, sb);
                sb.Append($"${(ushort)(inst.Opcode & 0x0fff):x3}");
            }

        }
    }
}
