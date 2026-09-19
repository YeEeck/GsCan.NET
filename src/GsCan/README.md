# GsCan

.NET Standard 2.0 host library for gs_usb adapters (FlintCAN-FD and compatible devices).

## Native library

`candle_api.dll` is built from the LGPL-3.0-or-later Candle Windows API sources
vendored under `native/candle-api/api/` (origin: Schildkroet/CANgaroo
`src/driver/CandleApiDriver/api/`). The NuGet package ships:

- `runtimes/win-x86/native/candle_api.dll` and `runtimes/win-x64/native/candle_api.dll`
- `licenses/GPL-3.0.txt` and `licenses/LGPL-3.0.txt`
- Corresponding `api/` sources under `native-src/` (see `native-src/SOURCE.txt`)

The GPL-2 CANgaroo Qt application is **not** included.

To rebuild the native DLL on Windows:

```powershell
powershell -ExecutionPolicy Bypass -File native/candle-api/build.ps1
```
