#include <stdint.h>
#include <math.h>
#include "BrushPixels.h"
#include "AdjustPixels.h"

// Only fixed-width integer APIs are exported; original C long APIs stay private.
__declspec(dllexport) int32_t compositor_abi_version(void) { return 1; }
__declspec(dllexport) void compositor_alpha_bounds(const uint8_t *p, int32_t w, int32_t h, int32_t stride, int32_t *out) {
    size_t bounds[4];
    brush_alpha_bounds(p, (size_t)w, (size_t)h, (size_t)stride, bounds);
    for (int i = 0; i < 4; ++i) out[i] = (int32_t)bounds[i];
}
__declspec(dllexport) void compositor_clamp(uint8_t *p, int32_t count) { rgba_clamp_premultiplied(p, (size_t)count); }

// Continuous swept circular brush, with maximum coverage across an entire stroke.
// Unlike the Mac density brush, soft crossings do not build up additional density.
__declspec(dllexport) int32_t compositor_brush(uint8_t *output, const uint8_t *original, float *coverage,
    int32_t tileX, int32_t tileY, int32_t width, int32_t height, const double *map,
    double ax, double ay, double bx, double by, double radius, double hardness, double opacity,
    int32_t red, int32_t green, int32_t blue, int32_t erase, int32_t canvasW, int32_t canvasH) {
    double vx = bx - ax, vy = by - ay, length = vx * vx + vy * vy;
    double rr = radius * radius, inner = radius * hardness;
    int32_t changed = 0;
    for (int y = 0; y < height; ++y) {
        double px = map[0] * (tileX + 0.5) + map[2] * (tileY + y + 0.5) + map[4];
        double py = map[1] * (tileX + 0.5) + map[3] * (tileY + y + 0.5) + map[5];
        for (int x = 0; x < width; ++x, px += map[0], py += map[1]) {
            if (px < 0 || py < 0 || px >= canvasW || py >= canvasH) continue;
            double t = length > 0 ? ((px - ax) * vx + (py - ay) * vy) / length : 0;
            if (t < 0) t = 0; if (t > 1) t = 1;
            double dx = px - ax - vx * t, dy = py - ay - vy * t, distance2 = dx * dx + dy * dy;
            if (distance2 > rr) continue;
            double distance = sqrt(distance2);
            double value = distance <= inner ? 1 : (radius - distance) / fmax(radius - inner, 0.0001);
            int index = y * 256 + x;
            if (value <= coverage[index]) continue;
            coverage[index] = (float)value;
            double a = value * opacity, inverse = 1 - a;
            const uint8_t *src = original + index * 4; uint8_t *dst = output + index * 4;
            uint8_t r = (uint8_t)(src[0] * inverse + (erase ? 0 : red * a) + 0.5);
            uint8_t g = (uint8_t)(src[1] * inverse + (erase ? 0 : green * a) + 0.5);
            uint8_t b = (uint8_t)(src[2] * inverse + (erase ? 0 : blue * a) + 0.5);
            uint8_t alpha = (uint8_t)(src[3] * inverse + (erase ? 0 : 255 * a) + 0.5);
            changed |= dst[0] != r || dst[1] != g || dst[2] != b || dst[3] != alpha;
            dst[0] = r; dst[1] = g; dst[2] = b; dst[3] = alpha;
        }
    }
    return changed;
}
