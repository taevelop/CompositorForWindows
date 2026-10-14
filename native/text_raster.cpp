#define NOMINMAX
#include <windows.h>
#include <d2d1_1.h>
#include <dwrite_1.h>
#include <wincodec.h>
#include <wrl/client.h>
#include <algorithm>
#include <cmath>
#include <cstdint>

using Microsoft::WRL::ComPtr;

namespace {
struct Apartment {
    HRESULT status = CoInitializeEx(nullptr, COINIT_MULTITHREADED);
    ~Apartment() { if (SUCCEEDED(status)) CoUninitialize(); }
};
constexpr float padding = 12;
#define CHECK_HR(operation) do { HRESULT result = (operation); if (FAILED(result)) return result; } while (0)
}

// UTF-16 input; output is top-down, 96-DPI premultiplied BGRA8. A null output
// measures only. No COM object or graphics type crosses the native boundary.
extern "C" __declspec(dllexport) HRESULT __cdecl compositor_text_raster(
    const wchar_t* content, uint32_t length, const wchar_t* family,
    uint32_t weight, uint32_t fontStyle, uint32_t stretch,
    float size, float tracking, float leading, uint32_t alignment,
    float red, float green, float blue, uint32_t boxWidth, uint32_t boxHeight,
    uint32_t* width, uint32_t* height, uint8_t* output, uint32_t outputLength)
{
    if (!content || !family || !width || !height || length > 100000 ||
        weight < 1 || weight > 999 || fontStyle > 2 || stretch < 1 || stretch > 9 || alignment > 2 ||
        !std::isfinite(size) || size < 1 || size > 2000 ||
        !std::isfinite(tracking) || tracking < -100 || tracking > 1000 ||
        !std::isfinite(leading) || leading <= 0 || leading > 5000 ||
        !std::isfinite(red) || red < 0 || red > 1 || !std::isfinite(green) || green < 0 || green > 1 ||
        !std::isfinite(blue) || blue < 0 || blue > 1 || ((boxWidth == 0) != (boxHeight == 0))) return E_INVALIDARG;
    if (boxWidth && (boxWidth < 16 || boxWidth > 30000 || boxHeight < 16 || boxHeight > 30000 ||
        uint64_t(boxWidth) * boxHeight > 100000000)) return E_INVALIDARG;

    Apartment apartment;
    if (FAILED(apartment.status) && apartment.status != RPC_E_CHANGED_MODE) return apartment.status;
    ComPtr<IDWriteFactory> factory;
    CHECK_HR(DWriteCreateFactory(DWRITE_FACTORY_TYPE_SHARED, __uuidof(IDWriteFactory), reinterpret_cast<IUnknown**>(factory.GetAddressOf())));
    ComPtr<IDWriteTextFormat> format;
    CHECK_HR(factory->CreateTextFormat(family, nullptr, DWRITE_FONT_WEIGHT(weight), DWRITE_FONT_STYLE(fontStyle), DWRITE_FONT_STRETCH(stretch), size, L"en-us", &format));
    CHECK_HR(format->SetTextAlignment(alignment == 1 ? DWRITE_TEXT_ALIGNMENT_CENTER : alignment == 2 ? DWRITE_TEXT_ALIGNMENT_TRAILING : DWRITE_TEXT_ALIGNMENT_LEADING));
    CHECK_HR(format->SetWordWrapping(DWRITE_WORD_WRAPPING_WRAP));
    // Use the selected face's ascent, retaining the user's baseline-to-baseline
    // leading even when it is smaller than the glyph height.
    float baseline = size * .8f;
    ComPtr<IDWriteFontCollection> fonts;
    CHECK_HR(factory->GetSystemFontCollection(&fonts));
    uint32_t familyIndex = 0; BOOL found = FALSE;
    CHECK_HR(fonts->FindFamilyName(family, &familyIndex, &found));
    // The managed resolver already selected a fallback when needed. Do not
    // silently select another font if WPF and DirectWrite disagree on a name.
    if (!found) return HRESULT_FROM_WIN32(ERROR_NOT_FOUND);
    if (found) {
        ComPtr<IDWriteFontFamily> fontFamily; ComPtr<IDWriteFont> font;
        CHECK_HR(fonts->GetFontFamily(familyIndex, &fontFamily));
        CHECK_HR(fontFamily->GetFirstMatchingFont(DWRITE_FONT_WEIGHT(weight), DWRITE_FONT_STRETCH(stretch), DWRITE_FONT_STYLE(fontStyle), &font));
        DWRITE_FONT_METRICS metrics{}; font->GetMetrics(&metrics);
        if (metrics.designUnitsPerEm) baseline = size * metrics.ascent / metrics.designUnitsPerEm;
    }
    CHECK_HR(format->SetLineSpacing(DWRITE_LINE_SPACING_METHOD_UNIFORM, leading, baseline));
    ComPtr<IDWriteTextLayout> layout;
    CHECK_HR(factory->CreateTextLayout(content, length, format.Get(), boxWidth ? std::max(1.f, float(boxWidth) - padding * 2) : 100000.f, 100000.f, &layout));
    ComPtr<IDWriteTextLayout1> spaced;
    CHECK_HR(layout.As(&spaced));
    CHECK_HR(spaced->SetCharacterSpacing(0, tracking, 0, DWRITE_TEXT_RANGE{0, length}));
    DWRITE_TEXT_METRICS measured{};
    CHECK_HR(layout->GetMetrics(&measured));
    double requestedWidth = boxWidth ? boxWidth : std::max(16., std::ceil(measured.widthIncludingTrailingWhitespace + padding * 2 + size * .1));
    double requestedHeight = boxHeight ? boxHeight : std::max(16., std::ceil(double(std::max(measured.height, leading)) + padding * 2));
    if (!std::isfinite(requestedWidth) || !std::isfinite(requestedHeight) || requestedWidth > 30000 || requestedHeight > 30000 || requestedWidth * requestedHeight > 100000000) return E_INVALIDARG;
    *width = uint32_t(requestedWidth); *height = uint32_t(requestedHeight);
    if (!output) return S_OK;
    uint64_t required = uint64_t(*width) * *height * 4;
    if (outputLength < required) return HRESULT_FROM_WIN32(ERROR_INSUFFICIENT_BUFFER);
    CHECK_HR(layout->SetMaxWidth(std::max(1.f, float(*width) - padding * 2)));

    ComPtr<IWICImagingFactory> wic;
    CHECK_HR(CoCreateInstance(CLSID_WICImagingFactory, nullptr, CLSCTX_INPROC_SERVER, IID_PPV_ARGS(&wic)));
    ComPtr<IWICBitmap> bitmap;
    CHECK_HR(wic->CreateBitmap(*width, *height, GUID_WICPixelFormat32bppPBGRA, WICBitmapCacheOnLoad, &bitmap));
    ComPtr<ID2D1Factory> drawing;
    CHECK_HR(D2D1CreateFactory(D2D1_FACTORY_TYPE_SINGLE_THREADED, drawing.GetAddressOf()));
    ComPtr<ID2D1RenderTarget> target;
    CHECK_HR(drawing->CreateWicBitmapRenderTarget(bitmap.Get(), D2D1::RenderTargetProperties(D2D1_RENDER_TARGET_TYPE_SOFTWARE, D2D1::PixelFormat(DXGI_FORMAT_B8G8R8A8_UNORM, D2D1_ALPHA_MODE_PREMULTIPLIED), 96, 96), &target));
    ComPtr<ID2D1SolidColorBrush> brush;
    CHECK_HR(target->CreateSolidColorBrush(D2D1::ColorF(red, green, blue, 1), &brush));
    target->SetTextAntialiasMode(D2D1_TEXT_ANTIALIAS_MODE_GRAYSCALE);
    target->BeginDraw(); target->Clear(D2D1::ColorF(0, 0, 0, 0));
    target->PushAxisAlignedClip(D2D1::RectF(padding, padding, padding + std::max(1.f, float(*width) - padding * 2), padding + std::max(1.f, float(*height) - padding * 2)), D2D1_ANTIALIAS_MODE_ALIASED);
    target->DrawTextLayout(D2D1::Point2F(padding, padding), layout.Get(), brush.Get(), D2D1_DRAW_TEXT_OPTIONS_ENABLE_COLOR_FONT);
    target->PopAxisAlignedClip(); CHECK_HR(target->EndDraw());
    CHECK_HR(bitmap->CopyPixels(nullptr, *width * 4, outputLength, output));
    return S_OK;
}
