using PendleCodeMonkey.MC68000EmulatorLib.Enumerations;

namespace PendleCodeMonkey.MC68000EmulatorLib
{
    public partial class Machine
    {
        public partial class Disassembler
        {
            /// <summary>
            /// Represents a non-executable section.
            /// </summary>
            /// <param name="address"></param>
            /// <param name="length">length of the section in bytes</param>
            /// <param name="itemOpSize"></param>
            public record NonExecutableSection
            {
                protected NonExecutableSection()
                {
                }

                public NonExecutableSection(uint address, uint length, OpSize itemOpSize = OpSize.Byte, uint itemsPerLine = 1, uint displayRadix = 16)
                {
                    Address = address;
                    Length = length;
                    ItemOpSize = itemOpSize;
                    ItemsPerLine = Math.Min(NonExecutableSections.MAX_NES_BYTES_PER_RECORD, Math.Max(1, itemsPerLine));
                    DisplayRadix = (uint)(displayRadix == 2 ? 2 : displayRadix == 10 ? 10 : 16);
                }

                public virtual uint Address { get; set; }
                public virtual uint Length { get; set; }
                public virtual OpSize ItemOpSize { get; set; }
                public virtual uint ItemsPerLine { get; set; }
                public virtual uint DisplayRadix { get; set; }

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


            public class NonExecutableSections : IEnumerable<NonExecutableSection>
            {
                public IEnumerator<NonExecutableSection> GetEnumerator()
                {
                    return _sections.GetEnumerator();
                }

                System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
                {
                    return GetEnumerator();
                }

                /// <summary>
                /// Maximum number of bytes to include in a disassembler record
                /// in a non-executable section.
                /// E.g., DC.B $01,$02,$03,$04
                ///       DC.W $0001,$0002
                ///       DC.L $00000001
                /// </summary>
                public const int MAX_NES_ITEMS_PER_RECORD = 8;
                public const int MAX_NES_BYTES_PER_RECORD = MAX_NES_ITEMS_PER_RECORD * 4;

                List<NonExecutableSection> _sections = [];

                public NonExecutableSections() { }
                public NonExecutableSections(List<NonExecutableSection> sections)
                {
                    SetSections(sections);
                }

                public NonExecutableSections(NonExecutableSections nonExecSections)
                {
                    SetSections(nonExecSections._sections);
                }

                public void SetSections(List<NonExecutableSection> sections)
                {
                    _sections = DeepCopy(sections);
                }

                public NonExecutableSections DeepCopy()
                {
                    return new NonExecutableSections(_sections);
                }

                public List<NonExecutableSection> Sections => _sections;

                private static List<NonExecutableSection> DeepCopy(List<NonExecutableSection> sections)
                {
                    List<NonExecutableSection> newSections = [];
                    foreach (var section in sections)
                    {
                        NonExecutableSection newSection = new(section.Address, section.Length, section.ItemOpSize, section.ItemsPerLine, section.DisplayRadix);
                        newSections.Add(newSection);
                    }
                    return newSections;
                }

                /// <summary>
                /// Sort the list and then walk up the list, removing duplicate sections that cover
                /// the same memory and combine those that area adjacent or overlap.
                /// If the new length of a section is incompatible with the OpSize, adjust the
                /// OpSize accordingly, including adding a small section at the end with a
                /// smaller OpSize.
                /// </summary>
                public void Normalize()
                {
                    // Sort the sections by address
                    _sections.Sort((a, b) => a.Address.CompareTo(b.Address));

                    bool adjusted;
                    try
                    {
                        // Keep cycling up through the list until we haven't adjusted any sections.
                        do
                        {
                            adjusted = false;

                            for (int i = 0; i < _sections.Count - 1; i++)
                            {
                                if (_sections[i].Address + _sections[i].Length >= _sections[i + 1].Address)
                                {

                                    // We have an adjacency or an overlap, so combine them if have the same options
                                    if (_sections[i].ItemOpSize == _sections[i + 1].ItemOpSize &&
                                        _sections[i].ItemsPerLine == _sections[i + 1].ItemsPerLine &&
                                        _sections[i].DisplayRadix == _sections[i + 1].DisplayRadix)
                                    {
                                        uint minAddress = Math.Min(_sections[i].Address, _sections[i + 1].Address);
                                        uint maxAddress = Math.Max(_sections[i].Address + _sections[i].Length, _sections[i + 1].Address + _sections[i + 1].Length);
                                        uint length = maxAddress - minAddress;
                                        NonExecutableSection merged = new(minAddress, length, _sections[i].ItemOpSize, _sections[i].ItemsPerLine, _sections[i].DisplayRadix);
                                        _sections[i + 1] = merged;
                                        _sections.RemoveAt(i);
                                        adjusted = true;
                                    }
                                    else if (_sections[i].Address + _sections[i].Length > _sections[i + 1].Address)
                                    {
                                        // Overlapping.
                                        // Make the first one shorter.  We'll further adjust this later if needed to make
                                        // the OpSize compatible with the length.
                                        _sections[i].Length = _sections[i + 1].Address - _sections[i].Address;
                                        adjusted = true;
                                    }
                                    break;
                                }
                            }
                        } while (adjusted);

                        bool needsSorting = false;
                        int index = 0;
                        while (index < _sections.Count)
                        {
                            NonExecutableSection section = _sections[index];
                            uint divisor = OpSizeToLength(section.ItemOpSize);
                            uint remainder = section.Length % divisor;
                            uint numFullOpSizes = section.Length / divisor;
                            if (remainder != 0)
                            {
                                OpSize newSize = LengthToOpSize(remainder);
                                // Add full section if any
                                if (numFullOpSizes > 0)
                                {
                                    // Adjust the original section's length to account for the new, small section to be added after
                                    section.Length -= remainder;

                                    // Add a new small section to make up the difference.
                                    NonExecutableSection sec = new(section.Address + section.Length, remainder, newSize, section.ItemsPerLine, section.DisplayRadix);
                                    _sections.Add(sec);
                                    needsSorting = true;
                                    index++;
                                }
                                else
                                {
                                    // Section is too small for the OpSize, so just change the OpSize.
                                    section.ItemOpSize = newSize;
                                }
                            }
                            index++;
                        }

                        if (needsSorting)
                        {
                            // Resort the list after adding new sections.
                            _sections.Sort((a, b) => a.Address.CompareTo(b.Address));
                        }
                    }
                    catch (Exception e)
                    {
                        Logger.Log(LogLevel.Critical, "DISASSEMBLER", () => $"NormalizeSections: {e.Message}");
                    }
                }

