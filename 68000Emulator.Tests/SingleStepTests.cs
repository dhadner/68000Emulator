using PendleCodeMonkey.MC68000EmulatorLib;
using PendleCodeMonkey.MC68000EmulatorLib.Enumerations;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;
using Xunit.Abstractions;

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
    ///  Valid opcodes are bucketed by operation; slightly more than 8,000 tests per operation are provided, giving 
    ///  a total of a little over 1,000,000 tests.
    ///  
    ///  Further:
    ///  
    ///    - 99% of the time, all address pointers begin the test with word-aligned values; and   
    ///    - a separate 99% of the time, the processor begins the test in supervisor mode.
    ///    
    ///  Sample test:
    ///  
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
    ///  
    ///  name is provided for human consumption and has no formal meaning.
    ///  
    ///  initial is the initial state of the processor:
    ///  
    ///    - d0–d7 are the data registers;
    ///    - a0–a6 are the fixed address registers;
    ///    - usp is the user stack pointer;
    ///    - ssp is the supervisor stack pointer;
    ///    - sr is the status register;
    ///    - pc is the formal program counter, providing a pointer to the location that the next instruction resides at;
    ///    - prefetch is the current contents of the prefetch queue, with the first item having been fetched earlier than the second; and
    ///    - ram contains a list of byte values to store in memory prior to execution, each one in the form [address, value].
    ///    
    ///  final is the state of the processor and relevant memory contents after execution.
    ///  
    ///  length provides the total number of cycles spent in this instruction.
    ///  
    ///  transactions provides a list of bus transactions that occurred during execution, in one of two forms:
    ///  
    ///    - ["n", 122] indicates an idle bus for a duration of 122 cycles;
    ///    - ["r", 4, 6, 3076, ".w", 10786] indicates:
    ///    - "r" indicates that the cycle was a read. Other options are "w" for a write, or "t" for a TAS indivisible read-modify-write;
    ///    - 4 is the length in cycles of the transaction;
    ///    - 6 is the posted function code for this transaction — bit 0 is FC0, bit 1 is FC1 and bit 2 is FC2;
    ///    - 3072 is the address posted for this operation;
    ///    - ".w" indicates that this was a word access. The alternative is ".b" for a byte access; and
    ///    - 10786 is the value on the data bus during the transaction. If it was a TAS cycle, it is the final value as written; before 
    ///      and after can be verified via before-and-after RAM state.
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

        [Theory]
        [MemberData(nameof(GetInstructionTestNames))]
        public void RunInstructionTest(string instruction)
        {
            var filePath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), @$"{TEST_DATA_PATH}\{instruction}.json"));

            using var fileStream = File.OpenRead(filePath);
            using var reader = new StreamReader(fileStream);
        
            var jsonString = reader.ReadToEnd();
            var testcases = JsonSerializer.Deserialize<List<M68kJsonTestCase>>(jsonString);

            foreach (var testcase in testcases)
            {
                var machine = new Machine();
                var cpu = machine.CPU;

                // Set initial state for registers
                cpu.WriteDataRegister(0, testcase.Initial.D0);
                cpu.WriteDataRegister(1, testcase.Initial.D1);
                cpu.WriteDataRegister(2, testcase.Initial.D2);
                cpu.WriteDataRegister(3, testcase.Initial.D3);
                cpu.WriteDataRegister(4, testcase.Initial.D4);
                cpu.WriteDataRegister(5, testcase.Initial.D5);
                cpu.WriteDataRegister(6, testcase.Initial.D6);
                cpu.WriteDataRegister(7, testcase.Initial.D7);

                cpu.WriteAddressRegister(0, testcase.Initial.A0);
                cpu.WriteAddressRegister(1, testcase.Initial.A1);
                cpu.WriteAddressRegister(2, testcase.Initial.A2);
                cpu.WriteAddressRegister(3, testcase.Initial.A3);
                cpu.WriteAddressRegister(4, testcase.Initial.A4);
                cpu.WriteAddressRegister(5, testcase.Initial.A5);
                cpu.WriteAddressRegister(6, testcase.Initial.A6);

                // Set stack pointers based on supervisor state
                cpu.SR = (SRFlags)testcase.Initial.Sr;
                if (cpu.SupervisorMode)
                {
                    cpu.USP = testcase.Initial.Usp;
                    cpu.SSP = testcase.Initial.Ssp;
                    cpu.WriteAddressRegister(7, testcase.Initial.Ssp);
                }
                else
                {
                    cpu.USP = testcase.Initial.Usp;
                    cpu.SSP = testcase.Initial.Ssp; // Set SSP even in user mode
                    cpu.WriteAddressRegister(7, testcase.Initial.Usp);
                }

                // The PC in the test data points after the first prefetch.
                // The Rust emulator subtracts 4 (2 words). Our C# emulator doesn't do prefetch,
                // and Step() expects PC to point at the instruction opcode.
                var instructionStartAddr = testcase.Initial.Pc - 4;

                // Separate executable code from other RAM data.
                var ramData = testcase.Initial.Ram.ToDictionary(r => (uint)r[0], r => (byte)r[1]);
                var executableCode = new List<byte>();
                var currentAddr = instructionStartAddr;
                while (ramData.TryGetValue(currentAddr, out var opByte))
                {
                    executableCode.Add(opByte);
                    ramData.Remove(currentAddr);
                    currentAddr++;
                }

                // Load the contiguous executable code block.
                if (executableCode.Count > 0)
                {
                    machine.LoadExecutableData(executableCode.ToArray(), instructionStartAddr, false);
                }

                // Write the remaining disparate RAM entries.
                foreach (var ramEntry in ramData)
                {
                    machine.Memory.WriteByte(ramEntry.Key, ramEntry.Value);
                }

                // Set PC to the start of the instruction.
                cpu.PC = instructionStartAddr;

                // Execute one instruction
                TrapException? trapException = machine.ExecuteInstruction();

                if (trapException != null)
                {
                    _output.WriteLine($"Instruction: {instruction}, Test Case: {testcase.Name} had exception: {trapException}");
                }

                // Assert final state
                var actualD0 = cpu.ReadDataRegister(0);
                Assert.True(testcase.Final.D0 == actualD0, $"D0 mismatch. Expected: ${testcase.Final.D0:x8} ({testcase.Final.D0}), Actual: ${actualD0:x8} ({actualD0})");
                var actualD1 = cpu.ReadDataRegister(1);
                Assert.True(testcase.Final.D1 == actualD1, $"D1 mismatch. Expected: ${testcase.Final.D1:x8} ({testcase.Final.D1}), Actual: ${actualD1:x8} ({actualD1})");
                var actualD2 = cpu.ReadDataRegister(2);
                Assert.True(testcase.Final.D2 == actualD2, $"D2 mismatch. Expected: ${testcase.Final.D2:x8} ({testcase.Final.D2}), Actual: ${actualD2:x8} ({actualD2})");
                var actualD3 = cpu.ReadDataRegister(3);
                Assert.True(testcase.Final.D3 == actualD3, $"D3 mismatch. Expected: ${testcase.Final.D3:x8} ({testcase.Final.D3}), Actual: ${actualD3:x8} ({actualD3})");
                var actualD4 = cpu.ReadDataRegister(4);
                Assert.True(testcase.Final.D4 == actualD4, $"D4 mismatch. Expected: ${testcase.Final.D4:x8} ({testcase.Final.D4}), Actual: ${actualD4:x8} ({actualD4})");
                var actualD5 = cpu.ReadDataRegister(5);
                Assert.True(testcase.Final.D5 == actualD5, $"D5 mismatch. Expected: ${testcase.Final.D5:x8} ({testcase.Final.D5}), Actual: ${actualD5:x8} ({actualD5})");
                var actualD6 = cpu.ReadDataRegister(6);
                Assert.True(testcase.Final.D6 == actualD6, $"D6 mismatch. Expected: ${testcase.Final.D6:x8} ({testcase.Final.D6}), Actual: ${actualD6:x8} ({actualD6})");
                var actualD7 = cpu.ReadDataRegister(7);
                Assert.True(testcase.Final.D7 == actualD7, $"D7 mismatch. Expected: ${testcase.Final.D7:x8} ({testcase.Final.D7}), Actual: ${actualD7:x8} ({actualD7})");

                var actualA0 = cpu.ReadAddressRegister(0);
                Assert.True(testcase.Final.A0 == actualA0, $"A0 mismatch. Expected: {testcase.Final.A0:x8}, Actual: {actualA0:x8}");
                var actualA1 = cpu.ReadAddressRegister(1);
                Assert.True(testcase.Final.A1 == actualA1, $"A1 mismatch. Expected: {testcase.Final.A1:x8}, Actual: {actualA1:x8}");
                var actualA2 = cpu.ReadAddressRegister(2);
                Assert.True(testcase.Final.A2 == actualA2, $"A2 mismatch. Expected: {testcase.Final.A2:x8}, Actual: {actualA2:x8}");
                var actualA3 = cpu.ReadAddressRegister(3);
                Assert.True(testcase.Final.A3 == actualA3, $"A3 mismatch. Expected: {testcase.Final.A3:x8}, Actual: {actualA3:x8}");
                var actualA4 = cpu.ReadAddressRegister(4);
                Assert.True(testcase.Final.A4 == actualA4, $"A4 mismatch. Expected: {testcase.Final.A4:x8}, Actual: {actualA4:x8}");
                var actualA5 = cpu.ReadAddressRegister(5);
                Assert.True(testcase.Final.A5 == actualA5, $"A5 mismatch. Expected: {testcase.Final.A5:x8}, Actual: {actualA5:x8}");
                var actualA6 = cpu.ReadAddressRegister(6);
                Assert.True(testcase.Final.A6 == actualA6, $"A6 mismatch. Expected: {testcase.Final.A6:x8}, Actual: {actualA6:x8}");

                // The M68000_SR_MASK from Rust is 0xA71F. We should only compare these bits.
                const ushort srMask = 0xA71F;
                var expectedSr = testcase.Final.Sr & srMask;
                var actualSr = (ushort)cpu.SR & srMask;
                Assert.True(expectedSr == actualSr, $"SR mismatch. Expected: ${expectedSr:x4}, Actual: ${actualSr:x4}");

                // Check stack pointers after execution
                if ((testcase.Final.Sr & 0x2000) != 0) // Is supervisor
                {
                    var actualSsp = cpu.ReadAddressRegister(7);
                    Assert.True(testcase.Final.Ssp == actualSsp, $"SSP mismatch. Expected: ${testcase.Final.Ssp:x8}, Actual: ${actualSsp:x8}");
                    var actualUsp = cpu.USP;
                    Assert.True(testcase.Final.Usp == actualUsp, $"USP mismatch. Expected: ${testcase.Final.Usp:x8}, Actual: ${actualUsp:x8}");
                }
                else
                {
                    var actualUsp = cpu.ReadAddressRegister(7);
                    Assert.True(testcase.Final.Usp == actualUsp, $"USP mismatch. Expected: ${testcase.Final.Usp:x8}, Actual: ${actualUsp:x8}");
                    var actualSsp = cpu.SSP;
                    Assert.True(testcase.Final.Ssp == actualSsp, $"SSP mismatch. Expected: ${testcase.Final.Ssp:x8}, Actual: ${actualSsp:x8}");
                }

                foreach (var ramEntry in testcase.Final.Ram)
                {
                    var address = (uint)ramEntry[0];
                    var expectedValue = (byte)ramEntry[1];
                    
                    var finalCpu = new CPU { SR = (SRFlags)testcase.Final.Sr };
                    if (finalCpu.SupervisorMode && (address >= testcase.Final.Ssp && address < testcase.Final.Ssp + 14))
                    {
                        continue;
                    }
                    var actualValue = machine.Memory.ReadByte(address);
                    Assert.True(expectedValue == actualValue, $"RAM mismatch at $0x{address:x8}. Expected: ${expectedValue:x2} ({expectedValue}), Actual: ${actualValue:x2} ({actualValue})");
                }
            }
        }
    }
}
