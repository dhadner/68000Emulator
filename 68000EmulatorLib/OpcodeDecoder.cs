using PendleCodeMonkey.MC68000EmulatorLib.Enumerations;
using System.Runtime.CompilerServices;
using static PendleCodeMonkey.MC68000EmulatorLib.Machine;

namespace PendleCodeMonkey.MC68000EmulatorLib
{
    /// <summary>
    /// Implementation of the <see cref="OpcodeDecoder"/> class.
    /// </summary>
    internal class OpcodeDecoder
    {
        /// <summary>
        /// Dictionary of instruction handler info.
        /// </summary>
        internal static Dictionary<byte, List<InstructionInfo>> InstructionInfos
        { get; } = new Dictionary<byte, List<InstructionInfo>>()
        {
            {
                0x00, new List<InstructionInfo>()
                {
                    { new InstructionInfo(0b0000000000111100, 0b1111111111111111, "ORItoCCR", OpHandlerID.ORItoCCR) },
                    { new InstructionInfo(0b0000000001111100, 0b1111111111111111, "ORItoSR", OpHandlerID.ORItoSR) },
                    { new InstructionInfo(0b0000000000000000, 0b1111111100000000, "ORI", OpHandlerID.ORI) },
                    { new InstructionInfo(0b0000001000111100, 0b1111111111111111, "ANDItoCCR", OpHandlerID.ANDItoCCR) },
                    { new InstructionInfo(0b0000001001111100, 0b1111111111111111, "ANDItoSR", OpHandlerID.ANDItoSR) },
                    { new InstructionInfo(0b0000001000000000, 0b1111111100000000, "ANDI", OpHandlerID.ANDI) },
                    { new InstructionInfo(0b0000010000000000, 0b1111111100000000, "SUBI", OpHandlerID.SUBI) },
                    { new InstructionInfo(0b0000011000000000, 0b1111111100000000, "ADDI", OpHandlerID.ADDI) },
                    { new InstructionInfo(0b0000101000111100, 0b1111111111111111, "EORItoCCR", OpHandlerID.EORItoCCR) },
                    { new InstructionInfo(0b0000101001111100, 0b1111111111111111, "EORItoSR", OpHandlerID.EORItoSR) },
                    { new InstructionInfo(0b0000101000000000, 0b1111111100000000, "EORI", OpHandlerID.EORI) },
                    { new InstructionInfo(0b0000110000000000, 0b1111111100000000, "CMPI", OpHandlerID.CMPI) },
                    { new InstructionInfo(0b0000000100001000, 0b1111000100111000, "MOVEP", OpHandlerID.MOVEP) },
                    { new InstructionInfo(0b0000100000000000, 0b1111111111000000, "BTST", OpHandlerID.BTST) },
                    { new InstructionInfo(0b0000100001000000, 0b1111111111000000, "BCHG", OpHandlerID.BCHG) },
                    { new InstructionInfo(0b0000100010000000, 0b1111111111000000, "BCLR", OpHandlerID.BCLR) },
                    { new InstructionInfo(0b0000100011000000, 0b1111111111000000, "BSET", OpHandlerID.BSET) },
                    { new InstructionInfo(0b0000000100000000, 0b1111000111000000, "BTST", OpHandlerID.BTST) },
                    { new InstructionInfo(0b0000000101000000, 0b1111000111000000, "BCHG", OpHandlerID.BCHG) },
                    { new InstructionInfo(0b0000000110000000, 0b1111000111000000, "BCLR", OpHandlerID.BCLR) },
                    { new InstructionInfo(0b0000000111000000, 0b1111000111000000, "BSET", OpHandlerID.BSET) }
                }
            },
            {
                0x01, new List<InstructionInfo>()
                {
                    { new InstructionInfo(0b0001000000000000, 0b1111000000000000, "MOVE", OpHandlerID.MOVE) }
                }
            },
            {
                0x02, new List<InstructionInfo>()
                {
                    { new InstructionInfo(0b0010000001000000, 0b1111000111000000, "MOVEA", OpHandlerID.MOVEA) },
                    { new InstructionInfo(0b0010000000000000, 0b1111000000000000, "MOVE", OpHandlerID.MOVE) }
                }
            },
            {
                0x03, new List<InstructionInfo>()
                {
                    { new InstructionInfo(0b0011000001000000, 0b1111000111000000, "MOVEA", OpHandlerID.MOVEA) },
                    { new InstructionInfo(0b0011000000000000, 0b1111000000000000, "MOVE", OpHandlerID.MOVE) }
                }
            },
            {
                0x04, new List<InstructionInfo>()
                {
                    { new InstructionInfo(0b0100000011000000, 0b1111111111000000, "MOVEfromSR", OpHandlerID.MOVEfromSR) },
                    { new InstructionInfo(0b0100010011000000, 0b1111111111000000, "MOVEtoCCR", OpHandlerID.MOVEtoCCR) },
                    { new InstructionInfo(0b0100011011000000, 0b1111111111000000, "MOVEtoSR", OpHandlerID.MOVEtoSR) },
                    { new InstructionInfo(0b0100000000000000, 0b1111111100000000, "NEGX", OpHandlerID.NEGX) },
                    { new InstructionInfo(0b0100001000000000, 0b1111111100000000, "CLR", OpHandlerID.CLR) },
                    { new InstructionInfo(0b0100010000000000, 0b1111111100000000, "NEG", OpHandlerID.NEG) },
                    { new InstructionInfo(0b0100011000000000, 0b1111111100000000, "NOT", OpHandlerID.NOT) },
                    { new InstructionInfo(0b0100100010000000, 0b1111111110111000, "EXT", OpHandlerID.EXT) },
                    { new InstructionInfo(0b0100100000000000, 0b1111111111000000, "NBCD", OpHandlerID.NBCD) },
                    { new InstructionInfo(0b0100100001000000, 0b1111111111111000, "SWAP", OpHandlerID.SWAP) },
                    { new InstructionInfo(0b0100100001000000, 0b1111111111000000, "PEA", OpHandlerID.PEA) },
                    { new InstructionInfo(0b0100101011111100, 0b1111111111111111, "ILLEGAL", OpHandlerID.ILLEGAL) },
                    { new InstructionInfo(0b0100101011000000, 0b1111111111000000, "TAS", OpHandlerID.TAS) },
                    { new InstructionInfo(0b0100101000000000, 0b1111111100000000, "TST", OpHandlerID.TST) },
                    { new InstructionInfo(0b0100111001000000, 0b1111111111110000, "TRAP", OpHandlerID.TRAP) },
                    { new InstructionInfo(0b0100111001010000, 0b1111111111111000, "LINK", OpHandlerID.LINK) },
                    { new InstructionInfo(0b0100111001011000, 0b1111111111111000, "UNLK", OpHandlerID.UNLK) },
                    { new InstructionInfo(0b0100111001100000, 0b1111111111110000, "MOVEUSP", OpHandlerID.MOVEUSP) },
                    { new InstructionInfo(0b0100111001110000, 0b1111111111111111, "RESET", OpHandlerID.RESET) },
                    { new InstructionInfo(0b0100111001110001, 0b1111111111111111, "NOP", OpHandlerID.NOP) },
                    { new InstructionInfo(0b0100111001110010, 0b1111111111111111, "STOP", OpHandlerID.STOP) },
                    { new InstructionInfo(0b0100111001110011, 0b1111111111111111, "RTE", OpHandlerID.RTE) },
                    { new InstructionInfo(0b0100111001110101, 0b1111111111111111, "RTS", OpHandlerID.RTS) },
                    { new InstructionInfo(0b0100111001110110, 0b1111111111111111, "TRAPV", OpHandlerID.TRAPV) },
                    { new InstructionInfo(0b0100111001110111, 0b1111111111111111, "RTR", OpHandlerID.RTR) },
                    { new InstructionInfo(0b0100111010000000, 0b1111111111000000, "JSR", OpHandlerID.JSR) },
                    { new InstructionInfo(0b0100111011000000, 0b1111111111000000, "JMP", OpHandlerID.JMP) },
                    { new InstructionInfo(0b0100100010000000, 0b1111101110000000, "MOVEM", OpHandlerID.MOVEM) },
                    { new InstructionInfo(0b0100000110000000, 0b1111000111000000, "CHK", OpHandlerID.CHK) },
                    { new InstructionInfo(0b0100000111000000, 0b1111000111000000, "LEA", OpHandlerID.LEA) }
                }
            },
            {
                0x05, new List<InstructionInfo>()
                {
                    { new InstructionInfo(0b0101000011001000, 0b1111000011111000, "DBcc", OpHandlerID.DBcc) },
                    { new InstructionInfo(0b0101000011000000, 0b1111000011000000, "Scc", OpHandlerID.Scc) },
                    { new InstructionInfo(0b0101000000000000, 0b1111000100000000, "ADDQ", OpHandlerID.ADDQ) },
                    { new InstructionInfo(0b0101000100000000, 0b1111000100000000, "SUBQ", OpHandlerID.SUBQ) }
               }
            },
            {
                0x06, new List<InstructionInfo>()
                {
                    { new InstructionInfo(0b0110000000000000, 0b1111111100000000, "BRA", OpHandlerID.BRA) },
                    { new InstructionInfo(0b0110000100000000, 0b1111111100000000, "BSR", OpHandlerID.BSR) },
                    { new InstructionInfo(0b0110000000000000, 0b1111000000000000, "Bcc", OpHandlerID.Bcc) }
               }
            },
            {
                0x07, new List<InstructionInfo>()
                {
                    { new InstructionInfo(0b0111000000000000, 0b1111000100000000, "MOVEQ", OpHandlerID.MOVEQ) }
               }
            },
            {
                0x08, new List<InstructionInfo>()
                {
                    { new InstructionInfo(0b1000000011000000, 0b1111000111000000, "DIVU", OpHandlerID.DIVU) },
                    { new InstructionInfo(0b1000000111000000, 0b1111000111000000, "DIVS", OpHandlerID.DIVS) },
                    { new InstructionInfo(0b1000000100000000, 0b1111000111110000, "SBCD", OpHandlerID.SBCD) },
                    { new InstructionInfo(0b1000000000000000, 0b1111000000000000, "OR", OpHandlerID.OR) }
               }
            },
            {
                0x09, new List<InstructionInfo>()
                {
                    { new InstructionInfo(0b1001000011000000, 0b1111000011000000, "SUBA", OpHandlerID.SUBA) },
                    { new InstructionInfo(0b1001000100000000, 0b1111000100110000, "SUBX", OpHandlerID.SUBX) },
                    { new InstructionInfo(0b1001000000000000, 0b1111000000000000, "SUB", OpHandlerID.SUB) }
               }
            },
            {
                0x0A, new List<InstructionInfo>()
                {
                    { new InstructionInfo(0b1010000000000000, 0b1111000000000000, "LINEA", OpHandlerID.LINEA) }
               }
            },
            {
                0x0B, new List<InstructionInfo>()
                {
                    { new InstructionInfo(0b1011000011000000, 0b1111000011000000, "CMPA", OpHandlerID.CMPA) },
                    { new InstructionInfo(0b1011000100001000, 0b1111000100111000, "CMPM", OpHandlerID.CMPM) },
                    { new InstructionInfo(0b1011000100000000, 0b1111000100000000, "EOR", OpHandlerID.EOR) },
                    { new InstructionInfo(0b1011000000000000, 0b1111000100000000, "CMP", OpHandlerID.CMP) }
               }
            },
            {
                0x0C, new List<InstructionInfo>()
                {
                    { new InstructionInfo(0b1100000011000000, 0b1111000111000000, "MULU", OpHandlerID.MULU) },
                    { new InstructionInfo(0b1100000111000000, 0b1111000111000000, "MULS", OpHandlerID.MULS) },
                    { new InstructionInfo(0b1100000100000000, 0b1111000111110000, "ABCD", OpHandlerID.ABCD) },
                    { new InstructionInfo(0b1100000100000000, 0b1111000100110000, "EXG", OpHandlerID.EXG) },
                    { new InstructionInfo(0b1100000000000000, 0b1111000000000000, "AND", OpHandlerID.AND) }
               }
            },
            {
                0x0D, new List<InstructionInfo>()
                {
                    { new InstructionInfo(0b1101000011000000, 0b1111000011000000, "ADDA", OpHandlerID.ADDA) },
                    { new InstructionInfo(0b1101000100000000, 0b1111000100110000, "ADDX", OpHandlerID.ADDX) },
                    { new InstructionInfo(0b1101000000000000, 0b1111000000000000, "ADD", OpHandlerID.ADD) }
               }
            },
            {
                0x0E, new List<InstructionInfo>()
                {
                    { new InstructionInfo(0b1110000011000000, 0b1111111111000000, "ASR", OpHandlerID.ASR) },
                    { new InstructionInfo(0b1110000111000000, 0b1111111111000000, "ASL", OpHandlerID.ASL) },
                    { new InstructionInfo(0b1110001011000000, 0b1111111111000000, "LSR", OpHandlerID.LSR) },
                    { new InstructionInfo(0b1110001111000000, 0b1111111111000000, "LSL", OpHandlerID.LSL) },
                    { new InstructionInfo(0b1110010011000000, 0b1111111111000000, "ROXR", OpHandlerID.ROXR) },
                    { new InstructionInfo(0b1110010111000000, 0b1111111111000000, "ROXL", OpHandlerID.ROXL) },
                    { new InstructionInfo(0b1110011011000000, 0b1111111111000000, "ROR", OpHandlerID.ROR) },
                    { new InstructionInfo(0b1110011111000000, 0b1111111111000000, "ROL", OpHandlerID.ROL) },
                    { new InstructionInfo(0b1110000000000000, 0b1111000100011000, "ASR", OpHandlerID.ASR) },
                    { new InstructionInfo(0b1110000100000000, 0b1111000100011000, "ASL", OpHandlerID.ASL) },
                    { new InstructionInfo(0b1110000000001000, 0b1111000100011000, "LSR", OpHandlerID.LSR) },
                    { new InstructionInfo(0b1110000100001000, 0b1111000100011000, "LSL", OpHandlerID.LSL) },
                    { new InstructionInfo(0b1110000000010000, 0b1111000100011000, "ROXR", OpHandlerID.ROXR) },
                    { new InstructionInfo(0b1110000100010000, 0b1111000100011000, "ROXL", OpHandlerID.ROXL) },
                    { new InstructionInfo(0b1110000000011000, 0b1111000100011000, "ROR", OpHandlerID.ROR) },
                    { new InstructionInfo(0b1110000100011000, 0b1111000100011000, "ROL", OpHandlerID.ROL) }
               }
            },
            {
                0x0F, new List<InstructionInfo>()
                {
                    { new InstructionInfo(0b1111000000000000, 0b1111000000000000, "LINEF", OpHandlerID.LINEF) }
               }
            },

        };

