#include <stdint.h>
#include <math.h>
#include "BrushPixels.h"
#include "AdjustPixels.h"
#include "LevelsPixels.h"
#include "NoisePixels.h"
#include "LensPixels.h"
#include "ContentFill.h"

__declspec(dllexport) int32_t compositor_content_fill(uint8_t *pixels, const uint8_t *mask, int32_t width, int32_t height) {
    return content_fill(pixels, (size_t)width * 4, mask, (size_t)width, width, height);
}

__declspec(dllexport) void compositor_lens(const uint8_t *source, uint8_t *destination, int32_t width, int32_t height, double k) {
    lens_distort(source, destination, (size_t)width, (size_t)height, (size_t)width * 4, k);
}

__declspec(dllexport) void compositor_add_noise(uint8_t *p,int32_t w,int32_t h,float amount,int32_t gaussian,int32_t monochromatic,uint32_t seed) {
    noise_add(p,(size_t)w,(size_t)h,(size_t)w*4,amount,gaussian,monochromatic,seed);
}

__declspec(dllexport) void compositor_levels(uint8_t *pixels, int32_t count, const float *tables) {
    levels_apply(pixels, (size_t)count, tables);
}

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

__declspec(dllexport) void compositor_black_white(uint8_t *pixels, int32_t count, const float *weights, int32_t tint, double hue, double saturation) {
    adjust_black_white(pixels, (size_t)count, 1, (size_t)count * 4, weights, tint, hue, saturation);
}
__declspec(dllexport) void compositor_color_balance(uint8_t *p, int32_t count, const float *s, const float *m, const float *h, int32_t preserve) {
    adjust_color_balance(p,(size_t)count,1,(size_t)count*4,s,m,h,preserve);
}
__declspec(dllexport) void compositor_grain(uint8_t *p,int32_t w,int32_t h,double amount,double size,double roughness,uint32_t seed,double x,double y,double units) {
    adjust_grain(p,(size_t)w,(size_t)h,(size_t)w*4,amount,size,roughness,seed,x,y,units);
}

__declspec(dllexport) void compositor_gradient_map(uint8_t *p, int32_t count, const uint8_t *table) {
    adjust_gradient_map(p,(size_t)count,1,(size_t)count*4,table);
}
// 33x33x33 straight RGBA cube, red fastest, matching HueSaturation.swift.
// Sampling is explicitly trilinear on CPU; alpha is preserved and applied once.
__declspec(dllexport) void compositor_hue_cube(uint8_t *p, int32_t count, const float *cube) {
    for (int32_t n=0;n<count;++n,p+=4) {
        unsigned a=p[3]; if (!a) continue;
        int lo[3], hi[3]; double t[3];
        for (int c=0;c<3;++c) {
            double x=fmin(32.0,(double)p[c]*32.0/a);
            lo[c]=(int)x; hi[c]=lo[c]<32?lo[c]+1:32; t[c]=x-lo[c];
        }
        for (int c=0;c<3;++c) {
            double sum=0;
            for (int z=0;z<2;++z) for (int y=0;y<2;++y) for (int x=0;x<2;++x) {
                int r=x?hi[0]:lo[0], g=y?hi[1]:lo[1], b=z?hi[2]:lo[2];
                double weight=(x?t[0]:1-t[0])*(y?t[1]:1-t[1])*(z?t[2]:1-t[2]);
                sum+=cube[((b*33+g)*33+r)*4+c]*weight;
            }
            p[c]=(uint8_t)fmin((double)a,fmax(0,floor(sum*a+0.5)));
        }
    }
}
