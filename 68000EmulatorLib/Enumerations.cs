using System;
using System.Numerics;

namespace PendleCodeMonkey.MC68000EmulatorLib.Enumerations
{
    /// <summary>
    /// Enumeration of the processor flags.
    /// </summary>
    [Flags]
    public enum SRFlags : ushort
    {
        Carry = 0x01,
        Overflow = 0x02,
        Zero = 0x04,
        Negative = 0x08,
        Extend = 0x10,
        SupervisorMode = 0x2000,
        TraceMode = 0x8000,
        InterruptLevel = 0x0700
    };

    /// <summary>
    /// Enumeration of the possible operation sizes (byte, word, or long)
    /// </summary>
    public enum OpSize : byte
    {
        Byte = 0x00,
        Word = 0x01,
        Long = 0x02
    };

    /// <summary>
    /// Enumeration of the two effective address types (source or destination)
    /// </summary>
    public enum EAType : byte
    {
        Source = 0x00,
        Destination = 0x01
    };

    /// <summary>
    /// Enumeration of the BCD operation types - Addition (for ABCD instruction) or Subtraction (for SBCD instruction)
    /// </summary>
    public enum BCDOperation : byte
    {
        Addition = 0x00,
        Subtraction = 0x01
    };

    /// <summary>
    /// Enumeration of the possible addressing modes
    /// </summary>
    public enum AddrMode : byte
    {
        DataRegister = 0x00,        // Dn
        AddressRegister = 0x08,     // An
        Address = 0x10,             // (An)
        AddressPostInc = 0x18,      // (An)+
        AddressPreDec = 0x20,       // -(An)
        AddressDisp = 0x28,         // (d,An)
        AddressIndex = 0x30,        // (d,An,Xn)
        AbsShort = 0x38,            // (xxx).W
        AbsLong = 0x39,             // (xxx).L
        PCDisp = 0x3A,              // (d,PC)
        PCIndex = 0x3B,             // (d,PC,Xn)
        Immediate = 0x3C            // #<data>
    };

    /// <summary>
    /// Enumeration of the possible condition values (for branch instructions, etc.)
    /// </summary>
    public enum Condition : byte
    {
        T = 0x00,       // True
        F = 0x01,       // False
        HI = 0x02,      // Higher
        LS = 0x03,      // Lower or Same
        CC = 0x04,      // Carry Clear
        CS = 0x05,      // Carry Set
        NE = 0x06,      // Not Equal
        EQ = 0x07,      // Equal
        VC = 0x08,      // Overflow Clear
        VS = 0x09,      // Overflow Set
        PL = 0x0A,      // Plus
        MI = 0x0B,      // Minus
        GE = 0x0C,      // Greater or Equal
        LT = 0x0D,      // Less Than
        GT = 0x0E,      // Greater Than
        LE = 0x0F       // Less or Equal
    };

