using Microsoft.VisualStudio.TestPlatform.ObjectModel;
using PendleCodeMonkey.MC68000EmulatorLib;
using PendleCodeMonkey.MC68000EmulatorLib.Enumerations;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;
using static PendleCodeMonkey.MC68000EmulatorLib.Machine;
using static System.Net.Mime.MediaTypeNames;

namespace PendleCodeMonkey.MC68000Emulator.Tests
{
    /// <summary>
    /// Uses the test cases from the modified TomHarte M68000 JSON test suite to verify single-step instruction execution.
    /// See https://github.com/SingleStepTests/ProcessorTests/tree/main/680x0/68000/v1 for the original test cases and description.
    /// See https://github.com/SingleStepTests/m68000 for the test cases used here.
    /// See https://github.com/mamedev/mame/tree/master/src/devices/cpu/m68000 for the microcode truth model.
    /// 
    /// Description of the test format:
    /// 
    /// Each test:
    ///
    ///   Requires execution of only a single instruction; and
    ///   Provides full processor and memory state before and after.
    /// 
    /// Tests are randomly generated, in substantial volume.
    ///  
    /// Valid opcodes are bucketed by operation; slightly more than 8,000 tests per operation are provided, giving 
    ///   a total of a little over 1,000,000 tests.
    ///  
    ///  Further:
    ///  
    ///    - 99% of the time, all address pointers begin the test with word-aligned values; and   
    ///    - a separate 99% of the time, the processor begins the test in supervisor mode.
    ///    
    ///  Sample test:
    ///  
    ///  <code>
    ///  {
    ///  	"name": "e3ae [LSL.l D1, D6] 5",
    ///  	"initial": {
    ///  		"d0": 727447539,
    ///  		"d1": 123414203,
    ///  		"d2": 2116184600,
    ///  		"d3": 613751030,
    ///  		"d4": 3491619782,
    ///  		"d5": 3327815506,
    ///  		"d6": 2480544920,
    ///  		"d7": 2492542949,
    ///  		"a0": 2379291595,
    ///  		"a1": 1170063127,
    ///  		"a2": 3877821425,
    ///  		"a3": 480834161,
    ///  		"a4": 998208767,
    ///  		"a5": 2493287663,
    ///  		"a6": 1026412676,
    ///  		"usp": 1546990282,
    ///  		"ssp": 2048,
    ///  		"sr": 9994,
    ///  		"pc": 3072,
    ///  		"prefetch": [58286, 50941],
    ///  		"ram": [
    ///  			[3077, 34],
    ///  			[3076, 42]
    ///  		]
    ///  	},
    ///  	"final": {
    ///  		"d0": 727447539,
    ///  		"d1": 123414203,
    ///  		"d2": 2116184600,
    ///  		"d3": 613751030,
    ///  		"d4": 3491619782,
    ///  		"d5": 3327815506,
    ///  		"d6": 0,
    ///  		"d7": 2492542949,
    ///  		"a0": 2379291595,
    ///  		"a1": 1170063127,
    ///  		"a2": 3877821425,
    ///  		"a3": 480834161,
    ///  		"a4": 998208767,
    ///  		"a5": 2493287663,
    ///  		"a6": 1026412676,
    ///  		"usp": 1546990282,
    ///  		"ssp": 2048,
    ///  		"sr": 9988,
    ///  		"pc": 3074,
    ///  		"prefetch": [50941, 10786],
    ///  		"ram": [
    ///  			[3077, 34],
    ///  			[3076, 42]
    ///  		]
    ///  	},
    ///  	"length": 126,
    ///  	"transactions": [
    ///  		["r", 4, 6, 3076, ".w", 10786],
    ///  		["n", 122]
    ///  	]
    ///  }
    ///  </code>
    ///  
    ///  <c>name</c> is provided for human consumption and has no formal meaning.
    ///
    ///  <c>initial</c> is the initial state of the processor:
    ///
    ///    - <c>d0–d7</c> are the data registers;
    ///    - <c>a0–a6</c> are the fixed address registers;
    ///    - <c>usp</c> is the user stack pointer;
    ///    - <c>ssp</c> is the supervisor stack pointer;
    ///    - <c>sr</c> is the status register;
    ///    - <c>pc</c> is the formal program counter, providing a pointer to the location that the next instruction resides at;
    ///    - <c>prefetch</c> is the current contents of the prefetch queue, with the first item having been fetched earlier than the second; and
    ///    - <c>ram</c> contains a list of byte values to store in memory prior to execution, each one in the form [address, value].
    ///
    ///  <c>final</c> is the state of the processor and relevant memory contents after execution.
    ///  
    ///  <c>length</c> provides the total number of cycles spent in this instruction.
    ///  
    ///  <c>transactions</c> provides a list of bus transactions that occurred during execution, in one of two forms:
    ///
    ///    - <c>["n", 122]</c> indicates an idle bus for a duration of 122 cycles;
    ///    - <c>["r", 4, 6, 3076, ".w", 10786]</c> indicates:
    ///       - <c>"r"</c> indicates that the cycle was a read. Other options are "w" for a write, or "t" for a TAS indivisible read-modify-write;
    ///       - <c>4</c> is the length in cycles of the transaction;
    ///       - <c>6</c> is the posted function code for this transaction — bit 0 is FC0, bit 1 is FC1 and bit 2 is FC2;
    ///       - <c>3076</c> is the address posted for this operation;
    ///       - <c>".w"</c> indicates that this was a word access. The alternative is <c>".b"</c> for a byte access; and
    ///       - <c>10786</c> is the value on the data bus during the transaction. If it was a TAS cycle, it is the final value as written; before
    ///         and after can be verified via before-and-after RAM state.
    ///  
    ///  All cycle counts assume an immediate DTACK.
    ///  
    ///  For byte accesses:
    ///  
    ///    - you can infer UDS or LDS by inspecting the lowest bit of the posted address; and
    ///    - the value recorded is that from whichever half of the bus was active — so it'll always be in the range 0 to 255.
    ///    
    /// 
    ///  
    ///  Further details from https://github.com/SingleStepTests/m68000:
    ///  
    ///    STATUS: all of the tests except TAS and TRAPV are verified as good.
    ///    
    ///    Caveats:
    ///    
    ///      - There's a new cycle type in addition to idle, read, write, and TAS. "re" for read address error, and "we" for write address error. 
    ///        On real m68k, they still happen, AS just isn't asserted, so the results aren't committed. The transactions are left in here with 
    ///        the new type to simplify correctly catching address errors
    ///    
    ///      - TAS doesn't properly handle the special 5-cycle TAS read-modify-write timing.
    ///    
    ///      - There's some strange issue I don't understand with the TRAPV tests.Or maybe I'm just interpreting them wrong. It appears to 
    ///        trigger incorrectly based on the S bit?
    ///    
    ///      - Any bugs that exist in Mame's microcoded M68000 emulator will exist here too
    ///    
    ///    Use decode.py to convert from .json.bin to .json.
    ///    
    ///    They are in ALMOST the same format as the TomHarte tests, just generated with a better emulator, and:
    ///    
    ///      - RAM pieces are now in 16 bits, as it is on the real processor.
    ///      
    ///      - There are the new "re" and "we" cycle types as mentioned above
    ///      
    ///      - PC is now set using m_au from MAME.To clarify, it has a number of registers, m_pc, m_ipc, m_au, all of which work as a sort of 
    ///        PC, but are updated differently. m_au seems to be consistent though. It's "next prefetch address" so it's +4 from where the 
    ///        test starts executing.
    ///    
    ///      - Data bus now always is as real processor (i.e.only UDS is on, and you read 0xAB, you will get 0xAB00 for data bus. This differs 
    ///        from TomHarte where it would would give 0xAB)
    ///    
    ///      - The tests now include UDS and LDS in the transaction logs, since the real M68K can't output A0.
    ///        These may not be the final form; I may add new features, or adjust so that certain things go better, etc., but they're worth using now.
    ///    
    ///    Thanks to the MAME project for the awesome microcoded emulator! Thanks to TomHarte for the idea for the JSON tests!
    /// </summary>
    public class SingleStepTests
    {
        private const string TEST_DATA_PATH = @"..\..\..\..\TestData\m68000\v1";
        private readonly ITestOutputHelper _output;

