# Orbit Probe (throwaway experiment)

Feasibility probe for Chango's core gesture: drag files in Explorer → tap **Shift** → radial wheel → release on an option.
Not part of the product. See `docs/research/01-orbit-probe-test-sheet.md` for the manual test procedure.

```
dotnet build experiments/OrbitProbe -c Release
experiments\OrbitProbe\bin\Release\net10.0-windows10.0.19041.0\OrbitProbe.exe              # interactive probe
experiments\OrbitProbe\bin\Release\net10.0-windows10.0.19041.0\OrbitProbe.exe --selftest D  # headless checks into folder D
experiments\OrbitProbe\bin\Release\net10.0-windows10.0.19041.0\OrbitProbe.exe --render D    # wheel renders into folder D
```

Run as a normal user. Elevated processes can't receive drops from Explorer (UIPI).
Logs go to `%LOCALAPPDATA%\OrbitProbe\`.