    /// <summary>
    /// This enumeration doubles as the vector number.  Multiply by 4
    /// to get the low-memory address used by the vector).
    /// <c>
    /// 
    /// Vector Numbers       Address     Space(6)    Assignment
    /// ===================  ==========  ==========  ===============================
    /// Hex      Decimal     Dec   Hex   
    ///  0        0           0    000   SP          Reset: Initial SSP(2)
    ///  1        1           4    004   SP          Reset: Initial PC(2)
    ///  2        2           8    008   SD          Bus Error
    ///  3        3          12    00C   SD          Address Error
    ///  4        4          16    010   SD          Illegal instruction
    ///  5        5          20    014   SD          Zero Divide
    ///  6        6          24    018   SD          CHK Instruction
    ///  7        7          28    01C   SD          TRAPV Instruction
    ///  8        8          32    020   SD          Privilege Violation
    ///  9        9          36    024   SD          Trace
    ///  A       10          40    028   SD          Line 1010 Emulator
    ///  B       11          44    02C   SD          Line 1111 Emulator
    ///  C       12(1)       48    030   SD          (Unassigned, Reserved)
    ///  D       13(1)       52    034   SD          (Unassigned, Reserved)
    ///  E       14          56    038   SD          Format Error(5)
    ///  F       15          60    03C   SD          Uninitialized Interrupt Vector
    /// 10-17    16-23(1)    64    040   SD          (Unassigned, Reserved)
    ///                      92    05C               -
    /// 18       24          96    060   SD          Spurious Interrupt(3)
    /// 19       25          100   064   SD          Level 1 Interrupt Autovector
    /// 1A       26          104   068   SD          Level 2 Interrupt Autovector
    /// 1B       27          108   06C   SD          Level 3 Interrupt Autovector
    /// 1C       28          112   070   SD          Level 4 Interrupt Autovector
    /// 1D       29          116   074   SD          Level 5 Interrupt Autovector
    /// 1E       30          120   078   SD          Level 6 Interrupt Autovector
    /// 1F       31          124   07C   SD          Level 7 Interrupt Autovector
    /// 20-2F    32-47       128   080   SD          TRAP Instruction Vectors(4)
    ///                      188   0BC               -
    /// 30-3F    48-63(1)    192   0C0   SD          (Unassigned, Reserved)
    ///                      255   0FF               -
    /// 40-FF    64-255      256   100   SD          User Interrupt Vectors
    ///                      1020  3FC               -
    ///                      
    /// NOTES:
    /// 1. Vector numbers 12, 13, 16–23, and 48–63 are reserved for future
    /// enhancements by Motorola.No user peripheral devices should be
    /// assigned these numbers.
    /// 2. Reset vector (0) requires four words, unlike the other vectors which only
    /// require two words, and is located in the supervisor program space.
    /// 3. The spurious interrupt vector is taken when there is a bus error
    /// indication during interrupt processing.
    /// 4. TRAP #n uses vector number 32+ n.
    /// 5. MC68010 only. This vector is unassigned, reserved on the MC68000
    /// and MC68008.
    /// 6. SP denotes supervisor program space, and SD denotes
    /// supervisor data space.
    /// </c>
    /// </summary>
    public enum TrapVector : byte
    {
        ResetSSP = 0,
        ResetPC = 1,
        BusError = 2,
        AddressError = 3,
        IllegalInstruction = 4,
        DivideByZero = 5,
        CHKInstruction = 6,
        TRAPVInstruction = 7,
        PrivilegeViolation = 8,
        Trace = 9,
        LineAInstruction = 10,
        LineFInstruction = 11,
        UninitializedInterrupt = 15,
        SpuriousInterrupt = 24,
        Interrupt = 25,
        MaxInterrupt = 31,
        TrapInstruction = 32,
        MaxTrapInstruction = 47,
        UserInterrupt = 64,
        MaxUserInterrupt = 255
    }

    /// <summary>
    /// Function codes.
    /// <c>
    /// 
    /// NOTES:
    /// 1. Vector numbers 12, 13, 16-23, and 48-63 are reserved for future
    ///    enhancements by Motorola. No user peripheral devices should be
    ///    assigned these numbers.
    /// 2. Reset vector (0) requires four words, unlike the other vectors which only
    ///    require two words, and is located in the supervisor program space.
    /// 3. The spurious interrupt vector is taken when there is a bus error
    ///    indication during interrupt processing.
    /// 4. TRAP #n uses vector number 32 + n.
    /// 5. MC68010 only. This vector is unassigned, reserved on the MC68000
    ///    and MC68008.
    /// 6. SP denotes supervisor program space, and SD denotes
    ///    supervisor data space.
    ///
    ///    M68000 Microprocessors User's Manual
    ///    Ninth Edition
    ///    
    ///    Function Code Output
    ///    FC2   FC1   FC0   Code  Address Space Type
    ///    ====  ====  ====  ====  =====================
    ///    Low   Low   Low   000   (Undefined, Reserved)*
    ///    Low   Low   High  001   User Data
    ///    Low   High  Low   010   User Program
    ///    Low   High  High  011   (Undefined, Reserved)*
    ///    High  Low   Low   100   (Undefined, Reserved)*
    ///    High  Low   High  101   Supervisor Data
    ///    High  High  Low   110   Supervisor Program
    ///    High  High  High  111   CPU Space
    /// 
    ///    * Address space 3 is reserved for user definition, while 0 and
    ///      4 are reserved for future use by Motorola.
    /// </c>
    /// </summary>
    public enum FC : byte
    {
        Zero = 0b000,
        UserData = 0b001,
        UserProgram = 0b010,
        Undef4 = 0b011,
        Undef5 = 0b100,
        SupervisorData = 0b101,
        SupervisorProgram = 0b110,
        CPUSpace = 0b111
    }

    /// <summary>
    /// Exception Read/Write flag
    /// </summary>
    public enum Erw { Read, Write }

