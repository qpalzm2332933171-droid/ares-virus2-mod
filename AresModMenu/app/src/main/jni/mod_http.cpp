#include "mod_http.h"
#include "mod_shm.h"
#include "Includes/Logger.h"

#include <arpa/inet.h>
#include <netinet/in.h>
#include <netinet/tcp.h>
#include <pthread.h>
#include <signal.h>
#include <sys/socket.h>
#include <sys/time.h>
#include <unistd.h>

#include <cctype>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <string>
#include <vector>

namespace modhttp {

static ModHttpBridge g_bridge = {};
static pthread_mutex_t g_initMtx = PTHREAD_MUTEX_INITIALIZER;
static pthread_mutex_t g_dispMtx = PTHREAD_MUTEX_INITIALIZER;
static bool g_started = false;
static int g_listenFd = -1;

static int find_header_end(const char* buf, size_t len) {
    if (len < 4) return -1;
    for (size_t i = 0; i + 3 < len; ++i) {
        if (buf[i] == '\r' && buf[i + 1] == '\n' && buf[i + 2] == '\r' && buf[i + 3] == '\n') {
            return static_cast<int>(i + 4);
        }
    }
    return -1;
}

static bool send_all(int fd, const void* data, size_t len) {
    const uint8_t* p = static_cast<const uint8_t*>(data);
    size_t sent = 0;
    while (sent < len) {
        ssize_t n = send(fd, p + sent, len - sent, MSG_NOSIGNAL);
        if (n <= 0) return false;
        sent += static_cast<size_t>(n);
    }
    return true;
}

static void send_http(int fd, int status, const uint8_t* body, uint32_t bodyLen) {
    const char* reason = (status == 200) ? "OK" : ((status == 413) ? "Payload Too Large" : "Bad Gateway");
    char hdr[256];
    int hlen = snprintf(
        hdr, sizeof(hdr),
        "HTTP/1.1 %d %s\r\n"
        "Content-Type: application/octet-stream\r\n"
        "Content-Length: %u\r\n"
        "Connection: close\r\n\r\n",
        status, reason, bodyLen);
    if (hlen > 0) send_all(fd, hdr, static_cast<size_t>(hlen));
    if (body && bodyLen > 0) send_all(fd, body, bodyLen);
}

static void handle_client(int cfd) {
    struct timeval tv;
    tv.tv_sec = 5;
    tv.tv_usec = 0;
    setsockopt(cfd, SOL_SOCKET, SO_RCVTIMEO, &tv, sizeof(tv));
    setsockopt(cfd, SOL_SOCKET, SO_SNDTIMEO, &tv, sizeof(tv));
    int nodelay = 1;
    setsockopt(cfd, IPPROTO_TCP, TCP_NODELAY, &nodelay, sizeof(nodelay));

    std::vector<char> raw(16384);
    size_t total = 0;
    int hdrEnd = -1;
    while (total < raw.size()) {
        ssize_t n = recv(cfd, raw.data() + total, raw.size() - total, 0);
        if (n <= 0) break;
        total += static_cast<size_t>(n);
        hdrEnd = find_header_end(raw.data(), total);
        if (hdrEnd >= 0) break;
    }
    if (hdrEnd < 0) {
        send_http(cfd, 502, nullptr, 0);
        close(cfd);
        return;
    }

    int32_t cmd = 0;
    uint32_t contentLen = 0;
    uint32_t routeTag = 1u;
    std::string hdrs(raw.data(), static_cast<size_t>(hdrEnd));
    size_t firstEol = hdrs.find('\n');
    if (firstEol != std::string::npos) {
        std::string reqLine = hdrs.substr(0, firstEol);
        if (reqLine.find(" /db") != std::string::npos) {
            routeTag = 2u;
        }
    }
    size_t pos = 0;
    while (pos < hdrs.size()) {
        size_t eol = hdrs.find('\n', pos);
        if (eol == std::string::npos) break;
        std::string line = hdrs.substr(pos, eol - pos);
        if (!line.empty() && line.back() == '\r') line.pop_back();
        pos = eol + 1;

        size_t colon = line.find(':');
        if (colon == std::string::npos) continue;
        std::string key = line.substr(0, colon);
        std::string val = line.substr(colon + 1);
        size_t vs = val.find_first_not_of(" \t");
        val = (vs == std::string::npos) ? "" : val.substr(vs);

        if (strcasecmp(key.c_str(), "Content-Length") == 0) {
            contentLen = static_cast<uint32_t>(strtoul(val.c_str(), nullptr, 10));
        } else if (strcasecmp(key.c_str(), "Header_Command") == 0) {
            cmd = static_cast<int32_t>(strtol(val.c_str(), nullptr, 10));
        }
    }

    if (contentLen > MOD_HTTP_MAX_REQ) {
        if (routeTag == 2u) {
            LOGE("modhttp: blocked oversized db cmd=%d len=%u", cmd, contentLen);
        }
        send_http(cfd, 413, nullptr, 0);
        close(cfd);
        return;
    }

    std::vector<uint8_t> body(contentLen);
    size_t haveBody = total - static_cast<size_t>(hdrEnd);
    if (haveBody > contentLen) haveBody = contentLen;
    if (haveBody > 0) {
        memcpy(body.data(), raw.data() + hdrEnd, haveBody);
    }
    while (haveBody < contentLen) {
        ssize_t n = recv(cfd, body.data() + haveBody, contentLen - haveBody, 0);
        if (n <= 0) break;
        haveBody += static_cast<size_t>(n);
    }
    if (haveBody < contentLen) {
        send_http(cfd, 502, nullptr, 0);
        close(cfd);
        return;
    }

    int32_t outStatus = 502;
    std::vector<uint8_t> outBody;

    pthread_mutex_lock(&g_dispMtx);
    __atomic_store_n(&g_bridge.state, MOD_HTTP_STATE_IDLE, __ATOMIC_RELEASE);
    g_bridge.cmd = cmd;
    g_bridge.httpStatus = 502;
    g_bridge.reqLen = contentLen;
    g_bridge.respLen = 0;
    g_bridge.routeTag = routeTag;
    if (contentLen > 0) {
        memcpy(g_bridge.reqBuf, body.data(), contentLen);
    }
    g_bridge.seq = g_bridge.seq + 1u;
    __atomic_store_n(&g_bridge.state, MOD_HTTP_STATE_REQ_READY, __ATOMIC_RELEASE);

    // Wait up to 25s (C# responds in <0.2ms for intercepted cmds, or async forwards)
    bool done = false;
    for (int iter = 0; iter < 25000; ++iter) {
        uint32_t st = __atomic_load_n(&g_bridge.state, __ATOMIC_ACQUIRE);
        if (st == MOD_HTTP_STATE_RESP_READY) {
            done = true;
            break;
        }
        usleep(iter < 40 ? 200 : 1000);
    }

    if (done) {
        outStatus = g_bridge.httpStatus;
        uint32_t rlen = g_bridge.respLen;
        if (rlen > MOD_HTTP_MAX_RESP) rlen = MOD_HTTP_MAX_RESP;
        outBody.resize(rlen);
        if (rlen > 0) memcpy(outBody.data(), g_bridge.respBuf, rlen);
    }
    __atomic_store_n(&g_bridge.state, MOD_HTTP_STATE_IDLE, __ATOMIC_RELEASE);
    pthread_mutex_unlock(&g_dispMtx);

    send_http(cfd, outStatus, outBody.data(), static_cast<uint32_t>(outBody.size()));
    close(cfd);
}

static void* accept_thread(void*) {
    signal(SIGPIPE, SIG_IGN);
    while (g_listenFd >= 0) {
        struct sockaddr_in cli;
        socklen_t clen = sizeof(cli);
        int cfd = accept(g_listenFd, reinterpret_cast<struct sockaddr*>(&cli), &clen);
        if (cfd < 0) {
            usleep(10000);
            continue;
        }
        handle_client(cfd);
    }
    return nullptr;
}

uint32_t start() {
    pthread_mutex_lock(&g_initMtx);
    if (g_started) {
        uint32_t p = g_bridge.port;
        pthread_mutex_unlock(&g_initMtx);
        return p;
    }

    int fd = socket(AF_INET, SOCK_STREAM, 0);
    if (fd < 0) {
        LOGE("modhttp: socket() failed");
        pthread_mutex_unlock(&g_initMtx);
        return 0;
    }
    int opt = 1;
    setsockopt(fd, SOL_SOCKET, SO_REUSEADDR, &opt, sizeof(opt));

    struct sockaddr_in addr = {};
    addr.sin_family = AF_INET;
    addr.sin_addr.s_addr = htonl(INADDR_LOOPBACK);
    addr.sin_port = htons(0);
    if (bind(fd, reinterpret_cast<struct sockaddr*>(&addr), sizeof(addr)) != 0 ||
        listen(fd, 16) != 0) {
        LOGE("modhttp: bind/listen failed");
        close(fd);
        pthread_mutex_unlock(&g_initMtx);
        return 0;
    }

    socklen_t alen = sizeof(addr);
    if (getsockname(fd, reinterpret_cast<struct sockaddr*>(&addr), &alen) != 0) {
        close(fd);
        pthread_mutex_unlock(&g_initMtx);
        return 0;
    }

    g_listenFd = fd;
    g_bridge.port = ntohs(addr.sin_port);
    g_bridge.state = MOD_HTTP_STATE_IDLE;
    g_started = true;

    pthread_t tid;
    pthread_create(&tid, nullptr, accept_thread, nullptr);
    pthread_detach(tid);

    LOGE("modhttp: listening on 127.0.0.1:%u (bridge=%p)", g_bridge.port, &g_bridge);
    uint32_t p = g_bridge.port;
    pthread_mutex_unlock(&g_initMtx);
    return p;
}

uint64_t bridge_ptr() {
    return static_cast<uint64_t>(reinterpret_cast<uintptr_t>(&g_bridge));
}

} // namespace modhttp
