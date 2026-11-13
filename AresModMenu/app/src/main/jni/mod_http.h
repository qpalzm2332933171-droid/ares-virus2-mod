#pragma once
#include <stdint.h>

namespace modhttp {

// Starts a detached 127.0.0.1:0 TCP HTTP server for CNetworkMgr interception.
// Idempotent; safe to call multiple times. Returns listening port (0 on error).
uint32_t start();

// Returns pointer to the static ModHttpBridge struct cast to uint64_t.
uint64_t bridge_ptr();

} // namespace modhttp
