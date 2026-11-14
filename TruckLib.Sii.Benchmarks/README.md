# Measure SII parsing

Run from the repository root with an extracted SII file:

```sh
dotnet run --project TruckLib.Sii.Benchmarks -c Release -- --file /path/to/example.sii
```

To choose the number of runs and save the result:

```sh
dotnet run --project TruckLib.Sii.Benchmarks -c Release -- --file /path/to/example.sii --iterations 10 --output result.json
```

If the file includes other files, keep their folders in place. Use `--root` for
the extracted resource folder when includes start with `/` or use parent folders:

```sh
dotnet run --project TruckLib.Sii.Benchmarks -c Release -- --file /path/to/resources/def/world/example.sii --root /path/to/resources
```

The tool reads the file and its includes first. It tries a few parses before timing,
then repeats parsing from memory.
It reports the unit count, time and allocated bytes for each run, plus the middle
value across runs. Allocated bytes are not peak memory use. CPU time may show zero
for very short runs.

Timing includes decoding, comments, includes and building the result. It excludes
disk reads. A read or parse error makes the command fail. The tool does not check
whether every part of the file was read correctly or whether the game accepts it.
A zero unit count does not prove that the file is valid. It does not measure game
or plugin startup time.