        /// <summary>
        /// Attempt to retrieve an <see cref="Instruction"/> instance corresponding to
        /// the specified opcode value.
        /// </summary>
        /// <param name="opcode">The 16-bit instruction opcode value.</param>
        /// <returns>
        /// The <see cref="Instruction"/> instance that corresponds to the specified
        /// opcode value (or null if the opcode is invalid).
        /// </returns>
        internal Instruction? GetLegalInstruction(ushort opcode)
        {
            if (InstructionCache.TryGetValue(opcode, out var cachedInst))
            {
                return cachedInst;
            }
            byte group = (byte)((opcode & 0xF000) >> 12);
            if (InstructionInfos.TryGetValue(group, out var groupList))
            {
                foreach (var inst in groupList)
                {
                    if ((opcode & inst.OpcodeMask) == inst.OpcodeValue)
                    {
                        return ValidateOpcode(opcode, inst);
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Dictionary of cached valid instructions with sizes and valid addressing modes.
        /// </summary>
        private readonly Dictionary<ushort, Instruction> InstructionCache = [];

        /// <summary>
        /// Effective Addresses 0b111101, 0b111110, and 0b111111 are never legal.
        /// </summary>
        /// <param name="ea"></param>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool UndefinedEA(byte? ea)
        {
            return ea == 0b111101 || ea == 0b111110 || ea == 0b111111;
        }

        /// <summary>
        /// Retrieve instruction details (decoding EA, extension words, etc.)
        /// </summary>
        /// <param name="opcode">The 16-bit opcode value for the instruction.</param>
        /// <param name="instInfo">The <see cref="InstructionInfo"/> instance for the instruction.</param>
        /// <returns>An <see cref="Instruction"/> object containing details of the instruction or
        /// null if the opcode is illegal.</returns>
        private Instruction? ValidateOpcode(ushort opcode, InstructionInfo instInfo)
        {
            byte? sourceEA = null;
            byte? destEA = null;
            OpSize opSize = OpSize.Word;        // Defaults to Word sized operations.
            byte? opMode;

            switch (instInfo.HandlerID)
            {
                case OpHandlerID.ORItoCCR:
                case OpHandlerID.ANDItoCCR:
                case OpHandlerID.EORItoCCR:
                    opSize = OpSize.Byte;
                    break;

                case OpHandlerID.NEGX:
                case OpHandlerID.CLR:
                case OpHandlerID.NEG:
                case OpHandlerID.NOT:
                case OpHandlerID.ORI:
                case OpHandlerID.ANDI:
                case OpHandlerID.SUBI:
                case OpHandlerID.ADDI:
                case OpHandlerID.EORI:
                case OpHandlerID.CMPI:
                    destEA = Helpers.GetEAMode(opcode);
                    if (UndefinedEA(destEA)) return null;
                    if ((destEA & 0b111000) == 0b001000 || // A(n) or
                        (destEA & 0b111111) == 0b111100 || // Immediate or
                        (destEA & 0b111010) == 0b111010)   // PCDisp or PCIndex
                    {
                        // Address mode not allowed
                        return null;
                    }

                    opSize = Helpers.GetOpSize(opcode);
                    if ((int)opSize == 0x03)
                    {
                        // Illegal OpSize
                        return null;
                    }
                    break;

                case OpHandlerID.BTST:
                    destEA = Helpers.GetEAMode(opcode);
                    if (UndefinedEA(destEA)) return null;

                    // When EA is D(n), size is long.  Otherwise, size it byte.
                    if ((destEA & 0b111000) == 0b000000)
                    {
                        opSize = OpSize.Long;
                    }
                    else
                    {
                        opSize = OpSize.Byte;
                    }
                    if ((opcode & 0x0100) == 0)
                    {
                        // Static bit number
                        // EA cannot be address register or immediate
                        if ((destEA & 0b111000) == 0b001000 ||
                            (destEA & 0b111111) == 0b111100)
                        {
                            return null; // A(n) and immediate not allowed
                        }
                    }
                    else
                    {
                        // Dynamic bit number in a data register. EA cannot be address register.
                        if ((destEA & 0b111000) == 0b001000)
                        {
                            return null; // A(n) not allowed
                        }
                    }
                    break;

                case OpHandlerID.BCHG:
                case OpHandlerID.BCLR:
                case OpHandlerID.BSET:
                    destEA = Helpers.GetEAMode(opcode);
                    if (UndefinedEA(destEA)) return null;

                    // When EA is D(n), size is long.  Otherwise, size it byte.
                    if ((destEA & 0b111000) == 0b000000)
                    {
                        opSize = OpSize.Long;
                    }
                    else
                    {
                        opSize = OpSize.Byte;
                    }
                    // EA cannot be address register, immediate, PCDisp, or PCIndex
                    if ((destEA & 0b111000) == 0b001000 || // A(n)
                        (destEA & 0b111111) == 0b111100 || // immediate
                        (destEA & 0b111010) == 0b111010)   // PCDisp or PCIndex
                    {
                        return null; // A(n), immediate, PCDisp, and PCIndex not allowed
                    }

                    break;

                case OpHandlerID.MOVE:
                    sourceEA = Helpers.GetEAMode(opcode);
                    destEA = Helpers.GetReversedEAMode(opcode);
                    if (UndefinedEA(sourceEA)) return null;
                    if (UndefinedEA(destEA)) return null;

                    // Get the operation size (which is in an alternative format and must therefore be translated
                    // to an OpSize enum value)
                    byte size = (byte)((opcode & 0x3000) >> 12);
                    switch (size)
                    {
                        case 0x01:
                            if ((sourceEA & 0b111000) == 0b001000)
                            {
                                // Address register direct mode not allowed for byte move
                                return null;
                            }
                            opSize = OpSize.Byte;
                            break;
                        case 0x02:
                            opSize = OpSize.Long;
                            break;
                        case 0x03:
                            opSize = OpSize.Word;
                            break;
                        default:
                            // Illegal OpSize
                            return null;
                    }
                    if ((sourceEA & 0b111111) == 0b111101 || // illegal
                        (sourceEA & 0b111111) == 0b111110 || // illegal
                        (sourceEA & 0b111111) == 0b111111)   // illegal
                    {
                        return null;
                    }
                    if ((destEA & 0b111000) == 0b001000 || // A(n)
                        (destEA & 0b111110) == 0b111100 || // immediate or indexed
                        (destEA & 0b111110) == 0b111010 || // indexed
                        (destEA & 0b111110) == 0b111110)   // illegal
                    {
                        return null; // A(n), immediate, indexed not allowed
                    }
                    break;

                case OpHandlerID.MOVEA:
                    sourceEA = Helpers.GetEAMode(opcode);
                    destEA = Helpers.GetReversedEAMode(opcode);
                    if (UndefinedEA(sourceEA)) return null;
                    if (UndefinedEA(destEA)) return null;

                    // Get the operation size (which is in an alternative format and must therefore be translated
                    // to an OpSize enum value)
                    byte sizeA = (byte)((opcode & 0x3000) >> 12);
                    switch (sizeA)
                    {
                        case 0x02:
                            opSize = OpSize.Long;
                            break;
                        case 0x03:
                            opSize = OpSize.Word;
                            break;
                        default:
                            // Illegal OpSize
                            return null;
                    }
                    if ((sourceEA & 0b111111) == 0b111101 || // illegal
                        (sourceEA & 0b111111) == 0b111110 || // immediate or indexed
                        (sourceEA & 0b111111) == 0b111111)   // illegal
                    {
                        return null; // not allowed
                    }
                    break;

                case OpHandlerID.MOVEfromSR:
                    destEA = Helpers.GetEAMode(opcode);
                    if (UndefinedEA(destEA)) return null;

                    // EA cannot be address register, immediate, PCDisp, or PCIndex
                    if ((destEA & 0b111000) == 0b001000 || // A(n)
                        (destEA & 0b111100) == 0b111100 || // immediate or indexed
                        (destEA & 0b111010) == 0b111010)   // indexed or PCDisp
                    {
                        return null; // A(n), immediate, PCDisp, and PCIndex not allowed
                    }
                    break;

                case OpHandlerID.MOVEtoCCR:
                    sourceEA = Helpers.GetEAMode(opcode);
                    opSize = OpSize.Byte;
                    if (UndefinedEA(sourceEA)) return null;
                    if ((sourceEA & 0b111000) == 0b001000)
                    {
                        // Address register direct mode not allowed
                        return null;
                    }
                    break;

                case OpHandlerID.CHK:
                case OpHandlerID.DIVU:
                case OpHandlerID.DIVS:
                case OpHandlerID.MULU:
                case OpHandlerID.MULS:
                case OpHandlerID.MOVEtoSR:
                    sourceEA = Helpers.GetEAMode(opcode);
                    if (UndefinedEA(sourceEA)) return null;
                    if ((sourceEA & 0b111000) == 0b001000)
                    {
                        // Address register direct mode not allowed
                        return null;
                    }
                    break;

                case OpHandlerID.CMP:
                    destEA = Helpers.GetEAMode(opcode);
                    if (UndefinedEA(destEA)) return null;

                    opSize = Helpers.GetOpSize(opcode);
                    if ((int)opSize == 0x03)
                    {
                        // Illegal OpSize
                        return null;
                    }

                    if ((destEA & 0b111000) == 0b001000 && opSize != OpSize.Long && opSize != OpSize.Word)
                    {
                        // An not allowed unless Word or Long
                        return null;
                    }
                    break;

                case OpHandlerID.TST:
                    destEA = Helpers.GetEAMode(opcode);
                    if (UndefinedEA(destEA)) return null;
                    opSize = Helpers.GetOpSize(opcode);
                    if ((int)opSize == 0x03)
                    {
                        // Illegal OpSize
                        return null;
                    }

                    if ((destEA & 0b111000) == 0b001000 || // An or
                        (destEA & 0b111111) == 0b111100 || // Immediate or
                        (destEA & 0b111010) == 0b111010)   // PCDisp or PCIndex
                    {
                        // Address mode not allowed
                        return null;
                    }
                    break;

                case OpHandlerID.ADDQ:
                case OpHandlerID.SUBQ:
                    destEA = Helpers.GetEAMode(opcode);
                    if (UndefinedEA(destEA)) return null;

                    opSize = Helpers.GetOpSize(opcode);
                    if ((int)opSize == 0x03)
                    {
                        // Illegal OpSize
                        return null;
                    }
                    if ((destEA & 0b111111) == 0b111100 || // Immediate or
                        (destEA & 0b111010) == 0b111010 || // PCDisp or PCIndex
                        ((destEA & 0b111000) == 0b001000) && opSize == OpSize.Byte)  // An with .B
                    {
                        // Address mode not allowed
                        return null;
                    }
                    break;

                case OpHandlerID.SUB:
                case OpHandlerID.ADD:
                    destEA = Helpers.GetEAMode(opcode);
                    if (UndefinedEA(destEA)) return null;

                    opMode = Helpers.GetOpMode(opcode);     // 0b1xx -> EA is destination
                    opSize = Helpers.GetOpSize(opcode);
                    if ((int)opSize == 0x03)
                    {
                        return null;
                    }
                    if ((opMode & 0b100) != 0 &&            // EA is destination
                        ((destEA & 0b110000) == 0b000000 || // Dn or An or
                         (destEA & 0b111111) == 0b111100 || // Immediate or
                         (destEA & 0b111010) == 0b111010))  // PCDisp or PCIndex
                    {
                        // Address mode not allowed
                        return null;
                    }
                    if ((opMode & 0b100) == 0 && opSize == OpSize.Byte && (destEA & 0b111000) == 0b001000) // An not allowed with .B
                    {
                        return null;
                    }
                    if ((opMode & 0b100) == 0 && opSize == OpSize.Byte && (opMode & 0b100) == 0b000 && (destEA & 0b111000) == 0b001000)
                    {
                        // EA is source, can't do byte read from A(n)
                        return null;
                    }
                    break;

                case OpHandlerID.AND:
                case OpHandlerID.OR:
                    destEA = Helpers.GetEAMode(opcode);
                    if (UndefinedEA(destEA)) return null;
                    if ((destEA & 0b111000) == 0b001000)
                    {
                        // An not allowed
                        return null;
                    }
                    opSize = Helpers.GetOpSize(opcode);
                    if ((int)opSize == 0x03)
                    {
                        // Illegal OpSize
                        return null;
                    }
                    opMode = Helpers.GetOpMode(opcode);
                    if ((opMode & 0b100) == 0b100 &&        // EA is destination
                        ((destEA & 0b110000) == 0b000000 || // Dn or An or
                         (destEA & 0b111111) == 0b111100 || // Immediate or
                         (destEA & 0b111010) == 0b111010))  // PCDisp or PCIndex
                    {
                        // Address mode not allowed
                        return null;
                    }
                    break;

                case OpHandlerID.EOR:
                    destEA = Helpers.GetEAMode(opcode);
                    if (UndefinedEA(destEA)) return null;
                    if ((destEA & 0b111000) == 0b001000)
                    {
                        // An not allowed
                        return null;
                    }
                    opSize = Helpers.GetOpSize(opcode);
                    if ((int)opSize == 0x03)
                    {
                        // Illegal OpSize
                        return null;
                    }
                    opMode = Helpers.GetOpMode(opcode);
                    if ((opMode & 0b100) == 0b100 &&        // EA is destination
                        ((destEA & 0b111000) == 0b001000 || // An or
                         (destEA & 0b111111) == 0b111100 || // Immediate or
                         (destEA & 0b111010) == 0b111010))  // PCDisp or PCIndex
                    {
                        // Address mode not allowed
                        return null;
                    }
                    break;

                case OpHandlerID.NBCD:
                case OpHandlerID.TAS:
                case OpHandlerID.Scc:
                    destEA = Helpers.GetEAMode(opcode);
                    if (UndefinedEA(destEA)) return null;
                    if ((destEA & 0b111000) == 0b001000 || // An
                        (destEA & 0b111100) == 0b111100 || // illegal
                        (destEA & 0b111010) == 0b111010)   // illegal
                    {
                        return null;
                    }
                    opSize = OpSize.Byte;
                    break;

                case OpHandlerID.PEA:
                    sourceEA = Helpers.GetEAMode(opcode);
                    if (UndefinedEA(sourceEA)) return null;
                    opSize = OpSize.Long;
                    if ((sourceEA & 0b111000) == 0b000000 || // Dn
                        (sourceEA & 0b111000) == 0b001000 || // An
                        (sourceEA & 0b111000) == 0b011000 || // (An)+
                        (sourceEA & 0b111000) == 0b100000 || // -(An)
                        (sourceEA & 0b111111) == 0b111100)   // Immed
                    {
                        // Address mode not allowed
                        return null;
                    }
                    break;

                case OpHandlerID.EXG:
                    opMode = (byte)((opcode & 0b0000_0000_1111_1000) >> 3);
                    if (opMode != 0b01000 && opMode != 0b01001 && opMode != 0b10001)
                    {
                        // Illegal mode
                        return null;
                    }
                    break;

                case OpHandlerID.JSR:
                case OpHandlerID.JMP:
                case OpHandlerID.LEA:
                    sourceEA = Helpers.GetEAMode(opcode);
                    if (UndefinedEA(sourceEA)) return null;

                    // (An)+, -(An), Dn, An, and immed not allowed
                    if ((sourceEA & 0b111000) == 0b000000 || // Dn
                        (sourceEA & 0b111000) == 0b001000 || // An
                        (sourceEA & 0b111000) == 0b011000 || // (An)+
                        (sourceEA & 0b111000) == 0b100000 || // -(An)
                        (sourceEA & 0b111111) == 0b111100)   // Immed
                    {
                        return null;
                    }
                    break;

                case OpHandlerID.MOVEM:
                    opSize = (opcode & 0x0040) == 0 ? OpSize.Word : OpSize.Long;
                    destEA = Helpers.GetEAMode(opcode);
                    if (UndefinedEA(destEA)) return null;
                    bool regToMem = (opcode & 0b0000_0100_0000_0000) == 0;
                    if ((destEA & 0b111000) == 0b000000 || // Dn
                        (destEA & 0b111000) == 0b001000 || // An
                        (destEA & 0b111111) == 0b111100)   // Immed
                    {
                        return null;
                    }
                    if (regToMem)
                    {
                        // (An)+, PCDisp and PCIndex not allowed
                        if ((destEA & 0b111000) == 0b011000 || // (An)+
                            (destEA & 0b111010) == 0b111010)  // PCDisp or PCIndex
                        {
                            return null;
                        }
                    }
                    else
                    {
                        // -(An) not allowed
                        if ((destEA & 0b111000) == 0b100000)  // -(An)
                        {
                            return null;
                        }
                    }
                    break;

                case OpHandlerID.BRA:
                case OpHandlerID.BSR:
                case OpHandlerID.Bcc:
                    // If the byte displacement value held within the opcode is zero then
                    // we're using a 16-bit displacement in the extension word operand,
                    // otherwise use the byte displacement.
                    if ((opcode & 0x00FF) != 0)
                    {
                        opSize = OpSize.Byte;
                    }
                    break;

                case OpHandlerID.SUBA:
                case OpHandlerID.CMPA:
                case OpHandlerID.ADDA:
                    sourceEA = Helpers.GetEAMode(opcode);
                    if (UndefinedEA(sourceEA)) return null;

                    opMode = Helpers.GetOpMode(opcode);
                    if (opMode == 0b011)
                    {
                        opSize = OpSize.Word;
                    }
                    else if (opMode == 0b111)
                    {
                        opSize = OpSize.Long;
                    }
                    else
                    {
                        return null;
                    }
                    break;

                case OpHandlerID.ASL:
                case OpHandlerID.ASR:
                case OpHandlerID.LSL:
                case OpHandlerID.LSR:
                case OpHandlerID.ROXL:
                case OpHandlerID.ROXR:
                case OpHandlerID.ROL:
                case OpHandlerID.ROR:
                    // If a memory shift then determine the effective address mode.
                    if (((opcode & 0x00C0) >> 6) == 0x03)
                    {
                        sourceEA = Helpers.GetEAMode(opcode);
                        if (UndefinedEA(sourceEA)) return null;
                        opSize = OpSize.Word;
                        if ((sourceEA & 0b111000) == 0b000000 || // Dn
                            (sourceEA & 0b111000) == 0b001000 || // An
                            (sourceEA & 0b111100) == 0b111100 || // not (xxx).W or (xxx).L
                            (sourceEA & 0b111010) == 0b111010)
                        {
                            return null;
                        }
                    }
                    else
                    {
                        opSize = Helpers.GetOpSize(opcode);
                    }
                    break;

                case OpHandlerID.SUBX:
                case OpHandlerID.ADDX:
                case OpHandlerID.CMPM:
                    opSize = Helpers.GetOpSize(opcode);
                    if ((int)opSize == 0x03)
                    {
                        // Illegal OpSize
                        return null;
                    }
                    break;

                default:
                    // All are legal that get here.
                    break;
            }

            // If we get here, the instruction is legal for the given addressing mode(s). It is
            // also one we haven't seen yet since it would have been in the cache.

            // Construct an Instruction object containing all the required operand info except for extension words.
            // Those will be filled in based on the current instruction stream position when the instruction is executed.
            Instruction inst = new(opcode, instInfo, opSize, sourceEA, null, null, destEA, null, null);

            // Cache the legal instruction for future use.  One instruction per opcode, or a max of
            // 65536 entries if all opcodes were valid (but of course that is not the case).
            InstructionCache[opcode] = inst;

            // and return it.
            return inst;
        }
    }
}