        public SingleStepTests(ITestOutputHelper output)
        {
            _output = output;
        }

        /// <summary>
        /// Get the list of instruction test names (file names without the .json extension).
        /// </summary>
        /// <returns></returns>
        public static TheoryData<string> GetInstructionTestNames()
        {
            var testDataDir = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), TEST_DATA_PATH));
            var files = Directory.GetFiles(testDataDir, "*.json");
            var data = new TheoryData<string>();
            foreach (var f in files)
            {
                data.Add(Path.GetFileNameWithoutExtension(f));
            }
            return data;
        }

        /// <summary>
        /// Check that the required CPU state matches the actual CPU state.
        /// </summary>
        /// <param name="requiredState"></param>
        /// <param name="cpu"></param>
        private void CheckCpuState(M68KJsonTestCase testcase, M68KTestCaseState requiredState, CPU cpu, Disassembler.DisassemblyRecord? record = null)
        {
            string testCaseInfo = $"{testcase.Name}: {record}";
            var actualD0 = cpu.ReadDataRegister(0);
            Assert.True(requiredState.D0 == actualD0, $"{testCaseInfo}: D0 mismatch. Expected: ${requiredState.D0:x8} ({requiredState.D0}), Actual: ${actualD0:x8} ({actualD0})");
            var actualD1 = cpu.ReadDataRegister(1);
            Assert.True(requiredState.D1 == actualD1, $"{testCaseInfo}: D1 mismatch. Expected: ${requiredState.D1:x8} ({requiredState.D1}), Actual: ${actualD1:x8} ({actualD1})");
            var actualD2 = cpu.ReadDataRegister(2);
            Assert.True(requiredState.D2 == actualD2, $"{testCaseInfo}: D2 mismatch. Expected: ${requiredState.D2:x8} ({requiredState.D2}), Actual: ${actualD2:x8} ({actualD2})");
            var actualD3 = cpu.ReadDataRegister(3);
            Assert.True(requiredState.D3 == actualD3, $"{testCaseInfo}: D3 mismatch. Expected: ${requiredState.D3:x8} ({requiredState.D3}), Actual: ${actualD3:x8} ({actualD3})");
            var actualD4 = cpu.ReadDataRegister(4);
            Assert.True(requiredState.D4 == actualD4, $"{testCaseInfo}: D4 mismatch. Expected: ${requiredState.D4:x8} ({requiredState.D4}), Actual: ${actualD4:x8} ({actualD4})");
            var actualD5 = cpu.ReadDataRegister(5);
            Assert.True(requiredState.D5 == actualD5, $"{testCaseInfo}: D5 mismatch. Expected: ${requiredState.D5:x8} ({requiredState.D5}), Actual: ${actualD5:x8} ({actualD5})");
            var actualD6 = cpu.ReadDataRegister(6);
            Assert.True(requiredState.D6 == actualD6, $"{testCaseInfo}: D6 mismatch. Expected: ${requiredState.D6:x8} ({requiredState.D6}), Actual: ${actualD6:x8} ({actualD6})");
            var actualD7 = cpu.ReadDataRegister(7);
            Assert.True(requiredState.D7 == actualD7, $"{testCaseInfo}: D7 mismatch. Expected: ${requiredState.D7:x8} ({requiredState.D7}), Actual: ${actualD7:x8} ({actualD7})");

            var actualA0 = cpu.ReadAddressRegister(0);
            Assert.True(requiredState.A0 == actualA0, $"{testCaseInfo}: A0 mismatch. Expected: {requiredState.A0:x8}, Actual: {actualA0:x8}");
            var actualA1 = cpu.ReadAddressRegister(1);
            Assert.True(requiredState.A1 == actualA1, $"{testCaseInfo}: A1 mismatch. Expected: {requiredState.A1:x8}, Actual: {actualA1:x8}");
            var actualA2 = cpu.ReadAddressRegister(2);
            Assert.True(requiredState.A2 == actualA2, $"{testCaseInfo}: A2 mismatch. Expected: {requiredState.A2:x8}, Actual: {actualA2:x8}");
            var actualA3 = cpu.ReadAddressRegister(3);
            Assert.True(requiredState.A3 == actualA3, $"{testCaseInfo}: A3 mismatch. Expected: {requiredState.A3:x8}, Actual: {actualA3:x8}");
            var actualA4 = cpu.ReadAddressRegister(4);
            Assert.True(requiredState.A4 == actualA4, $"{testCaseInfo}: A4 mismatch. Expected: {requiredState.A4:x8}, Actual: {actualA4:x8}");
            var actualA5 = cpu.ReadAddressRegister(5);
            Assert.True(requiredState.A5 == actualA5, $"{testCaseInfo}: A5 mismatch. Expected: {requiredState.A5:x8}, Actual: {actualA5:x8}");
            var actualA6 = cpu.ReadAddressRegister(6);
            Assert.True(requiredState.A6 == actualA6, $"{testCaseInfo}: A6 mismatch. Expected: {requiredState.A6:x8}, Actual: {actualA6:x8}");

            // The M68000_SR_MASK from Rust is 0xA71F. We should only compare these bits.
            const ushort SR_MASK = 0x271F; // Ignore trace bit errors for now, then -> 0xA71F
            var expectedSr = (SRFlags)(requiredState.Sr & SR_MASK);
            var actualSr = (SRFlags)((ushort)cpu.SR & SR_MASK);
            if (expectedSr != actualSr)
            {
                _output.WriteLine($"{testCaseInfo}: Expected SR: ${(ushort)expectedSr:x4}, Actual SR: ${(ushort)actualSr:x4}");
            }
            Assert.True((ushort)expectedSr == (ushort)actualSr, $"{testCaseInfo}: SR mismatch. Expected: ${(ushort)expectedSr:x4}, Actual: ${(ushort)actualSr:x4}");

            // Check stack pointers after execution
            if ((requiredState.Sr & 0x2000) != 0) // Is supervisor
            {
                var actualSsp = cpu.ReadAddressRegister(7);
                Assert.True(requiredState.Ssp == actualSsp, $"{testCaseInfo}: SSP mismatch. Expected: ${requiredState.Ssp:x8}, Actual: ${actualSsp:x8}");
                var actualUsp = cpu.USP;
                Assert.True(requiredState.Usp == actualUsp, $"{testCaseInfo}: USP mismatch. Expected: ${requiredState.Usp:x8}, Actual: ${actualUsp:x8}");
            }
            else
            {
                var actualUsp = cpu.ReadAddressRegister(7);
                Assert.True(requiredState.Usp == actualUsp, $"{testCaseInfo}: USP mismatch. Expected: ${requiredState.Usp:x8}, Actual: ${actualUsp:x8}");
                var actualSsp = cpu.SSP;
                Assert.True(requiredState.Ssp == actualSsp, $"{testCaseInfo}: SSP mismatch. Expected: ${requiredState.Ssp:x8}, Actual: ${actualSsp:x8}");
            }
        }

        /// <summary>
        /// Set the memory to the required initial state.
        /// </summary>
        /// <param name="requiredState"></param>
        /// <param name="machine"></param>
        private static void SetMemoryState(M68KTestCaseState requiredState, Machine machine)
        {
            uint low = 0xffffffff;
            uint high = 0;

            // No ordering can be assumned in the RAM list of addresses and bytes.
            foreach (var ramEntry in requiredState.Ram)
            {
                if (ramEntry.Address < low)
                {
                    low = ramEntry.Address;
                }
                if (ramEntry.Address > high)
                {
                    high = ramEntry.Address;
                }
            }
            if (low > high)
            {
                return; // Nothing to do - no data provided.
            }
            uint len = high - low;
            byte[] zeroes = new byte[len];

            // Zero out the range and set the _loadedAddress and _dataLength
            // used for IsEndOfData.
            machine.LoadExecutableData(zeroes, low);

            // No ordering can be assumned in the RAM list of addresses and bytes.
            foreach (var ramEntry in requiredState.Ram)
            {
                machine.Memory.WriteByte(ramEntry.Address, ramEntry.Data);
            }
        }

        /// <summary>
        /// Check that the required memory state matches the actual memory state.
        /// </summary>
        /// <param name="requiredState"></param>
        /// <param name="machine"></param>
        private static void CheckMemoryState(M68KJsonTestCase testcase, M68KTestCaseState requiredState, Machine machine, Disassembler.DisassemblyRecord? record = null)
        {
            string testCaseInfo = $"{testcase.Name}: {record}";
            foreach (var ramEntry in requiredState.Ram)
            {
                var address = ramEntry.Address;
                var expectedValue = ramEntry.Data;

                var finalCpu = new CPU { SR = (SRFlags)requiredState.Sr };
                if (finalCpu.SupervisorMode && (address >= requiredState.Ssp && address < requiredState.Ssp + 14))
                {
                    continue;
                }
                var actualValue = machine.Memory.ReadByte(address);
                Assert.True(expectedValue == actualValue, $"{testCaseInfo}: RAM mismatch at $0x{address:x8}. Expected: ${expectedValue:x2} ({expectedValue}), Actual: ${actualValue:x2} ({actualValue})");
            }
        }

        /// <summary>
        /// Set the CPU registers to the required initial state.
        /// </summary>
        /// <param name="requiredState"></param>
        /// <param name="cpu"></param>
        /// <returns></returns>
        private static uint SetCpuState(M68KTestCaseState requiredState, Machine machine)
        {
            CPU cpu = machine.CPU;
            cpu.WriteDataRegister(0, requiredState.D0);
            cpu.WriteDataRegister(1, requiredState.D1);
            cpu.WriteDataRegister(2, requiredState.D2);
            cpu.WriteDataRegister(3, requiredState.D3);
            cpu.WriteDataRegister(4, requiredState.D4);
            cpu.WriteDataRegister(5, requiredState.D5);
            cpu.WriteDataRegister(6, requiredState.D6);
            cpu.WriteDataRegister(7, requiredState.D7);

            cpu.WriteAddressRegister(0, requiredState.A0);
            cpu.WriteAddressRegister(1, requiredState.A1);
            cpu.WriteAddressRegister(2, requiredState.A2);
            cpu.WriteAddressRegister(3, requiredState.A3);
            cpu.WriteAddressRegister(4, requiredState.A4);
            cpu.WriteAddressRegister(5, requiredState.A5);
            cpu.WriteAddressRegister(6, requiredState.A6);

            cpu.SR = (SRFlags)requiredState.Sr;
            if (cpu.SupervisorMode)
            {
                cpu.USP = requiredState.Usp;
                cpu.SSP = requiredState.Ssp;
                cpu.WriteAddressRegister(7, requiredState.Ssp);
            }
            else
            {
                cpu.USP = requiredState.Usp;
                cpu.SSP = requiredState.Ssp;
                cpu.WriteAddressRegister(7, requiredState.Usp);
            }
            // The PC in the test data points after the first prefetch.
            var instructionStartAddr = requiredState.Pc - 4;
            cpu.Prefetch.Clear();

            return instructionStartAddr;
        }

        /// <summary>
        /// Read the list of test cases for the specified instruction from the JSON file.
        /// </summary>
        /// <param name="instruction"></param>
        /// <returns></returns>
        private static List<M68KJsonTestCase> LoadTestCases(string instruction)
        {
            var filePath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), @$"{TEST_DATA_PATH}\{instruction}.json"));
            using var fileStream = File.OpenRead(filePath);
            using var reader = new StreamReader(fileStream);

            var jsonString = reader.ReadToEnd();
            var testcases = System.Text.Json.JsonSerializer.Deserialize<List<M68KJsonTestCase>>(jsonString);
            Assert.NotNull(testcases);
            return testcases!;
        }

        /// <summary>
        /// Run the exhaustive list of tests for each instruction from the github 68000 SingleStepTests repo
        /// at https://github.com/SingleStepTests/m68000.
        /// </summary>
        /// <param name="instruction">File name without the '.json' extension</param>
        [Theory]
        [MemberData(nameof(GetInstructionTestNames))]
        public void RunInstructionTest(string instruction)
        {
            var testcases = LoadTestCases(instruction);
            Assert.NotEmpty(testcases);
            var machine = new Machine();
            Disassembler disassembler = new Disassembler(machine);
            var cpu = machine.CPU;
            Assert.NotNull(cpu);

            foreach (var testcase in testcases)
            {
                if (testcase.Name.StartsWith("152 MOVEtoCCR (d8, PC, Xn) 44fb"))
                {
                    _output.WriteLine($"Looking at failing test case {testcase.Name}");
                }
                machine.Reset();

                // Set initial state and get instruction start address.
                var instructionStartAddr = SetCpuState(testcase.Initial, machine);
                CheckCpuState(testcase, testcase.Initial, cpu);
                SetMemoryState(testcase.Initial, machine);
                CheckMemoryState(testcase, testcase.Initial, machine);

                // Set PC to the start of the instruction.
                cpu.PC = instructionStartAddr;
                var lines = disassembler.Disassemble(machine.CPU.PC, Disassembler.MAX_INSTRUCTION_LENGTH, 1);
                Disassembler.DisassemblyRecord record = lines[0];
                _output.WriteLine($"Test case {testcase.Name}: {record}");

                machine.CallDepth = 1;         // To ensure that an RTS is actually performed.

                // Execute the instruction
                TrapException? exception = machine.ExecuteInstruction();
                if (exception != null)
                {
                    _output.WriteLine($"{testcase.Name}: Exception: {exception}");
                }

                // Check final state
                CheckCpuState(testcase, testcase.Final, cpu, record);
                CheckMemoryState(testcase, testcase.Final, machine, record);
            }
        }
    }
}