    /// <summary>
    /// Exception Groups
    /// <c>
    ///   Exception Grouping and Priority
    ///   Group  Kind           Exception Processing
    ///   =====  =============  =================================================================
    ///   0      Reset
    ///          Address Error
    ///          Bus Error
    ///                         Exception Processing Begins within Two Clock Cycles, current 
    ///                         instruction is aborted
    ///   1      Trace
    ///          Interrupt
    ///          Illegal
    ///          Privilege
    ///                         Exception Processing Begins before the Next Instruction
    ///   2      TRAP 
    ///          TRAPV
    ///          CHK
    ///          Zero Divide
    ///                         Exception Processing Is Started by Normal Instruction Execution
    ///</c>
    /// </summary>
    public enum EG : byte
    {
        Group0 = 0,
        Group1 = 1,
        Group2 = 2
    }

    public struct ExceptionDetails
    {
        public ExceptionDetails(TrapVector vector, EG group, FC fc, string description)
        {
            Vector = vector;
            Group = group;
            Fc = fc;
            IsInterrupt = vector >= TrapVector.Interrupt && vector <= TrapVector.MaxInterrupt;
            Description = description;
        }

        public ExceptionDetails(ExceptionDetails entry)
        {
            Vector = entry.Vector;
            Group = entry.Group;
            Fc = entry.Fc;
            IsInterrupt = entry.IsInterrupt;
            Description = entry.Description;
        }

        public TrapVector Vector { get; private set; }
        public EG Group { get; private set; }
        public FC Fc { get; private set; }
        public bool IsInterrupt { get; private set; }
        public string Description { get; private set; }
        public bool IsDefined { get; private set; } = true;

        /// <summary>
        /// Get an EVEntry for the specified vector.
        /// </summary>
        /// <param name="vector"></param>
        /// <returns></returns>
        public static ExceptionDetails FromVector(TrapVector vector)
        {
            ExceptionDetails evEntry;
            bool setVector = false;
            switch ((TrapVector)(byte)vector)
            {
                case TrapVector v when v >= TrapVector.Interrupt && v <= TrapVector.MaxInterrupt:
                    setVector = EVTable.TryGetValue(TrapVector.Interrupt, out evEntry);
                    break;
                case TrapVector v when v >= TrapVector.TrapInstruction && v <= TrapVector.MaxTrapInstruction:
                    setVector = EVTable.TryGetValue(TrapVector.TrapInstruction, out evEntry);
                    break;
                case TrapVector v when v >= TrapVector.UserInterrupt && v <= TrapVector.MaxUserInterrupt:
                    setVector = EVTable.TryGetValue(TrapVector.UserInterrupt, out evEntry);
                    break;
                default:
                    if (!EVTable.TryGetValue(vector, out evEntry))
                    {
                        evEntry = new(vector, EG.Group2, FC.SupervisorData, "Undefined ExceptionDetails");
                        evEntry.IsDefined = false;
                        return evEntry;
                    }
                    break;
            }
            if (setVector)
            {
                evEntry = new(evEntry);
                evEntry.Vector = vector;
            }
            return evEntry;
        }

