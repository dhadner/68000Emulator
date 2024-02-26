# An emulator for the Motorola 68000 processor in C# #

This repo contains the code for a Motorola 68000 processor emulator library.

NOTE (dhadner): This fork has been updated for .NET 8.0.

1. Add S-record file loader.
2. Handle normal TRAPs without throwing exceptions but still use exceptions for unexpected errors.  Benchmarked against original exception-only implementation without any discernable performance difference when not handling exceptions.  Exceptions are now faster and don't clutter up output log with errors when executing normal LINEA instructions, for example.  In fact, when running Mac SE ROM there are no exceptions thrown except for a single intentional ROM test of the illegal instruction handler (which the subclassed Machine handles correctly.
3. Add Disassembler.
4. Add hooks for debiggers.
5. Fix a few opcode handlers where there were subtle errors.
6. Add ability to subclass Machine to add exception handling, trap dispatching, and interrupt capability.
6. Tested above by executing Mac SE ROM using custom debugger and display app.  ROM currently executes without apparent opcode execution errors.

<br>

The 68000Emulator solution consists of the following projects:

- **68000EmulatorConsoleTestApp**: A simple console application that demonstrates the functionality of the library.
- **68000EmulatorLib**: The code for the library itself.
- **68000Emulator.Tests**: An extensive set of tests.

<br>

### Prerequisites

- [.NET Core 3.1 SDK](https://www.microsoft.com/net/download/core)
  
<br>

### Why was this created?

For the very same reason that I wrote emulators for the Z80 and 6502 processors... to relive a little of my youth, but mainly "just for fun" :-)  
This project completes emulation of the trio of processors that I wrote code for in the 1980's and early 90's.
  
<br>

### Usage

The included **68000EmulatorConsoleTestApp** project demonstrates how to use the emulator. This application has a simple 68000 code example that it runs through the emulator.

From a developer's point of view, the emulator is used as follows:
1. Create an instance of the `Machine` class, optionally supplying the size of memory to be allocated for the emulator (in bytes) - if no size is specified then the default 16MB of memory is allocated (which is the maximum that a real 68000 processor can address).
2. Load binary executable data into the machine by calling the `LoadExecutableData` method, supplying a word array (or byte array) containing the binary data and the address at which the data should be loaded in memory.
3. Load any other binary data into the machine [if required] by calling the `LoadData` method, supplying a word array (or byte array) containing the binary data and the address at which the data should be loaded in memory. The final parameter passed to `LoadData` should be `false` to avoid clearing all memory before loading the data (otherwise any previously loaded executable data will be lost).
4. Set the initial state of the machine (e.g. register values, flags, etc.) [if required] by calling the `SetCPUState` method.
5. Call the `Execute` method to execute the loaded 68000 code.
6. Once execution has completed, the `GetCPUState` method can be called to retrieve the final state of the machine (register values, flags, etc.).
7. The `Dump` method can be called to get a string detailing the final state of the machine (which can be useful for debugging purposes).

<br>

### What next?

The following are features that are being considered for the future:  
1. Implement a disassembler.
2. Implement an assembler (because it's very tedious entering assembly language code in binary format).
3. Implement some form of interactive debugger (with features such as single stepping, breakpoint handling, etc.).

<br>

### History

| Version | Details
|---:| ---
| 1.0.0 | Initial implementation of the 68000 emulator.

