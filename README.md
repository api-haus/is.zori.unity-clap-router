# [Zori] CLAP Router — Unity bindings

C# P/Invoke bindings and a Unity surface over the
[clap-ipc-client](https://github.com/api-haus/clap-ipc-client) C ABI. Drives a
CLAP plugin host across an arm's-length IPC seam: a blittable `MrEvent` mirror,
the `MusicRouterSession` conductor (graph control + realtime event push over a
lock-free ring), a switchable `Unity.Logging` config, a StreamingAssets content
catalog + build packer, and a PlayMode demo gate. **Links zero CLAP/GPL code** —
it speaks the wire protocol to the host process.

**License:** MIT. Unity `6000.4`.

## Native library

The native client `libclap_ipc_client.{so,dll,dylib}` is a build output of
[clap-ipc-client](https://github.com/api-haus/clap-ipc-client), staged into
`Plugins/<platform>/` — it is gitignored here (only the `.meta` import settings
are tracked). Build it and stage it before entering play mode. At runtime the app
also spawns a [clap-ipc](https://github.com/api-haus/clap-ipc) host process and
loads `.clap` plugins by path.

| repo | license | role |
|---|---|---|
| [music-router](https://github.com/api-haus/music-router) | MIT | wire codec |
| [clap-ipc-client](https://github.com/api-haus/clap-ipc-client) | MIT | client library + C ABI (provides the native `.so`) |
| [clap-ipc](https://github.com/api-haus/clap-ipc) | GPL-3.0 | the host, spawned at runtime |
| [is.zori.unity-clap-router](https://github.com/api-haus/is.zori.unity-clap-router) | MIT | this — Unity/C# bindings |

## Install (UPM)

```json
"is.zori.unity-clap-router": "https://github.com/api-haus/is.zori.unity-clap-router.git"
```
