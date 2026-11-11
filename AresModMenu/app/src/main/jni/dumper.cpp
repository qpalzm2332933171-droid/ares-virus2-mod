// dumper.cpp —— 在游戏进程内扫描并落盘 .NET 程序集（Assembly-CSharp.dll 等）
// 之所以放在进程里做：外部用 adb 逐区 dd 扫 /proc/pid/mem 会把宿主拖垮，
// 进程内只需一次 mmap 遍历，零 adb 往返。

#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <string>
#include <vector>
#include <unistd.h>
#include <fcntl.h>
#include <sys/mman.h>
#include <android/log.h>

#define DUMPTAG "AresDump"

static uint32_t rd32(const uint8_t* p) { uint32_t v; memcpy(&v, p, 4); return v; }

// 判断 addr 处是否是一条合法的 .NET 程序集起点：MZ -> e_lfanew -> "PE\0\0"
static bool is_pe(const uint8_t* p, size_t avail) {
    if (avail < 0x40) return false;
    if (p[0] != 'M' || p[1] != 'Z') return false;
    uint32_t lfanew = rd32(p + 0x3c);
    if (lfanew == 0 || lfanew + 4 > avail) return false;
    if (memcmp(p + lfanew, "PE\0\0", 4) != 0) return false;
    return true;
}

struct Region { uintptr_t start; size_t size; char name[64]; };

static void collect_regions(std::vector<Region>& out) {
    FILE* f = fopen("/proc/self/maps", "r");
    if (!f) return;
    char line[512];
    while (fgets(line, sizeof(line), f)) {
        unsigned long long s, e;
        char perms[8] = {0};
        char name[256] = {0};
        int n = sscanf(line, "%llx-%llx %7s %*s %*s %*s %255[^\n]", &s, &e, perms, name);
        if (n < 3) continue;
        if (perms[0] != 'r' || perms[1] != 'w') continue;   // 只要 rw
        size_t sz = (size_t)(e - s);
        if (sz < 1u << 20) continue;                        // 小于 1MB 的跳过
        if (sz > 200u << 20) continue;                      // 大于 200MB 的保留区跳过
        Region r; r.start = (uintptr_t)s; r.size = sz;
        snprintf(r.name, sizeof(r.name), "%s", name);
        out.push_back(r);
    }
    fclose(f);
}

// 扫描所有 rw 区，找 BSJB（.NET metadata 签名），向前回溯到 MZ，落盘整段
int dump_dotnet_assemblies(const char* outdir) {
    std::vector<Region> regs;
    collect_regions(regs);
    __android_log_print(ANDROID_LOG_INFO, DUMPTAG, "regions=%zu", regs.size());

    const uint8_t SIG[4] = {'B', 'S', 'J', 'B'};
    int dumped = 0;

    for (auto& r : regs) {
        const uint8_t* base = (const uint8_t*) r.start;
        size_t n = r.size;
        for (size_t i = 0; i + 4 < n; i += 4) {
            if (memcmp(base + i, SIG, 4) != 0) continue;
            // 回溯最多 8MB 找 MZ+PE
            size_t back = i < (8u << 20) ? i : (8u << 20);
            for (size_t j = i; j > i - back; j -= 4) {
                if (base[j] != 'M' || base[j + 1] != 'Z') continue;
                if (!is_pe(base + j, n - j)) continue;
                // 命中：dump [j, min(n, j+8MB))
                size_t len = (n - j) > (8u << 20) ? (8u << 20) : (n - j);
                char path[256];
                snprintf(path, sizeof(path), "%s/asm_%zx.bin", outdir, (size_t) r.start + j);
                int fd = open(path, O_CREAT | O_WRONLY | O_TRUNC, 0644);
                if (fd >= 0) {
                    ssize_t w = write(fd, base + j, len);
                    close(fd);
                    __android_log_print(ANDROID_LOG_INFO, DUMPTAG,
                                        "dumped %s len=%zu (w=%zd) from region %p", path, len, w, (void*) r.start);
                    dumped++;
                }
                break;
            }
        }
    }
    __android_log_print(ANDROID_LOG_INFO, DUMPTAG, "total dumped=%d", dumped);
    return dumped;
}