                public void SetNonExecutableSection(NonExecutableSection section)
                {
                    ArgumentNullException.ThrowIfNull(section);
                    if (section.Length == 0)
                    {
                        throw new ArgumentException("Section length must be greater than zero.");
                    }
                    if (section.ItemsPerLine > MAX_NES_BYTES_PER_RECORD)
                    {
                        throw new ArgumentException($"itemsPerLine must be no more than MaxNESBytesPerRecord {MAX_NES_BYTES_PER_RECORD}");
                    }
                    ClearNonExecutableRange(section.Address, section.Length);
                    _sections.Add(section);
                    Normalize();
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
                /// <param name="itemOpSize">OpSize (B, L, W)</param>
                public void SetNonExecutableRange(uint startAddress, uint length, OpSize itemOpSize = OpSize.Byte, uint itemsPerLine = 1, uint displayRadix = 16)
                {
                    if (itemsPerLine > MAX_NES_BYTES_PER_RECORD)
                    {
                        throw new ArgumentException($"itemsPerLine must be no more than MaxNESBytesPerRecord {MAX_NES_BYTES_PER_RECORD}");
                    }
                    ClearNonExecutableRange(startAddress, length);
                    _sections.Add(new(startAddress, length, itemOpSize, itemsPerLine, displayRadix));
                    Normalize();
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
                    List<NonExecutableSection> newSections = [];
                    Normalize();

                    // CASE 0: Range to be [c]leared does not intersect any [s]ections.
                    //         Nothing needs to be done.
                    //
                    //    This is handled by calculating the intersections and including
                    //    only sections that intersect in the cases below.
                    //
                    foreach (var section in _sections.Where(section => section.IntersectsWith(startAddress, length)))
                    {
                        newSections.Add(section);
                    }

                    // Sort the sections by address
                    newSections.Sort((a, b) => a.Address.CompareTo(b.Address));

                    foreach (var section in newSections)
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
                            _sections.Remove(section);
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
                            _sections.Remove(section);
                            _sections.Add(new(section.Address, startAddress - section.Address, section.ItemOpSize, section.ItemsPerLine, section.DisplayRadix));
                            _sections.Add(new(startAddress + length, nesMaxAddress - maxAddress, section.ItemOpSize, section.ItemsPerLine, section.DisplayRadix));
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
                            _sections.Remove(section);
                            _sections.Add(new(startAddress + length, nesMaxAddress - maxAddress, section.ItemOpSize, section.ItemsPerLine, section.DisplayRadix));
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
                            _sections.Remove(section);
                            _sections.Add(new(section.Address, startAddress - section.Address, section.ItemOpSize, section.ItemsPerLine, section.DisplayRadix));
                        }
                        else
                        {
                            throw new InvalidOperationException("ClearNonExecutableSectionRange: Should not happen - logic error!");
                        }
                    }
                    Normalize();
                }

                /// <summary>
                /// Clear all non-executable sections.
                /// </summary>
                public void Clear()
                {
                    _sections.Clear();
                }

                /// <summary>
                /// Determines if the current effectiveAddress is within a non-executable data block.
                /// </summary>
                /// <returns>The zero-based index of the first non-executable data block that the current effectiveAddress falls within, or null if
                /// the current effectiveAddress is within executable code.</returns>
                public NonExecutableSection? GetSectionIncluding(uint address)
                {
                    foreach (var section in _sections)
                    {
                        if (address >= section.Address && address < (section.Address + section.Length))
                        {
                            return section;
                        }
                    }

                    return null;
                }

                /// <summary>
                /// Get the next non-executable section after the given address.  Does not include
                /// sections that contain the address.
                /// </summary>
                /// <param name="address"></param>
                /// <returns>next section not containing the address or null if none found</returns>
                public NonExecutableSection? GetNextSectionAfter(uint address)
                {
                    // Sections are ordered by address ascending.
                    foreach (var section in _sections)
                    {
                        if (section.Address > address)
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
                public bool IsNonExecutable(uint address)
                {
                    return GetSectionIncluding(address) != null;
                }

            }
        }
    }
}
