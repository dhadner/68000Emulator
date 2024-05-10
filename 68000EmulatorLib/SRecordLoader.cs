using PendleCodeMonkey.MC68000EmulatorLib.Enumerations;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace PendleCodeMonkey.MC68000EmulatorLib
{
    /// <summary>
    /// Partial implementation of the <see cref="Machine"/> class.
    /// </summary>
    public partial class Machine
    {
        /// <summary>
        /// Implementation of the <see cref="SRecordLoader"/> class.
        /// </summary>
        class SRecordLoader
        {
            /// <summary>
            /// Initialize a new instance of the <see cref="SRecordLoader"/> class.
            /// </summary>
            /// <param name="machine">Machine to load</param>
            /// <exception cref="ArgumentNullException"></exception>
            public SRecordLoader(Machine machine)
            {
                Machine = machine ?? throw new ArgumentNullException(nameof(machine));
            }

            /// <summary>
            /// Gets or sets the <see cref="Machine"/> instance that owns the memory to be loaded.  PC will be
            /// set to the start address if any.
            /// </summary>
            private Machine Machine { get; set; }

            /// <summary>
            /// Helper routine to parse ASCII hex.
            /// </summary>
            /// <param name="str">String containing ASCII hex</param>
            /// <param name="start">Character position to parse from (0-based)</param>
            /// <param name="length">Number of characters to parse</param>
            /// <returns>Parsed value</returns>
            private uint FromHex(string str, int start, int length)
            {
                string hex = str.Substring(start, length);
                return uint.Parse(hex, NumberStyles.HexNumber);
            }

            /// <summary>
            /// Load memory with contents of s_record file.
            /// </summary>
            /// <param name="name">Full file path</param>
            /// <param name="startingAddress">Starting execution address</param>
            /// <param name="lowestAddress">Lowest address found in file</param>
            /// <param name="highestAddress">Highest address found in file</param>
            /// <returns><c>null</c> if the data was successfully loaded, otherwise an error message.
            /// If there is an error, the addresses will be set to 0.</returns>
            internal string? Load(string name, out uint? startingAddress, out uint lowestAddress, out uint highestAddress)
            {
                int lineNumber = 0;
                uint lowAddress = 0xffffffff;
                uint highAddress = 0;
                uint? startAddress = null;
                string? errMsg = null;

                FileInfo file = new FileInfo(name);
                if (!file.Exists)
                {
                    errMsg = string.Format("File {0} does not exist.", name);
                }
                else
                {
                    IEnumerable<string> lines = File.ReadLines(name);
                    foreach (string line in lines)
                    {
                        bool eof = false;
                        uint loc = 0;
                        int index = 0;
                        int byteCount = 0;
                        int charPairs = 0;
                        uint runningSum = 0;
                        byte checksum;

                        lineNumber++;
                        if (lineNumber == 1)
                        {
                            if (line.Length < 6 || line[0] != 'S' || line[1] != '0')
                            {
                                errMsg = string.Format("First record is not 'S0': {0}", line);
                                break;
                            }
                        }
                        else
                        {
                            if (line.Length < 6 || line[0] != 'S')
                            {
                                errMsg = string.Format("Malformed S-record on line {0}: {1}", lineNumber, line);
                                break;
                            }
                            index = 2;
                            charPairs = (int)FromHex(line, index, 2);
                            if (charPairs * 2 > line.Length - 4)
                            {
                                errMsg = string.Format("Wrong byte count in line {0}: {1}", lineNumber, line);
                                break;
                            }
                            checksum = (byte)FromHex(line, index + charPairs * 2, 2); // last two characters in line are checksum byte

                            int bytes = charPairs;
                            int chkIdx = index;
                            while (bytes > 0)
                            {
                                runningSum += FromHex(line, chkIdx, 2);
                                bytes--;
                                chkIdx += 2;
                            }
                            runningSum = ~runningSum;
                            if (checksum != (byte)runningSum)
                            {
                                errMsg = string.Format("Checksum error on line {0}: {1}", lineNumber, line);
                                break;
                            }

                            index += 2;
                            char s_type = line[1];
                            switch (s_type)
                            {
                                case '0':
                                    byteCount = 0;
                                    break;
                                case '1':
                                    // 2 byte address
                                    loc = FromHex(line, index, 2 * 2);
                                    index += 2 * 2;
                                    byteCount = charPairs - 2 - 1;
                                    break;
                                case '2':
                                    // 3 byte address
                                    loc = FromHex(line, index, 3 * 2);
                                    index += 3 * 2;
                                    byteCount = charPairs - 3 - 1;
                                    break;
                                case '3':
                                    // 4 byte address
                                    loc = FromHex(line, index, 4 * 2);
                                    index += 4 * 2;
                                    byteCount = charPairs - 4 - 1;
                                    break;
                                case '5':
                                    // Count of previous S1, S2 and S3 records - ignore
                                    byteCount = 0;
                                    break;
                                case '7':
                                    // Termination with 4 byte starting address
                                    startAddress = FromHex(line, 4, 4 * 2);
                                    eof = true;
                                    break;
                                case '8':
                                    // Termination with 3 byte starting address
                                    startAddress = FromHex(line, 4, 3 * 2);
                                    eof = true;
                                    break;
                                case '9':
                                    // Termination with 2 byte starting address
                                    startAddress = FromHex(line, 4, 2 * 2);
                                    eof = true;
                                    break;
                                default:
                                    errMsg = string.Format("Unexpected record on line {0}: {1}", lineNumber, line);
                                    break;
                            }
                        }
                        if (errMsg != null || eof)
                        {
                            break;
                        }

                        if (byteCount > 0)
                        {
                            lowAddress = Math.Min(loc, lowAddress);
                            while (byteCount > 0)
                            {
                                byte b = (byte)FromHex(line, index, 2);
                                index += 2;
                                try
                                {
                                    Machine.Memory.WriteByte(loc, b);
                                }
                                catch (TrapException te)
                                {
                                    if (te.Vector != (uint)TrapVector.BusError) // Allow bus errors in order to load references to illegal addresses
                                    {
                                        throw;
                                    }
                                }
                                loc++;
                                byteCount--;
                            }
                            loc--;
                            highAddress = Math.Max(loc, highAddress);
                        }
                    }
                }
                if (errMsg == null)
                {
                    startingAddress = startAddress;
                    lowestAddress = lowAddress;
                    highestAddress = highAddress;
                }
                else
                {
                    startingAddress = null;
                    lowestAddress = 0;
                    highestAddress = 0;
                }
                return errMsg;
            }
        }
    }
}