        public static Dictionary<TrapVector, ExceptionDetails> EVTable { get; } = new()
        {
            {TrapVector.ResetSSP,               new ExceptionDetails(TrapVector.ResetSSP,               EG.Group0, FC.SupervisorProgram, TrapException.Description((ushort)TrapVector.ResetSSP              ))},
            {TrapVector.ResetPC,                new ExceptionDetails(TrapVector.ResetPC,                EG.Group0, FC.SupervisorProgram, TrapException.Description((ushort)TrapVector.ResetPC               ))},
            {TrapVector.BusError,               new ExceptionDetails(TrapVector.BusError,               EG.Group0, FC.SupervisorData   , TrapException.Description((ushort)TrapVector.BusError              ))},
            {TrapVector.AddressError,           new ExceptionDetails(TrapVector.AddressError,           EG.Group0, FC.SupervisorData   , TrapException.Description((ushort)TrapVector.AddressError          ))},
            {TrapVector.IllegalInstruction,     new ExceptionDetails(TrapVector.IllegalInstruction,     EG.Group1, FC.SupervisorData   , TrapException.Description((ushort)TrapVector.IllegalInstruction    ))},
            {TrapVector.DivideByZero,           new ExceptionDetails(TrapVector.DivideByZero,           EG.Group2, FC.SupervisorData   , TrapException.Description((ushort)TrapVector.DivideByZero          ))},
            {TrapVector.CHKInstruction,         new ExceptionDetails(TrapVector.CHKInstruction,         EG.Group2, FC.SupervisorData   , TrapException.Description((ushort)TrapVector.CHKInstruction        ))},
            {TrapVector.TRAPVInstruction,       new ExceptionDetails(TrapVector.TRAPVInstruction,       EG.Group2, FC.SupervisorData   , TrapException.Description((ushort)TrapVector.TRAPVInstruction      ))},
            {TrapVector.PrivilegeViolation,     new ExceptionDetails(TrapVector.PrivilegeViolation,     EG.Group1, FC.SupervisorData   , TrapException.Description((ushort)TrapVector.PrivilegeViolation    ))},
            {TrapVector.Trace,                  new ExceptionDetails(TrapVector.Trace,                  EG.Group1, FC.SupervisorData   , TrapException.Description((ushort)TrapVector.Trace                 ))},
            {TrapVector.LineAInstruction,       new ExceptionDetails(TrapVector.LineAInstruction,       EG.Group2, FC.SupervisorData   , TrapException.Description((ushort)TrapVector.LineAInstruction      ))},
            {TrapVector.LineFInstruction,       new ExceptionDetails(TrapVector.LineFInstruction,       EG.Group2, FC.SupervisorData   , TrapException.Description((ushort)TrapVector.LineFInstruction      ))},
            {TrapVector.UninitializedInterrupt, new ExceptionDetails(TrapVector.UninitializedInterrupt, EG.Group1, FC.SupervisorData   , TrapException.Description((ushort)TrapVector.UninitializedInterrupt))},
            {TrapVector.SpuriousInterrupt,      new ExceptionDetails(TrapVector.SpuriousInterrupt,      EG.Group1, FC.SupervisorData   , TrapException.Description((ushort)TrapVector.SpuriousInterrupt     ))},
            {TrapVector.Interrupt,              new ExceptionDetails(TrapVector.Interrupt,              EG.Group1, FC.SupervisorData   , TrapException.Description((ushort)TrapVector.Interrupt             ))},
            {TrapVector.MaxInterrupt,           new ExceptionDetails(TrapVector.MaxInterrupt,           EG.Group1, FC.SupervisorData   , TrapException.Description((ushort)TrapVector.MaxInterrupt          ))},
            {TrapVector.TrapInstruction,        new ExceptionDetails(TrapVector.TrapInstruction,        EG.Group2, FC.SupervisorData   , TrapException.Description((ushort)TrapVector.TrapInstruction       ))},
            {TrapVector.MaxTrapInstruction,     new ExceptionDetails(TrapVector.MaxTrapInstruction,     EG.Group2, FC.SupervisorData   , TrapException.Description((ushort)TrapVector.MaxTrapInstruction    ))},
            {TrapVector.UserInterrupt,          new ExceptionDetails(TrapVector.UserInterrupt,          EG.Group1, FC.SupervisorData   , TrapException.Description((ushort)TrapVector.UserInterrupt         ))},
            {TrapVector.MaxUserInterrupt,       new ExceptionDetails(TrapVector.MaxUserInterrupt,       EG.Group1, FC.SupervisorData   , TrapException.Description((ushort)TrapVector.MaxUserInterrupt      ))}
        };

    };


    /// <summary>
    /// Enumeration of Operation Handler identifiers.
    /// </summary>
    public enum OpHandlerID : byte
    {
        NONE,

        ORItoCCR,
        ORItoSR,
        ORI,
        ANDItoCCR,
        ANDItoSR,
        ANDI,
        SUBI,
        ADDI,
        EORItoCCR,
        EORItoSR,
        EORI,
        CMPI,
        BTST,
        BCHG,
        BCLR,
        BSET,
        MOVEP,
        MOVE,
        MOVEA,
        MOVEfromSR,
        MOVEtoCCR,
        MOVEtoSR,
        NEGX,
        CLR,
        NEG,
        NOT,
        EXT,
        NBCD,
        SWAP,
        PEA,
        ILLEGAL,
        TAS,
        TST,
        TRAP,
        LINK,
        UNLK,
        MOVEUSP,
        RESET,
        NOP,
        STOP,
        RTE,
        RTS,
        TRAPV,
        RTR,
        JSR,
        JMP,
        MOVEM,
        CHK,
        LEA,
        DBcc,
        Scc,
        ADDQ,
        SUBQ,
        BRA,
        BSR,
        Bcc,
        MOVEQ,
        DIVU,
        DIVS,
        SBCD,
        OR,
        SUBA,
        SUBX,
        SUB,
        CMPA,
        CMPM,
        EOR,
        CMP,
        MULU,
        MULS,
        ABCD,
        EXG,
        AND,
        ADDA,
        ADDX,
        ADD,
        ASR,
        ASL,
        LSR,
        LSL,
        ROXR,
        ROXL,
        ROR,
        ROL,
        LINEA,
        LINEF
    };
}
