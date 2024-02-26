rem See http://sun.hasenbraten.de/vasm/ for documentation on the assembler
rem Download http://sun.hasenbraten.de/vasm/bin/rel/vasmm68k_mot_Win64.zip 
rem  Copy vasmm68k_mot.exe to this directory
rem  Usage: a68.bat <filename>
rem    Assembles an m68000 assembly (.a68) file and generates an S-file (.h68) and a listing file (.lis).
rem    The S-file (.h68) can be read directly by the 68000 emulator's S-file loader.
rem    e.g. a68.bat proc.a86
rem         Creates proc.h68 and proc.lis

vasmm68k_mot.exe -m68000 -Fsrec -exec -o %~dp1%~n1.h68 -L %~dp1%~n1.lis %1